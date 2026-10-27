using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Controls;
using Avalonia.Styling;
using OrchidUI.Protocol;

namespace OrchidUI.Host.Theming;

// The resolved theme as Avalonia brushes and metrics. The host never parses theme files: every
// colour arrives from the shim as #AARRGGBB, already worked out by the shim's ThemeResolver.
internal sealed class Palette
{
    private readonly Dictionary<string, IBrush> _brushes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Color> _colors = new(StringComparer.Ordinal);

    public Palette(ThemeSpec theme, string density)
    {
        Theme = theme;
        foreach (var (key, hex) in theme.Colors)
        {
            var c = ParseColor(hex);
            _colors[key] = c;
            _brushes[key] = new ImmutableSolidColorBrush(c);
        }
        foreach (var g in theme.Gradients)
        {
            var stops = new List<ImmutableGradientStop>();
            for (int i = 0; i < g.Colors.Length && i < g.Offsets.Length; i++) stops.Add(new ImmutableGradientStop(g.Offsets[i], ParseColor(g.Colors[i])));
            _brushes[g.Key] = new ImmutableLinearGradientBrush(stops, 1, null, null, GradientSpreadMethod.Pad,
                new RelativePoint(g.X1, g.Y1, RelativeUnit.Relative), new RelativePoint(g.X2, g.Y2, RelativeUnit.Relative));
        }
        Metrics = Metrics.For(density);
        Font = MakeFamily(theme.FontFamily);
        HeadingFont = MakeFamily(theme.HeadingFontFamily);
        MonoFont = MakeFamily(theme.MonoFontFamily);
        HeadingWeight = ParseWeight(theme.HeadingWeight);
    }

    public ThemeSpec Theme { get; }
    public Metrics Metrics { get; }
    public FontFamily Font { get; set; }
    public FontFamily HeadingFont { get; set; }
    public FontFamily MonoFont { get; }
    public FontWeight HeadingWeight { get; }
    public double Radius => Theme.CornerRadius;
    public double LargeRadius => Theme.LargeCornerRadius;
    public double FontSize => Theme.FontSize;
    public double SmallFontSize => Theme.SmallFontSize;
    public bool IsDark => Theme.IsDark;

    public IBrush this[string key] => _brushes.TryGetValue(key, out var b) ? b : Brushes.Magenta;
    public Color Color(string key) => _colors.TryGetValue(key, out var c) ? c : Colors.Magenta;

    public static Color ParseColor(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return Colors.Transparent;
        var s = hex.TrimStart('#');
        if (s.Length == 6) s = "FF" + s;
        return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? Avalonia.Media.Color.FromUInt32(v) : Colors.Transparent;
    }

    // A comma-separated fallback list, plus Inter (bundled) so text always renders.
    public static FontFamily MakeFamily(string? families)
    {
        var list = string.IsNullOrWhiteSpace(families) ? "" : families.Trim().TrimEnd(',');
        return new FontFamily(list.Length == 0 ? "fonts:Inter#Inter" : list + ", fonts:Inter#Inter");
    }

    private static FontWeight ParseWeight(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "thin" => FontWeight.Thin,
        "extralight" or "ultralight" => FontWeight.ExtraLight,
        "light" => FontWeight.Light,
        "normal" or "regular" => FontWeight.Normal,
        "medium" => FontWeight.Medium,
        "bold" => FontWeight.Bold,
        "extrabold" or "ultrabold" => FontWeight.ExtraBold,
        "black" or "heavy" => FontWeight.Black,
        _ => FontWeight.SemiBold,
    };

    // Fluent's own resource keys, so built-in controls (text boxes, combo boxes, sliders, switches)
    // pick up the theme too.
    public void ApplyTo(IResourceDictionary resources, Avalonia.Controls.Window? root)
    {
        var accent = Color("Accent");
        resources["SystemAccentColor"] = accent;
        resources["SystemAccentColorLight1"] = Lerp(accent, Colors.White, 0.15);
        resources["SystemAccentColorLight2"] = Lerp(accent, Colors.White, 0.30);
        resources["SystemAccentColorLight3"] = Lerp(accent, Colors.White, 0.45);
        resources["SystemAccentColorDark1"] = Lerp(accent, Colors.Black, 0.15);
        resources["SystemAccentColorDark2"] = Lerp(accent, Colors.Black, 0.30);
        resources["SystemAccentColorDark3"] = Lerp(accent, Colors.Black, 0.45);

        void Brush(string key, string color) => resources[key] = this[color];
        Brush("TextControlBackground", "Input");
        Brush("TextControlBackgroundPointerOver", "InputHover");
        Brush("TextControlBackgroundFocused", "InputFocus");
        Brush("TextControlBorderBrush", "InputBorder");
        Brush("TextControlBorderBrushPointerOver", "InputBorder");
        Brush("TextControlForeground", "Text");
        Brush("TextControlForegroundPointerOver", "Text");
        Brush("TextControlForegroundFocused", "Text");
        Brush("TextControlPlaceholderForeground", "TextMuted");
        Brush("TextControlPlaceholderForegroundPointerOver", "TextMuted");
        Brush("TextControlPlaceholderForegroundFocused", "TextMuted");
        Brush("TextControlSelectionHighlightColor", "Selection");
        Brush("ComboBoxBackground", "Input");
        Brush("ComboBoxBackgroundPointerOver", "InputHover");
        Brush("ComboBoxBorderBrush", "InputBorder");
        Brush("ComboBoxForeground", "Text");
        Brush("ComboBoxDropDownBackground", "Popup");
        Brush("ComboBoxDropDownBorderBrush", "PopupBorder");
        Brush("ButtonBackground", "Input");
        Brush("ButtonBackgroundPointerOver", "InputHover");
        Brush("ButtonBorderBrush", "InputBorder");
        Brush("ButtonForeground", "Text");
        Brush("ButtonForegroundPointerOver", "Text");
        Brush("ToggleSwitchFillOff", "ToggleOff");
        Brush("ToggleSwitchFillOffPointerOver", "ToggleOffHover");
        Brush("ToggleSwitchFillOn", "Accent");
        Brush("ToggleSwitchFillOnPointerOver", "AccentHover");
        Brush("ToggleSwitchKnobFillOn", "OnAccent");
        Brush("ToggleSwitchKnobFillOnPointerOver", "OnAccent");
        Brush("ToggleSwitchStrokeOff", "InputUnderline");
        Brush("ToggleSwitchContentForeground", "Text");
        Brush("SliderTrackValueFill", "Accent");
        Brush("SliderTrackValueFillPointerOver", "AccentHover");
        Brush("SliderThumbBackground", "Accent");
        Brush("FlyoutPresenterBackground", "Popup");
        Brush("FlyoutBorderThemeBrush", "PopupBorder");
        Brush("MenuFlyoutPresenterBackground", "Popup");
        Brush("MenuFlyoutPresenterBorderBrush", "PopupBorder");
        Brush("ScrollBarThumbFill", "Scrollbar");
        resources["ControlCornerRadius"] = new CornerRadius(Radius);
        resources["OverlayCornerRadius"] = new CornerRadius(LargeRadius);
        if (root is not null) root.RequestedThemeVariant = IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private static Color Lerp(Color a, Color b, double t) => Avalonia.Media.Color.FromArgb(
        a.A, (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}

// Card spacing per density.
internal sealed record Metrics(double CardPadX, double CardPadY, double CardMinHeight, double CardGap, double ControlHeight)
{
    public static Metrics For(string density) => density switch
    {
        "compact" => new Metrics(14, 9, 52, 3, 28),
        "comfortable" => new Metrics(20, 18, 80, 6, 36),
        _ => new Metrics(16, 14, 68, 4, 32),
    };
}
