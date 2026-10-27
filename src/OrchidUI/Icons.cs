using System;
using System.Collections.Generic;
using System.Linq;
using OrchidUI.Protocol;

namespace OrchidUI
{
    // Pass any of these to Tab(...) / WithIcon(...). You can also pass:
    //   raw 24x24 SVG path data (filled, e.g. Material Design Icons), "stroke:" + path data (outline icons),
    //   an emoji / short text, or the name of an icon added with Icons.Register.
    public static class Icons
    {
        public const string Settings = "settings";
        public const string Home = "home";
        public const string Clock = "clock";
        public const string Stopwatch = "stopwatch";
        public const string Timer = "timer";
        public const string Calendar = "calendar";
        public const string Palette = "palette";
        public const string Font = "font";
        public const string Chat = "chat";
        public const string Mail = "mail";
        public const string Star = "star";
        public const string Heart = "heart";
        public const string People = "people";
        public const string Person = "person";
        public const string Link = "link";
        public const string Folder = "folder";
        public const string Document = "document";
        public const string Save = "save";
        public const string Flag = "flag";
        public const string Code = "code";
        public const string Lightning = "lightning";
        public const string Volume = "volume";
        public const string Microphone = "microphone";
        public const string Video = "video";
        public const string Camera = "camera";
        public const string Photo = "photo";
        public const string Shield = "shield";
        public const string Lock = "lock";
        public const string Key = "key";
        public const string Info = "info";
        public const string Warning = "warning";
        public const string Help = "help";
        public const string Edit = "edit";
        public const string List = "list";
        public const string Filter = "filter";
        public const string Sync = "sync";
        public const string Refresh = "refresh";
        public const string Reset = "reset";
        public const string Money = "money";
        public const string Gift = "gift";
        public const string Globe = "globe";
        public const string Game = "game";
        public const string Emoji = "emoji";
        public const string Wrench = "wrench";
        public const string Brightness = "brightness";
        public const string Zoom = "zoom";
        public const string Search = "search";
        public const string Play = "play";
        public const string Pause = "pause";
        public const string Eye = "eye";
        public const string Tag = "tag";
        public const string Bell = "bell";
        public const string Trophy = "trophy";
        public const string Music = "music";
        public const string Sparkle = "sparkle";
        public const string Coffee = "coffee";
        public const string Terminal = "terminal";
        public const string Database = "database";
        public const string Chart = "chart";
        public const string Robot = "robot";
        public const string Rocket = "rocket";
        public const string Plug = "plug";
        public const string Cloud = "cloud";
        public const string Megaphone = "megaphone";
        public const string Copy = "copy";
        public const string External = "external";
        public const string Import = "import";
        public const string Export = "export";
        public const string Delete = "delete";
        public const string Add = "add";
        public const string Check = "check";
        public const string Close = "close";

        public static IEnumerable<string> Names { get { return IconData.All.Keys.Where(k => !k.StartsWith("sev-") && !k.StartsWith("cap-")).OrderBy(k => k).ToArray(); } }

        // Adds (or replaces) a named icon. Path data uses a 24x24 grid unless you pass another grid size.
        public static void Register(string name, string pathData, bool stroke = false, double grid = 24, double strokeWidth = 2)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(pathData)) return;
            IconData.All[name.Trim()] = new IconDef { Data = pathData, Grid = grid, Stroke = strokeWidth, Filled = !stroke };
        }
    }

    // Turns an icon spec into path data (or text) for the host.
    internal static class IconResolver
    {
        private static readonly Dictionary<char, string> Legacy = new Dictionary<char, string>
        {
            { '', "settings" }, { '', "home" }, { '', "clock" }, { '', "stopwatch" }, { '', "calendar" },
            { '', "palette" }, { '', "font" }, { '', "chat" }, { '', "mail" }, { '', "star" },
            { '', "heart" }, { '', "people" }, { '', "person" }, { '', "link" }, { '', "folder" },
            { '', "document" }, { '', "save" }, { '', "flag" }, { '', "code" }, { '', "lightning" },
            { '', "volume" }, { '', "microphone" }, { '', "video" }, { '', "camera" }, { '', "photo" },
            { '', "shield" }, { '', "lock" }, { '', "key" }, { '', "info" }, { '', "warning" },
            { '', "edit" }, { '', "list" }, { '', "filter" }, { '', "sync" }, { '', "refresh" },
            { '', "money" }, { '', "gift" }, { '', "globe" }, { '', "game" }, { '', "emoji" },
            { '', "wrench" }, { '', "brightness" }, { '', "zoom" }, { '', "timer" }, { '', "play" },
            { '', "pause" }, { '', "eye" }, { '', "tag" }, { '', "bell" },
        };

        internal static IconDef Resolve(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return null;
            string s = spec.Trim();
            IconDef def;
            if (IconData.All.TryGetValue(s, out def)) return def;
            if (s.Length == 1)
            {
                string name;
                if (Legacy.TryGetValue(s[0], out name)) return IconData.All[name];
                return null;
            }
            if (s.StartsWith("stroke:", StringComparison.OrdinalIgnoreCase)) return new IconDef { Data = s.Substring(7) };
            if (s.StartsWith("fill:", StringComparison.OrdinalIgnoreCase)) return new IconDef { Data = s.Substring(5), Filled = true };
            if ((s[0] == 'M' || s[0] == 'm') && s.Any(char.IsDigit) && s.Length > 6) return new IconDef { Data = s, Filled = true };
            return null;
        }

        public static IconSpec ToSpec(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return null;
            var def = Resolve(spec);
            if (def == null) return new IconSpec { Text = spec.Trim() };
            return new IconSpec { Path = def.Data, Grid = def.Grid, Stroke = def.Stroke, Filled = def.Filled };
        }
    }
}
