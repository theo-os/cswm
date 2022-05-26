using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using X11;
using System.Diagnostics;
using System.Linq;

namespace CSWM
{
    public class WMCursors
    {
        public Cursor DefaultCursor;
        public Cursor FrameCursor;
        public Cursor TitleCursor;
    }

    public class WindowGroup
    {
        public Window title;
        public Window child;
        public Window frame;
    }

    public enum MouseMoveType
    {
        TitleDrag,
        TopLeftFrameDrag,
        TopRightFrameDrag,
        BottomLeftFrameDrag,
        BottomRightFrameDrag,
        RightFrameDrag,
        TopFrameDrag,
        LeftFrameDrag,
        BottomFrameDrag,
    }

    public class MouseMovement
    {
        public MouseMoveType Type { get; private set; }
        public int MotionStartX { get; set; } = 0;
        public int MotionStartY { get; set; } = 0;
        public int WindowOriginPointX { get; private set; } = 0;
        public int WindowOriginPointY { get; private set; } = 0;

        public MouseMovement(MouseMoveType type, int Motion_X, int Motion_Y, int Window_X, int Window_Y)
        {
            Type = type;
            MotionStartX = Motion_X;
            MotionStartY = Motion_Y;
            WindowOriginPointX = Window_X;
            WindowOriginPointY = Window_Y;
        }
    }

    public class WindowManager
    {
        private readonly SimpleLogger Log;
        private readonly IntPtr Display;
        private readonly Window Root;
        private readonly WMCursors Cursors = new();
        private readonly Dictionary<string, ulong> Colors = new();
        private readonly Configuration Configuration = new();
        private readonly Dictionary<Window, WindowGroup> WindowIndexByClient = new();
        private readonly Dictionary<Window, WindowGroup> WindowIndexByFrame = new();
        private readonly Dictionary<Window, WindowGroup> WindowIndexByTitle = new();
        private MouseMovement MouseMovement;

        public XErrorHandlerDelegate OnError;

        public int ErrorHandler(IntPtr display, ref XErrorEvent ev)
        {
            if (ev.error_code == 10) // BadAccess, i.e. another window manager has already claimed those privileges.
            {
                Log.Error("X11 denied access to window manager resources - another window manager is already running");
                Environment.Exit(1);
            }

            // Other runtime errors and warnings.
            var description = Marshal.AllocHGlobal(1024);
            Xlib.XGetErrorText(display, ev.error_code, description, 1024);
            var desc = Marshal.PtrToStringAnsi(description);
            Log.Warn($"X11 Error: {desc}");
            Marshal.FreeHGlobal(description);
            return 0;
        }

        public WindowManager(SimpleLogger.LogLevel level)
        {
            Log = new SimpleLogger(level);
            var pDisplayText = Xlib.XDisplayName(null);
            var DisplayText = Marshal.PtrToStringAnsi(pDisplayText);
            if (DisplayText == String.Empty)
            {
                Log.Error("No display configured for X11; check the value of the DISPLAY variable is set correctly");
                Environment.Exit(1);
            }

            Log.Info($"Connecting to X11 Display {DisplayText}");
            Display = Xlib.XOpenDisplay(null);

            if (Display == IntPtr.Zero)
            {
                Log.Error("Unable to open the default X display");
                Environment.Exit(1);
            }

            Root = Xlib.XDefaultRootWindow(Display);
            OnError = ErrorHandler;

            Xlib.XSetErrorHandler(OnError);
            // This will trigger a bad access error if another window manager is already running
            Xlib.XSelectInput(Display, Root,
                EventMask.SubstructureRedirectMask | EventMask.SubstructureNotifyMask |
                EventMask.ButtonPressMask | EventMask.KeyPressMask);

            Xlib.XSync(Display, false);

            // Setup cursors
            Cursors.DefaultCursor = Xlib.XCreateFontCursor(Display, FontCursor.XC_left_ptr);
            Cursors.TitleCursor = Xlib.XCreateFontCursor(Display, FontCursor.XC_fleur);
            Cursors.FrameCursor = Xlib.XCreateFontCursor(Display, FontCursor.XC_sizing);
            Xlib.XDefineCursor(Display, Root, Cursors.DefaultCursor);

            // Setup colours
            Configuration = Configuration.LoadFromFile("cswm.xml");

            foreach (KeyValuePair<string, string> entry in Configuration.Colors)
            {
                Colors[entry.Key] = GetPixelByName(entry.Value);
            }

            // Colors.DesktopBackground = GetPixelByName("black");
            // Colors.WindowBackground = GetPixelByName("white");
            // Colors.InactiveTitleBorder = GetPixelByName("light slate grey");
            // Colors.InactiveTitleColor = GetPixelByName("slate grey");
            // Colors.InactiveFrameColor = GetPixelByName("dark slate grey");
            // Colors.ActiveFrameColor = GetPixelByName("dark goldenrod");
            // Colors.ActiveTitleColor = GetPixelByName("gold");
            // Colors.ActiveTitleBorder = GetPixelByName("saddle brown");

            Xlib.XSetWindowBackground(Display, Root, Colors["DesktopBackground"]);
            Xlib.XClearWindow(Display, Root); // force a redraw with the new background color
        }

        private ulong GetPixelByName(string name)
        {
            var screen = Xlib.XDefaultScreen(Display);
            XColor color = new XColor();
            if (0 == Xlib.XParseColor(Display, Xlib.XDefaultColormap(Display, screen), name, ref color))
            {
                Log.Error($"Invalid Color {name}");
            }

            if (0 == Xlib.XAllocColor(Display, Xlib.XDefaultColormap(Display, screen), ref color))
            {
                Log.Error($"Failed to allocate color {name}");
            }

            return color.pixel;
        }

        private void AddFrame(Window child)
        {
            const int frame_width = 3;
            const int title_height = 20;
            const int inner_border = 1;

            if (WindowIndexByClient.ContainsKey(child))
                return; // Window has already been framed.

            var Name = string.Empty;
            Xlib.XFetchName(Display, child, ref Name);
            Log.Debug($"Framing {Name}");

            Xlib.XGetWindowAttributes(Display, child, out var attr);
            var title = Xlib.XCreateSimpleWindow(Display, Root, attr.x, attr.y, attr.width - (2 * inner_border),
                (title_height - 2 * inner_border), inner_border, Colors["InactiveTitleColor"], Colors["InactiveTitleBorder"]);

            // Try to keep the child window in the same place, unless this would push the window decorations off screen.
            var adjusted_x_loc = (attr.x - frame_width < 0) ? 0 : attr.x - frame_width;
            var adjusted_y_loc = (attr.y - (title_height + frame_width) < 0) ? 0 : (attr.y - (title_height + frame_width));

            var frame = Xlib.XCreateSimpleWindow(Display, Root, adjusted_x_loc,
                adjusted_y_loc, attr.width, attr.height + title_height,
                3, Colors["InactiveFrameColor"], Colors["WindowBackground"]);

            Xlib.XSelectInput(Display, title, EventMask.ButtonPressMask | EventMask.ButtonReleaseMask
                | EventMask.Button1MotionMask | EventMask.ExposureMask);
            Xlib.XSelectInput(Display, frame, EventMask.ButtonPressMask | EventMask.ButtonReleaseMask
                | EventMask.Button1MotionMask | EventMask.FocusChangeMask | EventMask.SubstructureRedirectMask | EventMask.SubstructureNotifyMask);

            Xlib.XDefineCursor(Display, title, Cursors.TitleCursor);
            Xlib.XDefineCursor(Display, frame, Cursors.FrameCursor);

            Xlib.XReparentWindow(Display, title, frame, 0, 0);
            Xlib.XReparentWindow(Display, child, frame, 0, title_height);
            Xlib.XMapWindow(Display, title);
            Xlib.XMapWindow(Display, frame);
            // Ensure the child window survives the untimely death of the window manager.
            Xlib.XAddToSaveSet(Display, child);

            // Grab left click events from the client, so we can focus & raise on click
            SetFocusTrap(child);

            var wg = new WindowGroup { child = child, frame = frame, title = title };
            WindowIndexByClient[child] = wg;
            WindowIndexByTitle[title] = wg;
            WindowIndexByFrame[frame] = wg;
        }

        private void RemoveFrame(Window child)
        {

            if (!WindowIndexByClient.ContainsKey(child))
            {
                return; // Do not attempt to unframe a window we have not framed.
            }
            var frame = WindowIndexByClient[child].frame;

            _ = Xlib.XUnmapWindow(Display, frame);
            Xlib.XDestroyWindow(Display, frame);

            WindowIndexByClient.Remove(child); // Cease tracking the window/frame pair.
        }

        private void SetFocusTrap(Window child)
        {
            Xlib.XGrabButton(Display, Button.LEFT, KeyButtonMask.AnyModifier, child, false,
                            EventMask.ButtonPressMask, GrabMode.Async, GrabMode.Async, 0, 0);
        }

        private void UnsetFocusTrap(Window w)
        {
            Xlib.XUngrabButton(Display, Button.LEFT, KeyButtonMask.AnyModifier, w);
        }


        private void OnMapRequest(XMapRequestEvent ev)
        {
            AddFrame(ev.window);
            _ = Xlib.XMapWindow(Display, ev.window);
        }

        private void OnButtonPressEvent(XButtonEvent ev)
        {
            var client = ev.window;
            if (WindowIndexByClient.ContainsKey(ev.window) && ev.button == (uint)Button.LEFT)
            {
                LeftClickClientWindow(ev);
            }

            else if (WindowIndexByTitle.ContainsKey(ev.window) && ev.button == (uint)Button.LEFT)
            {
                LeftClickTitleBar(ev);
                client = WindowIndexByTitle[ev.window].child;
            }

            else if (WindowIndexByFrame.ContainsKey(ev.window) && ev.button == (uint)Button.LEFT)
            {
                LeftClickFrame(ev);
                client = WindowIndexByFrame[ev.window].child;
            }

            else if (Root == ev.window && ev.button == (uint)Button.LEFT)
            {
                Xlib.XSetInputFocus(Display, Root, RevertFocus.RevertToNone, 0);
                Xlib.XRaiseWindow(Display, Root);
            }
            FocusAndRaiseWindow(client);
        }

        private void LeftClickTitleBar(XButtonEvent ev)
        {
            Window frame;
            var wg = WindowIndexByTitle[ev.window];

            frame = wg.frame;
            var child = wg.child;
            FocusAndRaiseWindow(child);
            Xlib.XGetWindowAttributes(Display, frame, out var attr);
            MouseMovement = new MouseMovement(MouseMoveType.TitleDrag, ev.x_root, ev.y_root, attr.x, attr.y);
            return;
        }

        private void LeftClickFrame(XButtonEvent ev)
        {
            Xlib.XGetWindowAttributes(Display, ev.window, out var attr);

            var control_width = (attr.width / 2) <= 40 ? attr.width / 2 : 40;
            var control_height = (attr.height / 2) <= 40 ? attr.width / 2 : 40;

            if (ev.x >= attr.width - control_width) // right side
            {
                if (ev.y >= attr.height - control_height)
                {
                    MouseMovement = new MouseMovement(MouseMoveType.BottomRightFrameDrag, ev.x_root, ev.y_root, attr.x, attr.y);
                }
                else if (ev.y <= control_height)
                {
                    MouseMovement = new MouseMovement(MouseMoveType.TopRightFrameDrag, ev.x_root, ev.y_root, attr.x, attr.y);
                }
                else
                {
                    MouseMovement = new MouseMovement(MouseMoveType.RightFrameDrag, ev.x_root, ev.y_root, attr.x, attr.y);
                }
            }
            else if (ev.x <= control_width)
            {
                if (ev.y >= attr.height - control_height)
                {
                    MouseMovement = new MouseMovement(MouseMoveType.BottomLeftFrameDrag, ev.x_root, ev.y_root, attr.x, attr.y);
                }
                else if (ev.y <= control_height)
                {
                    MouseMovement = new MouseMovement(MouseMoveType.TopLeftFrameDrag, ev.x_root, ev.y_root, attr.x, attr.y);
                }
                else
                {
                    MouseMovement = new MouseMovement(MouseMoveType.LeftFrameDrag, ev.x_root, ev.y_root, attr.x, attr.y);
                }
            }
            else if (ev.y >= attr.height / 2)
            {
                MouseMovement = new MouseMovement(MouseMoveType.BottomFrameDrag, ev.x_root, ev.y_root, attr.x, attr.y);
            }
            else
            {
                MouseMovement = new MouseMovement(MouseMoveType.TopFrameDrag, ev.x_root, ev.y_root, attr.x, attr.y);
            }
            return;
        }

        private void OnExposeEvent(XExposeEvent ev)
        {
            if (WindowIndexByTitle.ContainsKey(ev.window))
            {
                UpdateWindowTitle(ev.window);
            }
        }

        private void UpdateWindowTitle(Window titlebar)
        {
            var client = WindowIndexByTitle[titlebar].child;
            var name = String.Empty;
            if (Xlib.XFetchName(Display, client, ref name) != Status.Failure)
                Xlib.XDrawString(Display, titlebar, Xlib.XDefaultGC(Display, Xlib.XDefaultScreen(Display)), 2, 13,
                    name, name.Length);
        }

        private void LeftClickClientWindow(XButtonEvent ev)
        {
            // Release control of the left button to this application
            UnsetFocusTrap(ev.window);
            // Raise and focus it
            FocusAndRaiseWindow(ev.window);
            return;
        }

        private void FocusAndRaiseWindow(Window focus)
        {
            if (WindowIndexByClient.ContainsKey(focus))
            {
                var frame = WindowIndexByClient[focus].frame;
                Xlib.XSetInputFocus(Display, focus, RevertFocus.RevertToNone, 0);
                Xlib.XRaiseWindow(Display, frame);
            }
        }


        void OnMotionEvent(XMotionEvent ev)
        {
            if (WindowIndexByTitle.ContainsKey(ev.window))
            {
                LeftDragTitle(ev);
                return;
            }
            if (WindowIndexByFrame.ContainsKey(ev.window))
            {
                LeftDragFrame(ev);
                return;
            }
        }

        private void LeftDragTitle(XMotionEvent ev)
        {
            if (MouseMovement == null)
                return;

            // If we hit the screen edges, snap to edge
            Xlib.XGetWindowAttributes(Display, Root, out var attr);
            if (ev.y_root == attr.height - 1 // Snap to bottom
                || ev.y_root == 0 // snap to top
                || ev.x_root == attr.width - 1 // snap to right
                || ev.x_root == 0)  // snap left
            {
                var frame = WindowIndexByTitle[ev.window].frame;
                SnapFrameToEdge(frame, ev.x_root, ev.y_root, attr.width, attr.height);
                return;
            }

            // Move the window, after converting co-ordinates into offsets relative to the origin point of motion
            var new_y = ev.y_root - MouseMovement.MotionStartY;
            var new_x = ev.x_root - MouseMovement.MotionStartX;
            Xlib.XMoveWindow(Display, WindowIndexByTitle[ev.window].frame,
                MouseMovement.WindowOriginPointX + new_x, MouseMovement.WindowOriginPointY + new_y);
        }

        private void SnapFrameToEdge(Window frame, int x, int y, uint w, uint h)
        {
            var title = WindowIndexByFrame[frame].title;
            var client = WindowIndexByFrame[frame].child;

            Xlib.XGetWindowAttributes(Display, title, out var t_attr);
            var t_h = t_attr.height;
            Xlib.XGetWindowAttributes(Display, frame, out var f_attr);
            var border_w = (uint)f_attr.border_width;
            int f_y = 0, f_x = 0;

            if (x == 0 || x == w - 1)
            { // Vertical half screen sized window
                if (x == w - 1)
                    f_x = (int)w / 2;

                Xlib.XMoveResizeWindow(Display, frame, f_x, 0, w / 2, h - (2 * border_w));
                Xlib.XMoveResizeWindow(Display, title, 0, 0, w / 2, t_h);
                Xlib.XMoveResizeWindow(Display, client, 0, (int)t_h, w / 2, (h - t_h) - 2 * border_w);
            }
            else
            { // Horizontal half screen sized window
                if (y == h - 1)
                    f_y = (int)h / 2;

                Xlib.XMoveResizeWindow(Display, frame, 0, f_y, w, h / 2 - (2 * border_w));
                Xlib.XMoveResizeWindow(Display, title, 0, 0, w, t_h);
                Xlib.XMoveResizeWindow(Display, client, 0, (int)t_h, w, (h / 2) - t_h - 2 * border_w);
            }
        }

        private void LeftDragFrame(XMotionEvent ev)
        {
            var frame = ev.window;
            var title = WindowIndexByFrame[frame].title;
            var client = WindowIndexByFrame[frame].child;

            var y_delta = 0;
            var x_delta = 0;

            var w_delta = 0;
            var h_delta = 0;

            var t = MouseMovement.Type;

            // Stretch to the right, or compress left, no lateral relocation of window origin.
            if (t == MouseMoveType.RightFrameDrag
                || t == MouseMoveType.TopRightFrameDrag
                || t == MouseMoveType.BottomRightFrameDrag)
            {
                w_delta = ev.x_root - MouseMovement.MotionStartX; // width change
            }
            // Stretch down, or compress upwards, no vertical movement of the window origin.
            if (t == MouseMoveType.BottomFrameDrag
                || t == MouseMoveType.BottomRightFrameDrag
                || t == MouseMoveType.BottomLeftFrameDrag)
            {
                h_delta = ev.y_root - MouseMovement.MotionStartY;
            }
            // Combine vertical stretch with movement of the window origin.
            if (t == MouseMoveType.TopFrameDrag
                || t == MouseMoveType.TopRightFrameDrag
                || t == MouseMoveType.TopLeftFrameDrag)
            {
                h_delta = MouseMovement.MotionStartY - ev.y_root;
                y_delta = -h_delta;
            }
            // Combined left stretch with movement of the window origin
            if (t == MouseMoveType.LeftFrameDrag
                || t == MouseMoveType.TopLeftFrameDrag
                || t == MouseMoveType.BottomLeftFrameDrag)
            {
                w_delta = MouseMovement.MotionStartX - ev.x_root;
                x_delta = -w_delta;
            }

            //// Resize and move the frame
            Xlib.XGetWindowAttributes(Display, frame, out var attr);
            var new_width = (uint)(attr.width + w_delta);
            var new_height = (uint)(attr.height + h_delta);
            Xlib.XMoveResizeWindow(Display, frame, attr.x + x_delta, attr.y + y_delta, new_width, new_height);

            //// Resize and move the title bar
            Xlib.XGetWindowAttributes(Display, title, out attr);
            new_width = (uint)(attr.width + w_delta);
            new_height = (uint)attr.height;
            Xlib.XResizeWindow(Display, title, new_width, new_height);

            //// Resize and move the client window bar
            Xlib.XGetWindowAttributes(Display, client, out attr);
            new_width = (uint)(attr.width + w_delta);
            new_height = (uint)(attr.height + h_delta);
            Xlib.XResizeWindow(Display, client, new_width, new_height);

            MouseMovement.MotionStartX = ev.x_root;
            MouseMovement.MotionStartY = ev.y_root;
        }

        void OnMapNotify(XMapEvent ev)
        {
            Log.Debug($"(MapNotifyEvent) Window {ev.window} has been mapped.");
        }

        void OnConfigureRequest(XConfigureRequestEvent ev)
        {
            var changes = new XWindowChanges
            {
                x = ev.x,
                y = ev.y,
                width = ev.width,
                height = ev.height,
                border_width = ev.border_width,
                sibling = ev.above,
                stack_mode = ev.detail
            };

            if (WindowIndexByClient.ContainsKey(ev.window))
            {
                // Resize the frame
                Xlib.XConfigureWindow(Display, WindowIndexByClient[ev.window].frame, ev.value_mask, ref changes);
            }
            // Resize the window
            Xlib.XConfigureWindow(Display, ev.window, ev.value_mask, ref changes);
        }

        void OnUnmapNotify(XUnmapEvent ev)
        {
            if (ev.@event == Root)
            {
                Log.Debug($"(OnUnmapNotify) Window {ev.window} has been reparented to root");
                return;
            }
            if (!WindowIndexByClient.ContainsKey(ev.window))
                return; // Don't unmap a window we don't own.

            RemoveFrame(ev.window);
        }

        // Annoyingly, this event fires when an application quits itself, resuling in some bad window errors.
        void OnFocusOutEvent(XFocusChangeEvent ev)
        {
            var title = WindowIndexByFrame[ev.window].title;
            var frame = ev.window;
            if (Status.Failure == Xlib.XSetWindowBorder(Display, frame, Colors["InactiveTitleBorder"]))
                return; // If the windows have been destroyed asynchronously, cut this short.
            Xlib.XSetWindowBackground(Display, title, Colors["InactiveTitleColor"]);
            Xlib.XSetWindowBorder(Display, title, Colors["InactiveFrameColor"]);
            Xlib.XClearWindow(Display, title);
            UpdateWindowTitle(title);

            SetFocusTrap(WindowIndexByFrame[ev.window].child);
        }

        void OnFocusInEvent(XFocusChangeEvent ev)
        {
            var title = WindowIndexByFrame[ev.window].title;
            var frame = ev.window;
            Xlib.XSetWindowBorder(Display, frame, Colors["ActiveFrameColor"]);
            _ = Xlib.XSetWindowBackground(Display, title, Colors["ActiveTitleColor"]);
            Xlib.XSetWindowBorder(Display, title, Colors["ActiveTitleBorder"]);
            _ = Xlib.XClearWindow(Display, title); // Force colour update

            UpdateWindowTitle(title); //Redraw the title, purged by clearing.
        }

        void OnDestroyNotify(XDestroyWindowEvent ev)
        {
            if (WindowIndexByClient.ContainsKey(ev.window))
                WindowIndexByClient.Remove(ev.window);
            else if (WindowIndexByFrame.ContainsKey(ev.window))
                WindowIndexByFrame.Remove(ev.window);
            else if (WindowIndexByTitle.ContainsKey(ev.window))
                WindowIndexByTitle.Remove(ev.window);
            Log.Debug($"(OnDestroyNotify) Destroyed {ev.window}");
        }

        private static void OnReparentNotify(XReparentEvent ev)
        {
            return; // Never seems to be interesting and is often duplicated.
        }

        void OnCreateNotify(XCreateWindowEvent ev)
        {
            Log.Debug($"(OnCreateNotify) Created event {ev.window}, parent {ev.parent}");
        }

        public int Run()
        {
            IntPtr ev = Marshal.AllocHGlobal(24 * sizeof(long));
            Window ReturnedParent = 0, ReturnedRoot = 0;

            Xlib.XGrabServer(Display); // Lock the server during initialization
            Xlib.XQueryTree(Display, Root, ref ReturnedRoot, ref ReturnedParent,
                out var ChildWindows);

            Log.Debug($"Reparenting and framing pre-existing child windows: {ChildWindows.Count}");
            for (var i = 0; i < ChildWindows.Count; i++)
            {
                Log.Debug($"Framing child {i}, {ChildWindows[i]}");
                AddFrame(ChildWindows[i]);
            }
            Xlib.XUngrabServer(Display); // Release the lock on the server.

            var pDisplayText = Xlib.XDisplayName(null);
            var DisplayText = Marshal.PtrToStringAnsi(pDisplayText);

            while (true)
            {
                Xlib.XNextEvent(Display, ev);
                var xevent = Marshal.PtrToStructure<XAnyEvent>(ev);

                switch (xevent.type)
                {
                    case (int)Event.KeyPress:
                        var keyPressEvent = Marshal.PtrToStructure<XKeyEvent>(ev);

                        if (keyPressEvent.type == 2)
                        {
                            // TODO: control keys (eg. ctrl, super, alt)
                            if (keyPressEvent.keycode == 9)
                            {
                                // 'escape'
                                Console.WriteLine("User requested exit");
                                Marshal.FreeHGlobal(ev);
                                return 0;
                            }


                            foreach (var action in Configuration.KeyActions)
                            {
                                if (keyPressEvent.keycode == action.KeyCode)
                                {

                                    // Console.WriteLine($"keycode: {keyPressEvent.keycode}");
                                    // foreach (var mod in action.Mods)
                                    // {

                                    //     Console.WriteLine($"{mod} is pressed: {(keyPressEvent.state & ((int)mod)) != 0}");
                                    // }

                                    if (action.Mods.All(mod =>
                                        (keyPressEvent.state & ((int)mod)) != 0
                                    ))
                                    {
                                        Console.WriteLine($"Starting {action.Program}: {action.Arguments.Aggregate("", (acc, x) => $"{acc} {x}")}");
                                        Process cmd = new();
                                        cmd.StartInfo.FileName = action.Program;
                                        foreach (string x in action.Arguments)
                                            cmd.StartInfo.ArgumentList.Add(x);
                                        foreach (KeyValuePair<string, string> env in action.Environment)
                                            cmd.StartInfo.Environment[env.Key] = env.Value;
                                        cmd.StartInfo.Environment["DISPLAY"] = DisplayText;
                                        cmd.StartInfo.CreateNoWindow = true;
                                        cmd.StartInfo.UseShellExecute = false;
                                        cmd.StartInfo.RedirectStandardError = true;
                                        cmd.Start();
                                    }

                                }
                            }
                        }
                        break;
                    case (int)Event.DestroyNotify:
                        var destroy_event = Marshal.PtrToStructure<XDestroyWindowEvent>(ev);
                        OnDestroyNotify(destroy_event);
                        break;
                    case (int)Event.CreateNotify:
                        var create_event = Marshal.PtrToStructure<XCreateWindowEvent>(ev);
                        OnCreateNotify(create_event);
                        break;
                    case (int)Event.MapNotify:
                        var map_notify = Marshal.PtrToStructure<XMapEvent>(ev);
                        OnMapNotify(map_notify);
                        break;
                    case (int)Event.MapRequest:
                        var map_event = Marshal.PtrToStructure<XMapRequestEvent>(ev);
                        OnMapRequest(map_event);
                        break;
                    case (int)Event.ConfigureRequest:
                        var cfg_event = Marshal.PtrToStructure<XConfigureRequestEvent>(ev);
                        OnConfigureRequest(cfg_event);
                        break;
                    case (int)Event.UnmapNotify:
                        var unmap_event = Marshal.PtrToStructure<XUnmapEvent>(ev);
                        OnUnmapNotify(unmap_event);
                        break;
                    case (int)Event.ReparentNotify:
                        var reparent_event = Marshal.PtrToStructure<XReparentEvent>(ev);
                        OnReparentNotify(reparent_event);
                        break;
                    case (int)Event.ButtonPress:
                        var button_press_event = Marshal.PtrToStructure<XButtonEvent>(ev);
                        OnButtonPressEvent(button_press_event);
                        break;
                    case (int)Event.ButtonRelease:
                        MouseMovement = null;
                        break;
                    case (int)Event.MotionNotify:
                        // We only want the newest motion event in order to reduce perceived lag
                        while (Xlib.XCheckMaskEvent(Display, EventMask.Button1MotionMask, ev)) { /* skip over */ }
                        var motion_event = Marshal.PtrToStructure<XMotionEvent>(ev);
                        OnMotionEvent(motion_event);
                        break;
                    case (int)Event.FocusOut:
                        var focus_out_event = Marshal.PtrToStructure<XFocusChangeEvent>(ev);
                        OnFocusOutEvent(focus_out_event);
                        break;
                    case (int)Event.FocusIn:
                        var focus_in_event = Marshal.PtrToStructure<XFocusChangeEvent>(ev);
                        OnFocusInEvent(focus_in_event);
                        break;
                    case (int)Event.ConfigureNotify:
                        break;
                    case (int)Event.Expose:
                        var expose_event = Marshal.PtrToStructure<XExposeEvent>(ev);
                        OnExposeEvent(expose_event);
                        break;
                    default:
                        Log.Debug($"Event type: {Enum.GetName(typeof(Event), xevent.type)}");
                        break;
                }
            }
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var WM = new WindowManager(SimpleLogger.LogLevel.Info);
            WM.Run();
        }
    }
}

