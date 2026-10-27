using System;
using System.Collections.Generic;

namespace OrchidUI
{
    internal sealed class GradientSpec
    {
        public string Key;
        public double X1, Y1, X2, Y2;
        public Rgba[] Colors;
        public double[] Offsets;
    }

    internal sealed class ResolvedTheme
    {
        public readonly Dictionary<string, Rgba> Colors = new Dictionary<string, Rgba>(StringComparer.Ordinal);
        public readonly List<GradientSpec> Gradients = new List<GradientSpec>();
        public bool IsDark;

        public Rgba this[string key] { get { return Colors[key]; } }
    }

    // Turns an OrchidTheme (mostly nulls) into a full set of colours.
    internal static class ThemeResolver
    {
        public static ResolvedTheme Resolve(OrchidTheme t)
        {
            var r = new ResolvedTheme();
            var def = OrchidTheme.Orchid();
            var bg = Rgba.Parse(t.Background, Rgba.Parse(def.Background, Rgba.Black)).Opaque();
            var accent = Rgba.Parse(t.Accent, Rgba.Parse(def.Accent, Rgba.White)).Opaque();
            var accent2 = Rgba.Parse(t.Accent2, accent).Opaque();
            bool dark = bg.IsDark;
            r.IsDark = dark;
            var text = Rgba.Parse(t.Text, dark ? Rgba.White : Rgba.Parse("#1B1B1B", Rgba.Black)).Opaque();
            double tint = Math.Max(0, Math.Min(1, t.Tint));
            var ink = dark ? Rgba.Mix(Rgba.White, accent2, tint) : Rgba.Mix(Rgba.Black, accent2, tint);
            var black = Rgba.Black;

            Func<string, Rgba, Rgba> pick = (user, fallback) => Rgba.Parse(user, fallback);
            Action<string, Rgba> set = (k, c) => r.Colors[k] = c;

            set("WindowBg", bg);
            set("MicaTint", bg.WithAlpha((byte)Math.Round(255 * Math.Max(0, Math.Min(1, t.BackdropOpacity)))));
            set("Accent", accent);
            set("Accent2", accent2);
            set("Text", text);

            if (dark)
            {
                set("Surface", pick(t.Surface, Rgba.Mix(bg, ink, 0.05).WithAlpha(0x66)));
                set("Card", pick(t.Card, ink.WithAlpha(0x0F)));
                set("CardHover", pick(t.CardHover, ink.WithAlpha(0x18)));
                set("CardExpanded", pick(t.CardExpanded, ink.WithAlpha(0x08)));
                set("CardBorder", pick(t.CardBorder, ink.WithAlpha(0x12)));
                set("Popup", pick(t.Popup, Rgba.Mix(bg, ink, 0.09)));
                set("PopupBorder", pick(t.PopupBorder, Rgba.Mix(bg, ink, 0.20)));
                set("Divider", pick(t.Divider, ink.WithAlpha(0x15)));
                set("Overlay", pick(t.Overlay, black.WithAlpha(0x70)));
                set("Input", pick(t.Input, ink.WithAlpha(0x10)));
                set("InputHover", pick(t.InputHover, ink.WithAlpha(0x17)));
                set("InputFocus", pick(t.InputFocus, Rgba.Mix(bg, black, 0.3).WithAlpha(0xCC)));
                set("InputBorder", pick(t.InputBorder, ink.WithAlpha(0x14)));
                set("InputUnderline", pick(t.InputUnderline, ink.WithAlpha(0x8B)));
                set("ToggleOff", pick(t.ToggleOff, black.WithAlpha(0x22)));
                set("SubtleHover", pick(t.SubtleHover, ink.WithAlpha(0x10)));
                set("Scrollbar", pick(t.Scrollbar, ink.WithAlpha(0x8B)));
                set("ControlSolid", Rgba.Mix(bg, ink, 0.24));
                set("TextSecondary", pick(t.TextSecondary, text.WithAlpha(0xC5)));
                set("TextMuted", pick(t.TextMuted, text.WithAlpha(0x8A)));
                set("TextDisabled", pick(t.TextDisabled, text.WithAlpha(0x5D)));
                set("Link", pick(t.Link, Rgba.Mix(accent, Rgba.White, 0.15)));
                set("Success", pick(t.Success, Rgba.Parse("#6CCB5F", black)));
                set("Warning", pick(t.Warning, Rgba.Parse("#F5C451", black)));
                set("Danger", pick(t.Danger, Rgba.Parse("#FF8E9E", black)));
            }
            else
            {
                set("Surface", pick(t.Surface, Rgba.White.WithAlpha(0x80)));
                set("Card", pick(t.Card, Rgba.White.WithAlpha(0xB3)));
                set("CardHover", pick(t.CardHover, Rgba.Parse("#F9F9F9", Rgba.White).WithAlpha(0x80)));
                set("CardExpanded", pick(t.CardExpanded, Rgba.Parse("#F6F6F6", Rgba.White).WithAlpha(0x80)));
                set("CardBorder", pick(t.CardBorder, black.WithAlpha(0x10)));
                set("Popup", pick(t.Popup, Rgba.Mix(bg, Rgba.White, 0.8)));
                set("PopupBorder", pick(t.PopupBorder, Rgba.Mix(bg, ink, 0.18)));
                set("Divider", pick(t.Divider, ink.WithAlpha(0x14)));
                set("Overlay", pick(t.Overlay, black.WithAlpha(0x4D)));
                set("Input", pick(t.Input, Rgba.White.WithAlpha(0xB3)));
                set("InputHover", pick(t.InputHover, Rgba.Parse("#F9F9F9", Rgba.White).WithAlpha(0x80)));
                set("InputFocus", pick(t.InputFocus, Rgba.White));
                set("InputBorder", pick(t.InputBorder, black.WithAlpha(0x12)));
                set("InputUnderline", pick(t.InputUnderline, black.WithAlpha(0x72)));
                set("ToggleOff", pick(t.ToggleOff, black.WithAlpha(0x06)));
                set("SubtleHover", pick(t.SubtleHover, ink.WithAlpha(0x0B)));
                set("Scrollbar", pick(t.Scrollbar, black.WithAlpha(0x72)));
                set("ControlSolid", Rgba.White);
                set("TextSecondary", pick(t.TextSecondary, text.WithAlpha(0xB8)));
                set("TextMuted", pick(t.TextMuted, text.WithAlpha(0x88)));
                set("TextDisabled", pick(t.TextDisabled, text.WithAlpha(0x5C)));
                set("Link", pick(t.Link, Rgba.Mix(accent, black, 0.15)));
                set("Success", pick(t.Success, Rgba.Parse("#0F7B0F", black)));
                set("Warning", pick(t.Warning, Rgba.Parse("#9D5D00", black)));
                set("Danger", pick(t.Danger, Rgba.Parse("#C42B1C", black)));
            }

            var titleBar = pick(t.TitleBar, Rgba.Transparent);
            set("TitleBar", titleBar);
            set("Pane", pick(t.Pane, Rgba.Transparent));
            set("WindowBorder", pick(t.WindowBorder, r["PopupBorder"]));
            set("SubtlePressed", r["SubtleHover"].WithAlpha((byte)(r["SubtleHover"].A * 2 / 3)));
            set("ToggleOffHover", r["SubtleHover"]);
            set("Selection", pick(t.Selection, accent2.WithAlpha(0xA0)));
            set("Focus", pick(t.Focus, text));
            set("FocusInner", dark ? black.WithAlpha(0xB3) : Rgba.White.WithAlpha(0xB3));
            set("Code", pick(t.Code, dark ? Rgba.Mix(accent, Rgba.White, 0.25) : Rgba.Mix(accent, black, 0.2)));
            set("CodeBackground", pick(t.CodeBackground, accent2.WithAlpha(dark ? (byte)0x38 : (byte)0x22)));

            set("AccentHover", pick(t.AccentHover, accent.WithAlpha(0xE6)));
            set("AccentPressed", pick(t.AccentPressed, accent.WithAlpha(0xCC)));
            var darkOn = Rgba.Mix(black, accent2, 0.22);
            double la = accent.Luminance;
            bool useDark = (la + 0.05) / (darkOn.Luminance + 0.05) >= 1.05 / (la + 0.05);
            var onAccent = pick(t.OnAccent, useDark ? darkOn : Rgba.White);
            set("OnAccent", onAccent);
            set("OnAccentMuted", onAccent.WithAlpha(0x99));
            set("Chip", pick(t.Chip, accent2.WithAlpha(dark ? (byte)0x40 : (byte)0x26)));
            set("ChipBorder", pick(t.ChipBorder, accent2.WithAlpha(dark ? (byte)0x66 : (byte)0x55)));

            set("Info", pick(t.Info, accent));
            Func<string, string, Rgba> statusBg = (user, key) => pick(user, Rgba.Mix(bg, r[key], dark ? 0.17 : 0.12));
            set("InfoBg", pick(t.InfoBackground, Rgba.Mix(bg, accent2, dark ? 0.20 : 0.12)));
            set("SuccessBg", statusBg(t.SuccessBackground, "Success"));
            set("WarningBg", statusBg(t.WarningBackground, "Warning"));
            set("DangerBg", statusBg(t.DangerBackground, "Danger"));
            set("OnStatus", dark ? black.WithAlpha(0xE4) : Rgba.White);

            set("CloseHover", pick(t.CloseHover, Rgba.Parse("#C42B1C", black)));
            set("ClosePressed", r["CloseHover"].WithAlpha(0xE6));
            set("White", Rgba.White);

            var heroStart = pick(t.HeroStart, Rgba.Mix(accent2, black, 0.45));
            var heroEnd = pick(t.HeroEnd, Rgba.Mix(accent, black, 0.38));
            set("HeroStart", heroStart);
            set("HeroEnd", heroEnd);
            var heroText = pick(t.HeroText, Rgba.White);
            set("HeroText", heroText);
            set("HeroTextMuted", heroText.WithAlpha(0xE0));
            set("HeroOrb1", accent.WithAlpha(0x30));
            set("HeroOrb2", accent2.WithAlpha(0x38));
            set("HeroBorder", Rgba.White.WithAlpha(0x26));

            var ib = r["InputBorder"];
            var ibStrong = ib.WithAlpha((byte)Math.Min(255, ib.A + (dark ? 6 : 0x14)));
            Gradient(r, "ElevationBorder", 0, 0, 0, 1, new[] { ibStrong, ibStrong, ib }, new[] { 0.0, 0.9, 1.0 });
            var aTop = (dark ? Rgba.White : Rgba.White).WithAlpha(0x14);
            var aBottom = black.WithAlpha(0x23);
            Gradient(r, "AccentElevationBorder", 0, 0, 0, 1, new[] { aTop, aTop, aBottom }, new[] { 0.0, 0.88, 1.0 });
            Gradient(r, "Hero", 0, 0, 1, 1, new[] { heroStart, Rgba.Mix(heroStart, heroEnd, 0.55), heroEnd }, new[] { 0.0, 0.55, 1.0 });
            Gradient(r, "AppIcon", 0, 0, 1, 1, new[] { accent2, accent }, new[] { 0.0, 1.0 });
            return r;
        }

        private static void Gradient(ResolvedTheme r, string key, double x1, double y1, double x2, double y2, Rgba[] colors, double[] offsets)
        {
            r.Gradients.Add(new GradientSpec { Key = key, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Colors = colors, Offsets = offsets });
        }
    }
}
