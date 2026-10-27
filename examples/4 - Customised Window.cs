// Every customisation hook in one place. Copy the bits you need.
using System;
using OrchidUI;

public class CPHInline
{
    public bool Execute()
    {
        if (Orchid.AlreadyOpened("ALERT BOX")) return true;

        var ui = new Orchid(CPH, "ALERT BOX", "2.1.0");

        // Colours: start from a preset, then override anything you like.
        ui.SetTheme("Synthwave");
        ui.ConfigureTheme(t =>
        {
            t.Accent = "#FF5FC8";          // switches, sliders, primary buttons
            t.Accent2 = "#8E6CFF";         // banner, chips, selection, tint
            t.Background = "#100B1C";
            t.Card = "#16FFFFFF";          // #AARRGGBB works everywhere
            t.Danger = "#FF6B81";
            t.FontFamily = "Inter, " + OrchidTheme.DefaultFonts;
            t.FontSize = 13;
            t.PageTitleSize = 24;
            t.CornerRadius = 8;
            t.LargeCornerRadius = 12;
        });
        // ui.SetFontFile("fonts/Inter-Regular.ttf");   // ship a font next to Streamer.bot.exe
        // ui.LoadThemeFile("themes/my-theme.json");    // or keep all of this in a JSON file

        // Window: small, fixed size, no maximise.
        ui.ConfigureWindow(w =>
        {
            w.Width = 760;
            w.Height = 600;
            w.Resizable = false;
            w.CanMaximize = false;
            w.Topmost = false;
            w.RememberBounds = true;        // reopens where it was closed
            w.IconPath = "";                // .png/.ico; empty = generated badge
            // w.Chrome = ChromeMode.Native;      // force the OS title bar
        });

        // Layout: tabs along the top, compact cards, fewer built-in buttons.
        ui.ConfigureLayout(l =>
        {
            l.Navigation = NavigationStyle.Tabs;
            l.Density = Density.Compact;
            l.ContentPadding = 24;
            l.InputWidth = 240;
            l.ShowImportExport = false;
            l.ShowDiscard = false;
            l.Animations = true;
        });

        // Text: translate or rename any built-in label.
        ui.ConfigureStrings(s =>
        {
            s.Save = "Apply";
            s.SaveAndClose = "Apply & close";
            s.WindowTitle = "{0} · Settings";
        });

        var main = ui.Tab("Alerts", Icons.Bell);
        main.AddSegmented("Position", "Where alerts appear.", "position", new[] { "Left", "Center", "Right" }, 1);
        main.AddSlider("Volume", "Alert sound volume.", "volume", 0, 100, 70).WithFormat(v => v + "%").WithIcon(Icons.Volume);
        main.AddTextbox("Webhook", "Discord webhook for alert logs.", "webhook")
            .WithWidth(320)
            .Validate(v => string.IsNullOrEmpty((string)v) || ((string)v).StartsWith("https://"), "Must start with https://");

        var style = ui.Tab("Style", Icons.Palette);
        style.AddColorPicker("Accent", "Alert accent colour.", "accent", "#FF5FC8");
        style.AddDropdown("Animation", "Entry animation.", "animation", new[] { "Fade", "Slide", "Bounce", "Pop" });

        ui.ShowUI();
        return true;
    }
}
