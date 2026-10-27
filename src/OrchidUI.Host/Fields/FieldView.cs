using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using OrchidUI.Host.Theming;
using OrchidUI.Host.Views;
using OrchidUI.Protocol;

namespace OrchidUI.Host.Fields;

// One field on screen. Values come from the shim (SetValue, never echoed back); user input goes
// out as change messages; FieldState patches decide visibility, enabled, errors and markers.
internal abstract class FieldView
{
    private int _suppress;

    protected FieldView(FieldSpec spec, WindowContext ctx)
    {
        Spec = spec;
        Ctx = ctx;
    }

    public FieldSpec Spec { get; private set; }
    protected WindowContext Ctx { get; }
    protected Palette P => Ctx.P;

    // The whole row (card or plain element) and the control a screen reader lands on.
    public Control Row { get; set; } = new Panel();
    public Border? Card { get; set; }
    public TextBlock? ErrorText { get; set; }
    public TextBlock? SummaryText { get; set; }

    // Opens a collapsed card (an expander), e.g. before jumping to it.
    public Action? Expand { get; set; }
    internal Views.DisclosureButton? Disclosure { get; set; }
    public Control? DirtyDot { get; set; }
    public bool StateVisible { get; private set; } = true;
    public bool SearchMatch { get; set; } = true;

    public virtual bool IsCard => Spec.Label is not null;

    public abstract Control? BuildControl();

    // Every interactive control this view owns, for accessibility checks and focus.
    public virtual IEnumerable<Control> Inputs => Array.Empty<Control>();

    public virtual void SetValue(string key, object? value) { }

    public virtual void Respec(FieldSpec spec) => Spec = spec;

    public virtual void ApplyState(FieldState state)
    {
        StateVisible = state.Visible;
        UpdateVisibility();
        Row.IsEnabled = state.Enabled;
        Row.Opacity = state.Enabled ? 1 : 0.5;
        if (DirtyDot is not null) DirtyDot.IsVisible = state.Dirty;
        if (ErrorText is not null)
        {
            ErrorText.Text = state.Error ?? "";
            ErrorText.IsVisible = state.Error is not null;
        }
        if (Card is not null) Card.BorderBrush = state.Error is not null ? P["Danger"] : P["CardBorder"];
        if (SummaryText is not null) SummaryText.Text = state.Summary ?? "";
    }

    public void UpdateVisibility() => Row.IsVisible = StateVisible && SearchMatch;

    // The last value for each key that the shim and this view agree on. TextBox raises TextChanged
    // after the Quiet() scope that set the text has ended, so suppression alone isn't enough.
    private readonly Dictionary<string, string> _known = new(StringComparer.Ordinal);

    private static string Canonical(object? value) => Json.Serialize(value, false);

    // A value from the shim (on open or in a patch): show it without sending it back.
    public void Push(string key, object? value)
    {
        _known[key] = Canonical(value);
        using (Quiet()) SetValue(key, value);
    }

    protected void Changed(string key, object? value)
    {
        if (_suppress > 0) return;
        var canonical = Canonical(value);
        if (_known.TryGetValue(key, out var known) && known == canonical) return;
        _known[key] = canonical;
        Ctx.SendChange(Spec.Id, key, value);
    }

    // The value as this view shows it counts as agreed with the shim (a list pads short rows).
    protected void Agree(string key, object? value) => _known[key] = Canonical(value);

    protected IDisposable Quiet()
    {
        _suppress++;
        return new Release(() => _suppress--);
    }

    private sealed class Release(Action a) : IDisposable
    {
        public void Dispose() => a();
    }

    protected double InputWidth(double fallback) => Spec.Width > 0 ? Spec.Width : fallback;

    protected void Name(Control c, string? suffix = null) =>
        Ui.Named(c, suffix is null ? Spec.Label : Spec.Label + " " + suffix, Spec.Description);

    public bool Matches(string query, string tabName) =>
        Contains(Spec.Label, query) || Contains(Spec.Description, query) || Contains(Spec.Key, query) || Contains(tabName, query);

    private static bool Contains(string? hay, string needle) => hay is not null && hay.Contains(needle, StringComparison.OrdinalIgnoreCase);

    public static FieldView Create(FieldSpec spec, WindowContext ctx) => spec.Kind switch
    {
        FieldKinds.Title => new TitleView(spec, ctx),
        FieldKinds.Description => new DescriptionView(spec, ctx),
        FieldKinds.Separator => new SeparatorView(spec, ctx),
        FieldKinds.Info => new InfoView(spec, ctx),
        FieldKinds.Text => new TextView(spec, ctx),
        FieldKinds.Copy => new CopyView(spec, ctx),
        FieldKinds.Toggle => new ToggleView(spec, ctx),
        FieldKinds.Segmented => new SegmentedView(spec, ctx),
        FieldKinds.Slider => new SliderView(spec, ctx),
        FieldKinds.Stepper => new StepperView(spec, ctx),
        FieldKinds.Dropdown => new DropdownView(spec, ctx),
        FieldKinds.Color => new ColorView(spec, ctx),
        FieldKinds.Duration => new DurationView(spec, ctx),
        FieldKinds.Button => new ButtonView(spec, ctx),
        FieldKinds.List => new ListView(spec, ctx),
        FieldKinds.MultiSelect => new MultiSelectView(spec, ctx),
        FieldKinds.Path or FieldKinds.Image => new PathView(spec, ctx),
        _ => new PlaceholderView(spec, ctx),
    };

    protected static string Num(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}

internal sealed class TitleView(FieldSpec spec, WindowContext ctx) : FieldView(spec, ctx)
{
    public override bool IsCard => false;

    public override Control BuildControl() => new TextBlock
    {
        Text = Spec.PropStr("text", ""),
        FontFamily = P.HeadingFont,
        FontSize = P.Theme.SectionTitleSize,
        FontWeight = P.HeadingWeight,
        Foreground = P["Text"],
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(1, 22, 0, 8),
    };
}

internal sealed class DescriptionView(FieldSpec spec, WindowContext ctx) : FieldView(spec, ctx)
{
    public override bool IsCard => false;

    public override Control BuildControl()
    {
        var tb = Ui.RichText(Spec.PropStr("text", ""), P, P["TextSecondary"], Math.Round(P.FontSize * 0.93));
        tb.Margin = new Thickness(1, -2, 0, 12);
        return tb;
    }
}

internal sealed class SeparatorView(FieldSpec spec, WindowContext ctx) : FieldView(spec, ctx)
{
    public override bool IsCard => false;
    public override Control BuildControl() => new Border { Height = 1, Background = P["Divider"], Margin = new Thickness(0, 16, 0, 0) };
}

internal sealed class InfoView(FieldSpec spec, WindowContext ctx) : FieldView(spec, ctx)
{
    public override bool IsCard => false;

    public override Control BuildControl()
    {
        var kind = Spec.PropStr("kind", "info");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var icon = Ui.SeverityIcon(kind, P);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 3, 14, 0);
        grid.Children.Add(icon);
        var text = Ui.RichText(Spec.PropStr("text", ""), P, P["Text"], P.FontSize);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return new Border
        {
            Background = Ui.SeverityBackground(kind, P),
            BorderBrush = P["CardBorder"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(P.Radius),
            Padding = new Thickness(16, 13, 16, 14),
            Margin = new Thickness(0, 0, 0, 8),
            Child = grid,
        };
    }
}

// AddCustom (a WPF element) and fields the host doesn't draw: a card that says so, with the key.
internal sealed class PlaceholderView(FieldSpec spec, WindowContext ctx) : FieldView(spec, ctx)
{
    public override bool IsCard => true;

    public override Control BuildControl()
    {
        string text = Spec.Kind == FieldKinds.Custom
            ? "This part uses a custom WPF control (AddCustom), which OrchidUI can't show."
            : "Not supported by OrchidUI yet (" + Spec.PropStr("api", "this field") + ")"
              + (Spec.Key is null ? "." : ". Its value is kept in “" + Spec.Key + "”.");
        var tb = new TextBlock { Text = text, Foreground = P["TextMuted"], FontSize = Math.Round(P.FontSize * 0.93), TextWrapping = TextWrapping.Wrap };
        Ui.Named(tb, (Spec.Label ?? "Custom content") + ": " + text);
        return tb;
    }
}
