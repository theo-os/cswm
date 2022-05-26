using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CSWM
{

    // public class ColorSettings
    // {
    //     public ulong ActiveFrameColor;
    //     public ulong ActiveTitleColor;
    //     public ulong ActiveTitleBorder;
    //     public ulong InactiveFrameColor;
    //     public ulong InactiveTitleColor;
    //     public ulong InactiveTitleBorder;
    //     public ulong DesktopBackground;
    //     public ulong WindowBackground;
    // }

    public class KeyAction
    {
        public int KeyCode;
        public IEnumerable<ModKey> Mods;
        public string Program;
        public IEnumerable<string> Arguments;
        public Dictionary<string, string> Environment;
    }

    public class Configuration
    {
        public Dictionary<string, string> Colors;
        public IEnumerable<KeyAction> KeyActions;

        public static Configuration LoadFromFile(string file)
        {
            XDocument doc = XDocument.Load(file);
            XElement root = doc.Element("config");
            Configuration config = new()
            {
                Colors = root.Element("colors").Elements().ToDictionary(
                    x => x.Attribute("name").Value,
                    x => x.Value
                ),
                KeyActions = root.Element("key-actions").Elements().Select(
                    x =>
                    new KeyAction()
                    {
                        KeyCode = int.Parse(x.Attribute("code").Value),
                        Program = x.Element("program").Value,
                        Mods = x
                        .Attribute("mod")
                        .Value
                        .Split(",")
                        .Where(x => !string.IsNullOrEmpty(x))
                        .Select(x =>
                        {
                            if (Enum.TryParse(x, out ModKey mod))
                                return mod;
                            else return ModKey.None;
                        }),
                        Arguments = x
                        .Element("arguments")
                        .Elements()
                        .Select(x => x.Value),
                        Environment = new(),
                    }
                ),
            };

            return config;
        }
    }
}