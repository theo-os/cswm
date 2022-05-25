using System;
using System.Runtime.InteropServices;
using X11;

/**
WIP CSWM OpenGL Context
**/

namespace CSWM
{
    static class GLX
    {
        [DllImport("libX11", EntryPoint = "XOpenDisplay", ExactSpelling = true)]
        public static extern IntPtr XOpenDisplay(IntPtr display_name);

        [DllImport("libX11", EntryPoint = "XDefaultScreen", ExactSpelling = true)]
        public static extern int XDefaultScreen(IntPtr dpy);

        [DllImport("libGL", EntryPoint = "glXChooseVisual", ExactSpelling = true)]
        public static extern IntPtr ChooseVisual(IntPtr dpy, int screen, int[] attribList);

        [DllImport("libGL", EntryPoint = "glXCreateContext", ExactSpelling = true)]
        public static extern IntPtr CreateContext(IntPtr dpy, IntPtr visual, IntPtr shareList, bool direct);

        [DllImport("libGL", EntryPoint = "glXMakeCurrent", ExactSpelling = true)]
        public static extern IntPtr MakeCurrent(IntPtr dpy, Window drawable, IntPtr glContext);

        public const int RGBA = 4;
        public const int DOUBLEBUFFER = 5;
        public const int RED_SIZE = 8;
        public const int GREEN_SIZE = 9;
        public const int BLUE_SIZE = 10;
        public const int ALPHA_SIZE = 11;
        public const int DEPTH_SIZE = 12;
        public const int None = 0x8000;
    }

    public class WMGL
    {
        public class GLWindowContext
        {
            public Window window;
            public IntPtr glContext;

            public void Show(IntPtr display)
            {
                var res = Xlib.XMapWindow(display, window);

                if (res != 0) throw new Exception($"Error showing window: code {res}");
            }
        }

        public static GLWindowContext CreateWindow(IntPtr display, Window parent, int[] bounds, uint borderWidth, ulong borderColor, ulong backgroundColor)
        {
            var win = Xlib.XCreateSimpleWindow(display, parent, bounds[0], bounds[1], (uint)bounds[2], (uint)bounds[3], borderWidth, borderColor, backgroundColor);

            var screen = GLX.XDefaultScreen(display);

            // Set BackBuffer format
            int[] attrListDbl =
            {
                GLX.RGBA,
                GLX.DOUBLEBUFFER,
                GLX.RED_SIZE, 8,
                GLX.GREEN_SIZE, 8,
                GLX.BLUE_SIZE, 8,
                GLX.DEPTH_SIZE, 16,
                0
            };

            var visual = GLX.ChooseVisual(display, screen, attrListDbl);
            if (visual == IntPtr.Zero)
            {
                int[] attrListSgl =
                {
                    GLX.RGBA,
                    GLX.RED_SIZE, 8,
                    GLX.GREEN_SIZE, 8,
                    GLX.BLUE_SIZE, 8,
                    GLX.DEPTH_SIZE, 16,
                    0
                };

                visual = GLX.ChooseVisual(display, screen, attrListSgl);
            }

            if (visual == IntPtr.Zero)
            {
                Console.WriteLine("Failed to get visual.");
            }
            else
            {
                Console.WriteLine("Yahoo.");
            }

            var ctx = GLX.CreateContext(display, visual, new IntPtr(0), true);
            GLX.MakeCurrent(display, win, ctx);

            return new GLWindowContext()
            {
                glContext = ctx,
                window = win,
            };
        }
    }
}