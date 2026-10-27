using System;
using System.Collections.Generic;
using System.Globalization;

namespace OrchidUI
{
    internal sealed class IconDef
    {
        public string Data;
        public double Grid = 24;
        public double Stroke = 2;
        public bool Filled;
    }

    // Built-in icons drawn as vector paths on a 24x24 grid (round caps/joins).
    // Vector paths render identically on Windows and Wine, unlike icon fonts.
    internal static class IconData
    {
        public static readonly Dictionary<string, IconDef> All = new Dictionary<string, IconDef>(StringComparer.OrdinalIgnoreCase);

        private static string N(double v) { return v.ToString("0.##", CultureInfo.InvariantCulture); }

        private static string Circle(double cx, double cy, double r)
        {
            return "M" + N(cx + r) + "," + N(cy) + " A" + N(r) + "," + N(r) + " 0 1 1 " + N(cx - r) + "," + N(cy)
                 + " A" + N(r) + "," + N(r) + " 0 1 1 " + N(cx + r) + "," + N(cy) + " Z";
        }

        private static void Add(string name, string data, double stroke = 2) { All[name] = new IconDef { Data = data, Stroke = stroke }; }
        private static void Alias(string alias, string name) { All[alias] = All[name]; }

        static IconData()
        {
            // UI
            Add("chevron-down", "M6,9 L12,15 L18,9");
            Add("chevron-up", "M6,15 L12,9 L18,15");
            Add("chevron-right", "M9,6 L15,12 L9,18");
            Add("chevron-left", "M15,6 L9,12 L15,18");
            Add("close", "M6,6 L18,18 M18,6 L6,18");
            Add("add", "M12,5 V19 M5,12 H19");
            Add("minus", "M5,12 H19");
            Add("check", "M5,12.5 L10,17.5 L19,7");
            Add("menu", "M4,6.5 H20 M4,12 H20 M4,17.5 H20");
            Add("search", Circle(11, 11, 7) + " M16.2,16.2 L20.5,20.5");
            Add("delete", "M4,7 H20 M10,11 V17 M14,11 V17 M6,7 L7,19 A2,2 0 0 0 9,21 H15 A2,2 0 0 0 17,19 L18,7 M9,7 V4.5 A1.5,1.5 0 0 1 10.5,3 H13.5 A1.5,1.5 0 0 1 15,4.5 V7");
            Add("arrow-up", "M12,19 V5 M5.5,11.5 L12,5 L18.5,11.5");
            Add("arrow-down", "M12,5 V19 M5.5,12.5 L12,19 L18.5,12.5");
            Add("refresh", "M18.93,8 A8,8 0 1 1 12,4 M9,1 L12,4 L9,7");
            Add("reset", "M5.07,8 A8,8 0 1 0 12,4 M15,1 L12,4 L15,7");
            Add("sync", "M4,11 A8,8 0 0 1 18.5,7.5 M19,3 V8 H14 M20,13 A8,8 0 0 1 5.5,16.5 M5,21 V16 H10");
            Add("import", "M12,3 V15 M7,10 L12,15 L17,10 M4,15 V19 A2,2 0 0 0 6,21 H18 A2,2 0 0 0 20,19 V15");
            Add("export", "M12,15 V3 M7,8 L12,3 L17,8 M4,15 V19 A2,2 0 0 0 6,21 H18 A2,2 0 0 0 20,19 V15");
            Add("external", "M14,4 H20 V10 M20,4 L11,13 M18,14 V19 A1,1 0 0 1 17,20 H5 A1,1 0 0 1 4,19 V7 A1,1 0 0 1 5,6 H10");
            Add("more", Circle(5, 12, 1) + " " + Circle(12, 12, 1) + " " + Circle(19, 12, 1));
            Add("copy", "M9,9 H19 A1,1 0 0 1 20,10 V20 A1,1 0 0 1 19,21 H9 A1,1 0 0 1 8,20 V10 A1,1 0 0 1 9,9 Z M16,9 V5 A1,1 0 0 0 15,4 H5 A1,1 0 0 0 4,5 V15 A1,1 0 0 0 5,16 H8");
            Add("help", Circle(12, 12, 9) + " M9.5,9.5 A2.5,2.5 0 1 1 13,11.8 C12.3,12.2 12,12.7 12,13.5 V14 M12,17 V17.1");

            // General
            Add("settings", "M10.19,5.24 L10.69,2.69 L13.31,2.69 L13.81,5.24 L15.50,5.94 L17.66,4.49 L19.51,6.34 L18.06,8.50 L18.76,10.19 L21.31,10.69 L21.31,13.31 L18.76,13.81 L18.06,15.50 L19.51,17.66 L17.66,19.51 L15.50,18.06 L13.81,18.76 L13.31,21.31 L10.69,21.31 L10.19,18.76 L8.50,18.06 L6.34,19.51 L4.49,17.66 L5.94,15.50 L5.24,13.81 L2.69,13.31 L2.69,10.69 L5.24,10.19 L5.94,8.50 L4.49,6.34 L6.34,4.49 L8.50,5.94 Z " + Circle(12, 12, 3), 1.7);
            Add("home", "M3.5,11 L12,3.5 L20.5,11 M5.5,9.5 V19 A1,1 0 0 0 6.5,20 H10 V14.5 H14 V20 H17.5 A1,1 0 0 0 18.5,19 V9.5");
            Add("clock", Circle(12, 12, 9) + " M12,7 V12 L15.5,14");
            Add("stopwatch", Circle(12, 13.5, 7.5) + " M9.5,2.5 H14.5 M12,2.5 V6 M12,13.5 L15,10.5 M18.3,7.2 L19.8,5.7");
            Add("timer", "M6.5,3 H17.5 M6.5,21 H17.5 M7.5,3 C7.5,8 12,9 12,12 C12,15 7.5,16 7.5,21 M16.5,3 C16.5,8 12,9 12,12 C12,15 16.5,16 16.5,21");
            Add("calendar", "M5,5 H19 A2,2 0 0 1 21,7 V19 A2,2 0 0 1 19,21 H5 A2,2 0 0 1 3,19 V7 A2,2 0 0 1 5,5 Z M3,10 H21 M8,3 V7 M16,3 V7");
            Add("palette", "M12,3 A9,9 0 0 0 12,21 C13.5,21 14.2,20.2 14.2,19.2 C14.2,17.8 13,17.4 13,16.2 C13,15.1 13.9,14.5 15,14.5 H17 A4,4 0 0 0 21,10.5 C21,6.4 17,3 12,3 Z "
                + Circle(7.5, 11.5, 0.9) + " " + Circle(9.5, 7.5, 0.9) + " " + Circle(14.5, 7.5, 0.9));
            Add("font", "M5,6.5 V4 H19 V6.5 M12,4 V20 M9,20 H15");
            Add("chat", "M5,4.5 H19 A2,2 0 0 1 21,6.5 V15 A2,2 0 0 1 19,17 H10 L5.5,20.5 V17 H5 A2,2 0 0 1 3,15 V6.5 A2,2 0 0 1 5,4.5 Z M7.5,9 H16.5 M7.5,12.5 H13.5");
            Add("mail", "M4.5,5 H19.5 A1.5,1.5 0 0 1 21,6.5 V17.5 A1.5,1.5 0 0 1 19.5,19 H4.5 A1.5,1.5 0 0 1 3,17.5 V6.5 A1.5,1.5 0 0 1 4.5,5 Z M3.5,6.5 L12,13 L20.5,6.5");
            Add("star", "M12,3.3 L14.41,9.48 L21.04,9.86 L15.9,14.07 L17.58,20.49 L12,16.9 L6.42,20.49 L8.1,14.07 L2.96,9.86 L9.59,9.48 Z");
            Add("heart", "M12,20.5 C5,16 2.5,12 2.5,8.5 C2.5,5.5 5,3.5 7.5,3.5 C9.5,3.5 11,4.6 12,6.2 C13,4.6 14.5,3.5 16.5,3.5 C19,3.5 21.5,5.5 21.5,8.5 C21.5,12 19,16 12,20.5 Z");
            Add("people", Circle(9, 7.5, 3.5) + " M2.5,20 C2.5,16 5.5,14 9,14 C12.5,14 15.5,16 15.5,20 M15.5,4.3 A3.5,3.5 0 0 1 15.5,10.7 M18,14.3 C20.2,15 21.5,17 21.5,20");
            Add("person", Circle(12, 7.5, 4) + " M4.5,20.5 C4.5,16.5 7.8,14.5 12,14.5 C16.2,14.5 19.5,16.5 19.5,20.5");
            Add("link", "M10,13.5 A4,4 0 0 0 15.66,13.5 L18.5,10.66 A4,4 0 0 0 12.84,5 L11.4,6.4 M14,10.5 A4,4 0 0 0 8.34,10.5 L5.5,13.34 A4,4 0 0 0 11.16,19 L12.6,17.6");
            Add("folder", "M3,7 A2,2 0 0 1 5,5 H9.5 L11.5,7 H19 A2,2 0 0 1 21,9 V17 A2,2 0 0 1 19,19 H5 A2,2 0 0 1 3,17 Z");
            Add("document", "M14,3 H7 A2,2 0 0 0 5,5 V19 A2,2 0 0 0 7,21 H17 A2,2 0 0 0 19,19 V8 Z M14,3 V8 H19 M9,13 H15 M9,17 H13");
            Add("save", "M5,3 H16 L21,8 V19 A2,2 0 0 1 19,21 H5 A2,2 0 0 1 3,19 V5 A2,2 0 0 1 5,3 Z M7.5,3 V8 H15 V3 M7,21 V14 H17 V21");
            Add("flag", "M5,21 V4 M5,4 C8,2.5 10,5.5 13,4.5 C15,3.8 17,3.5 19,4.5 V13.5 C17,12.5 15,12.8 13,13.5 C10,14.5 8,11.5 5,13");
            Add("code", "M8,7 L3,12 L8,17 M16,7 L21,12 L16,17 M13.5,5 L10.5,19");
            Add("lightning", "M13,2.5 L4.5,13.5 H11.5 L10.5,21.5 L19.5,10.5 H12.5 Z");
            Add("volume", "M4,9.5 H7.5 L12,5.5 V18.5 L7.5,14.5 H4 Z M15.5,9 A4,4 0 0 1 15.5,15 M18,6.5 A7.5,7.5 0 0 1 18,17.5");
            Add("microphone", "M12,3 A3,3 0 0 1 15,6 V12 A3,3 0 0 1 9,12 V6 A3,3 0 0 1 12,3 Z M5.5,11 A6.5,6.5 0 0 0 18.5,11 M12,17.5 V21 M8.5,21 H15.5");
            Add("video", "M4,6 H14 A2,2 0 0 1 16,8 V16 A2,2 0 0 1 14,18 H4 A2,2 0 0 1 2,16 V8 A2,2 0 0 1 4,6 Z M16,10.5 L22,7 V17 L16,13.5");
            Add("camera", "M4,7 H7.5 L9.5,4.5 H14.5 L16.5,7 H20 A1.5,1.5 0 0 1 21.5,8.5 V18.5 A1.5,1.5 0 0 1 20,20 H4 A1.5,1.5 0 0 1 2.5,18.5 V8.5 A1.5,1.5 0 0 1 4,7 Z " + Circle(12, 13, 3.5));
            Add("photo", "M5,4 H19 A2,2 0 0 1 21,6 V18 A2,2 0 0 1 19,20 H5 A2,2 0 0 1 3,18 V6 A2,2 0 0 1 5,4 Z M3,16 L8,11 L13,16 M12,15 L15,12 L21,18 " + Circle(15.5, 8.5, 1.5));
            Add("shield", "M12,3 L19.5,6 V11.5 C19.5,16 16.5,19.5 12,21 C7.5,19.5 4.5,16 4.5,11.5 V6 Z");
            Add("lock", "M6,11 H18 A1,1 0 0 1 19,12 V20 A1,1 0 0 1 18,21 H6 A1,1 0 0 1 5,20 V12 A1,1 0 0 1 6,11 Z M8,11 V7.5 A4,4 0 0 1 16,7.5 V11");
            Add("key", Circle(8, 16, 4) + " M10.9,13.1 L20,4 M17.5,6.5 L20,9 M15,9 L17,11");
            Add("info", Circle(12, 12, 9) + " M12,11 V16.5 M12,7.6 V7.7");
            Add("warning", "M12,3.5 L21.5,20 H2.5 Z M12,9.5 V14 M12,17 V17.1");
            Add("edit", "M15.5,4.5 L19.5,8.5 L8.5,19.5 H4.5 V15.5 Z M13,7 L17,11");
            Add("list", "M9,6 H20 M9,12 H20 M9,18 H20 " + Circle(4.8, 6, 0.6) + " " + Circle(4.8, 12, 0.6) + " " + Circle(4.8, 18, 0.6));
            Add("filter", "M3.5,5 H20.5 L14,12.5 V19 L10,21 V12.5 Z");
            Add("money", "M3,6.5 H21 V17.5 H3 Z " + Circle(12, 12, 2.5) + " M6.5,9.5 V9.6 M17.5,14.4 V14.5");
            Add("gift", "M4,8.5 H20 V12 H4 Z M5.5,12 V20 H18.5 V12 M12,8.5 V20 M12,8.5 C10.5,5 7.5,4 7,6 C6.5,8 9.5,8.5 12,8.5 C14.5,8.5 17.5,8 17,6 C16.5,4 13.5,5 12,8.5");
            Add("globe", Circle(12, 12, 9) + " M3.5,9 H20.5 M3.5,15 H20.5 M12,3 C9,6 9,18 12,21 M12,3 C15,6 15,18 12,21");
            Add("game", "M7,7.5 H17 A4.5,4.5 0 0 1 21.5,12 V14.5 A3,3 0 0 1 16.5,16.5 L15,15 H9 L7.5,16.5 A3,3 0 0 1 2.5,14.5 V12 A4.5,4.5 0 0 1 7,7.5 Z M7.5,10.5 V13.5 M6,12 H9 M15.5,11 V11.1 M17.5,13 V13.1");
            Add("emoji", Circle(12, 12, 9) + " M8.5,14.5 C9.5,16 10.7,16.5 12,16.5 C13.3,16.5 14.5,16 15.5,14.5 M9,9.5 V10 M15,9.5 V10");
            Add("wrench", "M14.7,3.3 A5,5 0 0 0 10.2,10.4 L3.9,16.7 A2,2 0 0 0 6.7,19.5 L13,13.2 A5,5 0 0 0 20.1,8.7 L17.2,11.6 L13.9,10.1 L12.4,6.8 Z");
            Add("brightness", Circle(12, 12, 3.6) + " M18.6,12 L21.2,12 M16.67,16.67 L18.51,18.51 M12,18.6 L12,21.2 M7.33,16.67 L5.49,18.51 M5.4,12 L2.8,12 M7.33,7.33 L5.49,5.49 M12,5.4 L12,2.8 M16.67,7.33 L18.51,5.49");
            Add("zoom", Circle(11, 11, 7) + " M16.2,16.2 L20.5,20.5 M8,11 H14 M11,8 V14");
            Add("play", "M7,4.5 L19,12 L7,19.5 Z");
            Add("pause", "M8,5 V19 M16,5 V19", 2.6);
            Add("eye", "M2.5,12 C5,7 8.5,5 12,5 C15.5,5 19,7 21.5,12 C19,17 15.5,19 12,19 C8.5,19 5,17 2.5,12 Z " + Circle(12, 12, 3));
            Add("tag", "M3.5,4.5 A1,1 0 0 1 4.5,3.5 H11.5 L20.5,12.5 L12.5,20.5 L3.5,11.5 Z " + Circle(8, 8, 1.2));
            Add("bell", "M6,16.5 V11 A6,6 0 0 1 18,11 V16.5 L19.5,18 H4.5 Z M10,20.5 C10.5,21.2 11.2,21.5 12,21.5 C12.8,21.5 13.5,21.2 14,20.5");
            Add("trophy", "M8,4 H16 V10 A4,4 0 0 1 8,10 Z M8,6 H5 V7.5 A3,3 0 0 0 8,10.5 M16,6 H19 V7.5 A3,3 0 0 1 16,10.5 M12,14 V18 M8.5,20.5 H15.5 M10,18 H14 L14.5,20.5 H9.5 Z");
            Add("music", "M9,18 V5.5 L20,3.5 V16 " + Circle(6.5, 18, 2.5) + " " + Circle(17.5, 16, 2.5));
            Add("sparkle", "M12,3 Q13,11 21,12 Q13,13 12,21 Q11,13 3,12 Q11,11 12,3 Z");
            Add("coffee", "M4,9 H17 V14 A5,5 0 0 1 12,19 H9 A5,5 0 0 1 4,14 Z M17,10.5 H18.5 A2.5,2.5 0 0 1 18.5,15.5 H17 M8,3.5 V6 M12,3.5 V6");
            Add("terminal", "M4,5 H20 A1,1 0 0 1 21,6 V18 A1,1 0 0 1 20,19 H4 A1,1 0 0 1 3,18 V6 A1,1 0 0 1 4,5 Z M7,9.5 L10,12 L7,14.5 M12.5,15 H17");
            Add("database", "M19.5,6 C19.5,7.66 16.14,9 12,9 C7.86,9 4.5,7.66 4.5,6 C4.5,4.34 7.86,3 12,3 C16.14,3 19.5,4.34 19.5,6 Z M4.5,6 V18 C4.5,19.66 7.86,21 12,21 C16.14,21 19.5,19.66 19.5,18 V6 M4.5,12 C4.5,13.66 7.86,15 12,15 C16.14,15 19.5,13.66 19.5,12");
            Add("chart", "M4,20 H20 M7,16 V11 M12,16 V6 M17,16 V9", 2.4);
            Add("robot", "M6,8 H18 A2,2 0 0 1 20,10 V17 A2,2 0 0 1 18,19 H6 A2,2 0 0 1 4,17 V10 A2,2 0 0 1 6,8 Z M12,8 V4.5 M12,4.5 V4.4 M9,13 V13.5 M15,13 V13.5 M2,12.5 V14.5 M22,12.5 V14.5");
            Add("rocket", "M12,2.5 C15.5,5 17,8.5 16.5,13 L14.5,17 H9.5 L7.5,13 C7,8.5 8.5,5 12,2.5 Z " + Circle(12, 9.5, 1.6) + " M7.5,13 L4.5,16 L8,17 M16.5,13 L19.5,16 L16,17 M10.5,19.5 L12,21.5 L13.5,19.5");
            Add("plug", "M9,3 V7 M15,3 V7 M6,7 H18 V11 A6,6 0 0 1 6,11 Z M12,17 V21");
            Add("cloud", "M7,18.5 A4.5,4.5 0 0 1 6.4,9.5 A6,6 0 0 1 17.8,8.4 A5,5 0 0 1 17.5,18.5 Z");
            Add("megaphone", "M3.5,10 A1,1 0 0 1 4.5,9 H7.5 L16,4.5 V19.5 L7.5,15 H4.5 A1,1 0 0 1 3.5,14 Z M8,15.2 L9.2,20 H11.6 L10.6,16.2 M19,9.5 A3,3 0 0 1 19,14.5");
            Add("dot", Circle(12, 12, 3));

            Alias("cog", "settings");
            Alias("bolt", "lightning");
            Alias("download", "import");
            Alias("upload", "export");
            Alias("x", "close");
            Alias("plus", "add");
            Alias("trash", "delete");
            Alias("hourglass", "timer");
            Alias("pencil", "edit");
            Alias("image", "photo");
            Alias("user", "person");
            Alias("users", "people");
            Alias("question", "help");
            Alias("sun", "brightness");
            Alias("message", "chat");

            // Severity glyphs drawn inside a filled 16px circle
            All["sev-success"] = new IconDef { Data = "M5,8.4 L7.1,10.5 L11,6", Grid = 16, Stroke = 1.6 };
            All["sev-danger"] = new IconDef { Data = "M5.6,5.6 L10.4,10.4 M10.4,5.6 L5.6,10.4", Grid = 16, Stroke = 1.6 };
            All["sev-warning"] = new IconDef { Data = "M8,4.4 V8.9 M8,11.5 V11.6", Grid = 16, Stroke = 1.8 };
            All["sev-info"] = new IconDef { Data = "M8,7.4 V11.6 M8,4.6 V4.7", Grid = 16, Stroke = 1.8 };

            // Caption buttons on a 10px grid with 1px strokes so they stay pixel-crisp
            All["cap-minimize"] = new IconDef { Data = "M0,5.5 H10", Grid = 10, Stroke = 1 };
            All["cap-maximize"] = new IconDef { Data = "M0.5,0.5 H9.5 V9.5 H0.5 Z", Grid = 10, Stroke = 1 };
            All["cap-restore"] = new IconDef { Data = "M2.5,0.5 H9.5 V7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z", Grid = 10, Stroke = 1 };
            All["cap-close"] = new IconDef { Data = "M0.5,0.5 L9.5,9.5 M9.5,0.5 L0.5,9.5", Grid = 10, Stroke = 1 };
        }
    }
}
