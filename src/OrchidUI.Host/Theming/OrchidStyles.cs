using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;

namespace OrchidUI.Host.Theming;

// The few looks Fluent doesn't have: sidebar items, top tabs, segmented buttons, link buttons.
internal static class OrchidStyles
{
    public static Styles Build(Palette p)
    {
        var styles = new Styles();

        Style Presenter(Func<Selector?, Selector> select, params (AvaloniaProperty Property, object Value)[] setters)
        {
            var s = new Style(x => select(x).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
            foreach (var (prop, value) in setters) s.Setters.Add(new Setter(prop, value));
            return s;
        }

        Style On(Func<Selector?, Selector> select, params (AvaloniaProperty Property, object Value)[] setters)
        {
            var s = new Style(select);
            foreach (var (prop, value) in setters) s.Setters.Add(new Setter(prop, value));
            return s;
        }

        var radius = new CornerRadius(p.Radius);

        // Sidebar items: quiet until hovered or selected; selected gets a tint and an accent bar.
        styles.Add(On(x => x.OfType<ToggleButton>().Class("nav"),
            (TemplatedControl.BackgroundProperty, Brushes.Transparent),
            (TemplatedControl.ForegroundProperty, p["Text"]),
            (TemplatedControl.BorderThicknessProperty, new Thickness(3, 0, 0, 0)),
            (TemplatedControl.BorderBrushProperty, Brushes.Transparent),
            (TemplatedControl.CornerRadiusProperty, radius)));
        styles.Add(Presenter(x => x.OfType<ToggleButton>().Class("nav").Class(":pointerover"),
            (ContentPresenter.BackgroundProperty, p["SubtleHover"]), (ContentPresenter.ForegroundProperty, p["Text"])));
        styles.Add(Presenter(x => x.OfType<ToggleButton>().Class("nav").Class(":checked"),
            (ContentPresenter.BackgroundProperty, p["SubtleHover"]), (ContentPresenter.ForegroundProperty, p["Text"]),
            (ContentPresenter.BorderBrushProperty, p["Accent"])));
        styles.Add(Presenter(x => x.OfType<ToggleButton>().Class("nav").Class(":pressed"),
            (ContentPresenter.BackgroundProperty, p["SubtlePressed"])));

        // Top tabs: an accent underline on the selected one.
        styles.Add(On(x => x.OfType<ToggleButton>().Class("tab"),
            (TemplatedControl.BackgroundProperty, Brushes.Transparent),
            (TemplatedControl.ForegroundProperty, p["TextSecondary"]),
            (TemplatedControl.BorderThicknessProperty, new Thickness(0, 0, 0, 2)),
            (TemplatedControl.BorderBrushProperty, Brushes.Transparent),
            (TemplatedControl.CornerRadiusProperty, new CornerRadius(0))));
        styles.Add(Presenter(x => x.OfType<ToggleButton>().Class("tab").Class(":pointerover"),
            (ContentPresenter.BackgroundProperty, p["SubtleHover"]), (ContentPresenter.ForegroundProperty, p["Text"])));
        styles.Add(Presenter(x => x.OfType<ToggleButton>().Class("tab").Class(":checked"),
            (ContentPresenter.BackgroundProperty, Brushes.Transparent), (ContentPresenter.ForegroundProperty, p["Text"]),
            (ContentPresenter.BorderBrushProperty, p["Accent"])));

        // Segmented control: the selected option is an accent pill.
        styles.Add(On(x => x.OfType<ToggleButton>().Class("segment"),
            (TemplatedControl.BackgroundProperty, Brushes.Transparent),
            (TemplatedControl.ForegroundProperty, p["Text"]),
            (TemplatedControl.BorderThicknessProperty, new Thickness(0)),
            (TemplatedControl.CornerRadiusProperty, new CornerRadius(Math.Max(0, p.Radius - 2)))));
        styles.Add(Presenter(x => x.OfType<ToggleButton>().Class("segment").Class(":pointerover"),
            (ContentPresenter.BackgroundProperty, p["SubtleHover"]), (ContentPresenter.ForegroundProperty, p["Text"])));
        styles.Add(Presenter(x => x.OfType<ToggleButton>().Class("segment").Class(":checked"),
            (ContentPresenter.BackgroundProperty, p["Accent"]), (ContentPresenter.ForegroundProperty, p["OnAccent"])));

        // Links inside text: no button chrome.
        styles.Add(Presenter(x => x.OfType<Button>().Class("link").Class(":pointerover"),
            (ContentPresenter.BackgroundProperty, Brushes.Transparent), (ContentPresenter.ForegroundProperty, p["Link"])));

        return styles;
    }

    // Fluent's ToggleButton reads its checked colours from these keys; set them where the toggles live.
    public static void CheckedResources(IResourceDictionary r, Palette p, string background, string foreground)
    {
        foreach (var state in new[] { "", "PointerOver", "Pressed" })
        {
            r["ToggleButtonBackgroundChecked" + state] = p[background];
            r["ToggleButtonForegroundChecked" + state] = p[foreground];
            r["ToggleButtonBorderBrushChecked" + state] = Brushes.Transparent;
        }
    }

    public static void AccentResources(IResourceDictionary r, Palette p)
    {
        r["AccentButtonBackground"] = p["Accent"];
        r["AccentButtonBackgroundPointerOver"] = p["AccentHover"];
        r["AccentButtonBackgroundPressed"] = p["AccentPressed"];
        r["AccentButtonBackgroundDisabled"] = p["Input"];
        r["AccentButtonForeground"] = p["OnAccent"];
        r["AccentButtonForegroundPointerOver"] = p["OnAccent"];
        r["AccentButtonForegroundPressed"] = p["OnAccentMuted"];
        r["AccentButtonForegroundDisabled"] = p["TextDisabled"];
        r["AccentButtonBorderBrush"] = p["AccentElevationBorder"];
        r["AccentButtonBorderBrushPointerOver"] = p["AccentElevationBorder"];
        r["ButtonBackgroundDisabled"] = p["Input"];
        r["ButtonForegroundDisabled"] = p["TextDisabled"];
        r["ButtonBorderBrushDisabled"] = p["InputBorder"];
        r["ButtonBackgroundPressed"] = p["SubtlePressed"];
    }
}
