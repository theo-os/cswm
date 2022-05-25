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
        public string Program;
        public IEnumerable<string> Arguments;
        public Dictionary<string, string> Environment;
    }

    public class Configuration
    {
        public Dictionary<string, string> Colors;
        public Dictionary<int, KeyAction> KeyActions;

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
                KeyActions = root.Element("key-actions").Elements().ToDictionary(
                    x => int.Parse(x.Attribute("code").Value),
                    // program name
                    x => new KeyAction()
                    {
                        Program = x.Element("program").Value,
                        Arguments = x.Element("arguments").Elements().Select(x => x.Value),
                        Environment = new(),
                    }
                ),
            };

            return config;
        }
    }
}