using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OrchidUI
{
    // Every colour is a hex string ("#RGB", "#RRGGBB" or "#AARRGGBB") or "transparent".
    // Leave a colour null and it is derived from Background / Accent / Accent2 / Text.
    public sealed class OrchidTheme
    {
        // Base
        public string Background { get; set; }
        public string Accent { get; set; }
        public string Accent2 { get; set; }
        public string Text { get; set; }
        public double Tint { get; set; }

        // Surfaces
        public string Surface { get; set; }
        public string Pane { get; set; }
        public string TitleBar { get; set; }
        public string Card { get; set; }
        public string CardHover { get; set; }
        public string CardBorder { get; set; }
        public string CardExpanded { get; set; }
        public string Popup { get; set; }
        public string PopupBorder { get; set; }
        public string Divider { get; set; }
        public string Overlay { get; set; }
        public string WindowBorder { get; set; }

        // Inputs
        public string Input { get; set; }
        public string InputHover { get; set; }
        public string InputFocus { get; set; }
        public string InputBorder { get; set; }
        public string InputUnderline { get; set; }
        public string ToggleOff { get; set; }
        public string SubtleHover { get; set; }
        public string Scrollbar { get; set; }

        // Text
        public string TextSecondary { get; set; }
        public string TextMuted { get; set; }
        public string TextDisabled { get; set; }
        public string Link { get; set; }
        public string Selection { get; set; }
        public string Focus { get; set; }
        public string Code { get; set; }
        public string CodeBackground { get; set; }

        // Accent states
        public string AccentHover { get; set; }
        public string AccentPressed { get; set; }
        public string OnAccent { get; set; }
        public string Chip { get; set; }
        public string ChipBorder { get; set; }

        // Status
        public string Success { get; set; }
        public string Warning { get; set; }
        public string Danger { get; set; }
        public string Info { get; set; }
        public string SuccessBackground { get; set; }
        public string WarningBackground { get; set; }
        public string DangerBackground { get; set; }
        public string InfoBackground { get; set; }

        // Chrome & banner
        public string CloseHover { get; set; }
        public string HeroStart { get; set; }
        public string HeroEnd { get; set; }
        public string HeroText { get; set; }

        // Fonts. Families are comma-separated fallback lists.
        public string FontFamily { get; set; }
        public string HeadingFontFamily { get; set; }
        public string MonoFontFamily { get; set; }
        public string FontFile { get; set; }
        public double FontSize { get; set; }
        public double SmallFontSize { get; set; }
        public double PageTitleSize { get; set; }
        public double SectionTitleSize { get; set; }
        public double HeroTitleSize { get; set; }
        public string HeadingWeight { get; set; }
        public bool CrispText { get; set; }

        // Shape & effects
        public double CornerRadius { get; set; }
        public double LargeCornerRadius { get; set; }
        public double BorderThickness { get; set; }
        public bool Shadows { get; set; }
        public double BackdropOpacity { get; set; }

        public const string DefaultFonts = "Segoe UI Variable Text, Segoe UI, Noto Sans, Open Sans, Ubuntu, Cantarell, DejaVu Sans, Arial, Tahoma";
        public const string DefaultHeadingFonts = "Segoe UI Variable Display, Segoe UI Semibold, Segoe UI, Noto Sans, Open Sans, Ubuntu, DejaVu Sans, Arial, Tahoma";
        public const string DefaultMonoFonts = "Cascadia Mono, Consolas, JetBrains Mono, DejaVu Sans Mono, Liberation Mono, Courier New";

        public OrchidTheme()
        {
            Background = "#16111D";
            Accent = "#F28AC9";
            Accent2 = "#A57CDB";
            Text = "#F7F2FA";
            Tint = 0.12;
            FontFamily = DefaultFonts;
            HeadingFontFamily = DefaultHeadingFonts;
            MonoFontFamily = DefaultMonoFonts;
            FontSize = 14;
            SmallFontSize = 12;
            PageTitleSize = 28;
            SectionTitleSize = 14;
            HeroTitleSize = 30;
            HeadingWeight = "SemiBold";
            CrispText = true;
            CornerRadius = 6;
            LargeCornerRadius = 10;
            BorderThickness = 1;
            Shadows = true;
            BackdropOpacity = 0.72;
        }

        // Presets

        public static OrchidTheme Orchid() { return new OrchidTheme(); }

        public static OrchidTheme Amethyst()
        {
            return new OrchidTheme { Background = "#211926", Accent = "#C4A4DA", Accent2 = "#7F5D99", Text = "#FFFFFF", Tint = 0.18, CornerRadius = 4, LargeCornerRadius = 8 };
        }

        public static OrchidTheme Sakura()
        {
            return new OrchidTheme { Background = "#1B1218", Accent = "#FF9EC9", Accent2 = "#D47FC0", Text = "#FFF4F9", Tint = 0.14 };
        }

        public static OrchidTheme Synthwave()
        {
            return new OrchidTheme { Background = "#0E0A1A", Accent = "#FF4FD8", Accent2 = "#7C5CFF", Text = "#F3EEFF", Tint = 0.16, HeroStart = "#3B1C8C", HeroEnd = "#B0207F" };
        }

        public static OrchidTheme Graphite()
        {
            return new OrchidTheme { Background = "#151517", Accent = "#F48FC8", Accent2 = "#9B8AFB", Text = "#F2F2F4", Tint = 0.02 };
        }

        public static OrchidTheme Daylight()
        {
            return new OrchidTheme { Background = "#F6F1F8", Accent = "#C2378A", Accent2 = "#7E4FC2", Text = "#1D1424", Tint = 0.06, Shadows = true };
        }

        public static readonly string[] PresetNames = { "Orchid", "Amethyst", "Sakura", "Synthwave", "Graphite", "Daylight" };

        public static OrchidTheme Preset(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "amethyst": return Amethyst();
                case "sakura": return Sakura();
                case "synthwave": return Synthwave();
                case "graphite": return Graphite();
                case "daylight": return Daylight();
                default: return Orchid();
            }
        }

        public OrchidTheme Clone() { return (OrchidTheme)MemberwiseClone(); }

        internal static IEnumerable<PropertyInfo> Settable()
        {
            return typeof(OrchidTheme).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite);
        }

        internal List<string> Apply(IDictionary<string, object> values) { return ConfigApplier.Apply(this, values); }

        internal Dictionary<string, object> ToDictionary()
        {
            var d = new Dictionary<string, object>();
            foreach (var p in Settable())
            {
                var v = p.GetValue(this, null);
                if (v != null) d[p.Name] = v;
            }
            return d;
        }
    }
}
