using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using OrchidUI.Host.Theming;
using OrchidUI.Host.Views;
using OrchidUI.Protocol;

namespace OrchidUI.Host.Fields;

internal sealed class TextView(FieldSpec spec, WindowContext ctx) : FieldView(spec, ctx)
{
    private TextBox _box = null!;

    public override IEnumerable<Control> Inputs => [_box];

    public override Control BuildControl()
    {
        int lines = (int)Spec.PropNum("lines", 1);
        bool password = Spec.PropBool("password");
        _box = new TextBox { PlaceholderText = Spec.Placeholder, FontSize = P.FontSize };
        if (password)
        {
            _box.PasswordChar = '•';
            _box.RevealPassword = false;
        }
        if (lines > 1)
        {
            _box.AcceptsReturn = true;
            _box.TextWrapping = TextWrapping.Wrap;
            _box.VerticalContentAlignment = VerticalAlignment.Top;
            _box.MinHeight = Math.Round(P.FontSize * 1.45) * lines + 12;
            _box.MaxHeight = 420;
        }
        Name(_box);
        _box.TextChanged += (_, _) => Changed(Spec.Key!, _box.Text ?? "");
        if (Spec.Layout == "inline") _box.Width = InputWidth(Ctx.Layout("inputWidth", 280));
        else if (Spec.Width > 0) { _box.Width = Spec.Width; _box.HorizontalAlignment = HorizontalAlignment.Left; }
        return _box;
    }

    public override void SetValue(string key, object? value)
    {
        var s = value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        if (_box.Text == s) return;
        using (Quiet()) _box.Text = s;
    }
}

internal sealed class CopyView(FieldSpec spec, WindowContext ctx) : FieldView(spec, ctx)
{
    private TextBox _box = null!;
    private Button _copy = null!;

    public override IEnumerable<Control> Inputs => [_box, _copy];

    public override Control BuildControl()
    {
        _box = new TextBox { IsReadOnly = true, Width = InputWidth(Ctx.Layout("inputWidth", 280)), FontFamily = P.MonoFont, FontSize = P.SmallFontSize + 1 };
        Name(_box);
        _copy = IconButton(Ctx, "M9,9 H19 A1,1 0 0 1 20,10 V20 A1,1 0 0 1 19,21 H9 A1,1 0 0 1 8,20 V10 A1,1 0 0 1 9,9 Z M16,9 V5 A1,1 0 0 0 15,4 H5 A1,1 0 0 0 4,5 V15 A1,1 0 0 0 5,16 H8",
            Ctx.S("Copy", "Copy") + " " + Spec.Label);
        _copy.Margin = new Thickness(4, 0, 0, 0);
        _copy.Click += async (_, _) => await Ctx.Copy(_box.Text ?? "");
        return new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _box, _copy } };
    }

    public override void ApplyState(FieldState state)
    {
        base.ApplyState(state);
        if (state.Text is not null) _box.Text = state.Text;
    }

    internal static Button IconButton(WindowContext ctx, string path, string name)
    {
        var b = new Button
        {
            Content = Ui.Icon(new IconSpec { Path = path }, 16),
            Padding = new Thickness(7),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(b, name);
        Ui.Named(b, name);
        return b;
    }
}

internal sealed class ToggleView(FieldSpec spec, WindowContext ctx) : FieldView(spec, ctx)
{
    private ToggleSwitch _toggle = null!;

    public override IEnumerable<Control> Inputs => [_toggle];

    public override Control BuildControl()
    {
        _toggle = MakeSwitch(Ctx);
        Name(_toggle);
        _toggle.IsCheckedChanged += (_, _) => Changed(Spec.Key!, _toggle.IsChecked == true);
        return _toggle;
    }

    public override void SetValue(string key, object? value)
    {
        bool on = value is bool b && b;
        if (_toggle.IsChecked == on) return;
        using (Quiet()) _toggle.IsChecked = on;
    }

    internal static ToggleSwitch MakeSwitch(WindowContext ctx) => new()
    {
        OnContent = ctx.S("On", "On"),
        OffContent = ctx.S("Off", "Off"),
        VerticalAlignment = VerticalAlignment.Center,
        MinWidth = 0,
    };
}

internal sealed class SegmentedView(FieldSpec spec, WindowContext ctx) : FieldView(spec, ctx)
{
    private readonly List<ToggleButton> _buttons = new();
    private string _value = "";
    private bool _syncing;

    // Checks or unchecks buttons without their handlers reacting to it.
    private void Sync(Action change)
    {
        _syncing = true;
        try { change(); }
        finally { _syncing = false; }
    }

    public override IEnumerable<Control> Inputs => _buttons;

    public override Control BuildControl()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var option in Spec.PropStrings("options"))
        {
            var opt = option;
            var rb = new ToggleButton { Content = opt, MinWidth = 0, Padding = new Thickness(12, 5, 12, 6), FontSize = P.FontSize };
            rb.Classes.Add("segment");
            Ui.Named(rb, Spec.Label + ": " + opt, Spec.Description);
            // Acts like a radio button: clicking the selected option keeps it selected. The value
            // changes before the other buttons are unchecked: else the old one, unchecked while it
            // was still the value, checked itself again and unchecked the new one.
            rb.IsCheckedChanged += (_, _) =>
            {
                if (_syncing) return;
                if (rb.IsChecked != true)
                {
                    if (_value == opt) Sync(() => rb.IsChecked = true);
                    return;
                }
                bool changed = _value != opt;
                _value = opt;
                Sync(() => { foreach (var other in _buttons) if (other != rb) other.IsChecked = false; });
                if (changed) Changed(Spec.Key!, opt);
            };
            _buttons.Add(rb);
            panel.Children.Add(rb);
        }
        OrchidStyles.CheckedResources(panel.Resources, P, "Accent", "OnAccent");
        return new Border
        {
            Background = P["Input"],
            BorderBrush = P["ElevationBorder"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(P.Radius),
            Padding = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = panel,
        };
    }

    public override void SetValue(string key, object? value)
    {
        _value = value as string ?? "";
        Sync(() => { foreach (var b in _buttons) b.IsChecked = (string?)b.Content == _value; });
    }
}

internal sealed class SliderView(FieldSpec spec, WindowContext ctx) : FieldView(spec, ctx)
{
    private Slider _slider = null!;
    private TextBox _box = null!;
    private TextBlock? _hint;
    private ToggleSwitch? _toggle;
    private int _min, _max;

    public override IEnumerable<Control> Inputs => _toggle is null ? [_slider, _box] : [_slider, _box, _toggle];

    public override Control BuildControl()
    {
        _min = (int)Spec.PropNum("min");
        _max = (int)Spec.PropNum("max", 100);
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _slider = new Slider
        {
            Minimum = _min,
            Maximum = _max,
            Width = InputWidth(Ctx.Layout("sliderWidth", 220)),
            IsSnapToTickEnabled = true,
            TickFrequency = 1,
            SmallChange = 1,
            LargeChange = Spec.PropNum("largeChange", 1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Name(_slider);
        _box = new TextBox { Width = 76, Margin = new Thickness(14, 0, 0, 0), TextAlignment = TextAlignment.Center, FontSize = P.FontSize, VerticalAlignment = VerticalAlignment.Center };
        Name(_box, "value");
        _slider.ValueChanged += (_, _) =>
        {
            int v = (int)Math.Round(_slider.Value);
            if (!_box.IsKeyboardFocusWithin) _box.Text = v.ToString(CultureInfo.InvariantCulture);
            Changed(Spec.Key!, v);
        };
        _box.LostFocus += (_, _) => Commit();
        _box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        sp.Children.Add(_slider);
        sp.Children.Add(_box);
        if (Spec.HasFormat)
        {
            _hint = new TextBlock
            {
                Foreground = P["TextSecondary"],
                FontSize = Math.Round(P.FontSize * 0.93),
                Width = 72,
                Margin = new Thickness(12, 0, 0, 1),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            sp.Children.Add(_hint);
        }
        if (Spec.PropBool("toggle"))
        {
            sp.Children.Add(new Border { Width = 1, Height = 24, Background = P["Divider"], Margin = new Thickness(18, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center });
            _toggle = ToggleView.MakeSwitch(Ctx);
            Ui.Named(_toggle, Spec.Label + " " + Ctx.S("EnableDisable", "Enable / disable"), Spec.Description);
            _toggle.IsCheckedChanged += (_, _) =>
            {
                UpdateEnabled();
                Changed(Spec.PropStr("enabledKey", Spec.Key + "_enabled"), _toggle.IsChecked == true);
            };
            sp.Children.Add(_toggle);
        }
        return sp;
    }

    private void Commit()
    {
        if (!int.TryParse(_box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) v = (int)Math.Round(_slider.Value);
        v = Math.Max(_min, Math.Min(_max, v));
        _box.Text = v.ToString(CultureInfo.InvariantCulture);
        if ((int)Math.Round(_slider.Value) != v) _slider.Value = v;
    }

    private void UpdateEnabled()
    {
        bool on = _toggle is null || _toggle.IsChecked == true;
        _slider.IsEnabled = on;
        _box.IsEnabled = on;
        if (_hint is not null) _hint.Opacity = on ? 1 : 0.45;
    }

    public override void SetValue(string key, object? value)
    {
        using (Quiet())
        {
            if (_toggle is not null && key == Spec.PropStr("enabledKey"))
            {
                _toggle.IsChecked = value is bool b && b;
                UpdateEnabled();
                return;
            }
            var v = value is null || value is string || value is bool ? _min : Convert.ToDouble(value, CultureInfo.InvariantCulture);
            _slider.Value = v;
            _box.Text = ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
        }
    }

    public override void ApplyState(FieldState state)
    {
        base.ApplyState(state);
        if (_hint is not null) _hint.Text = state.Display ?? "";
    }
}

internal sealed class StepperView(FieldSpec spec, WindowContext ctx) : FieldView(spec, ctx)
{
    private NumericUpDown _box = null!;
    private bool _integer;

    public override IEnumerable<Control> Inputs => [_box];

    public override Control BuildControl()
    {
        _integer = Spec.PropBool("integer");
        int decimals = (int)Spec.PropNum("decimals");
        _box = new NumericUpDown
        {
            Minimum = (decimal)Spec.PropNum("min"),
            Maximum = (decimal)Spec.PropNum("max", 100),
            Increment = (decimal)Spec.PropNum("step", 1),
            FormatString = "F" + decimals.ToString(CultureInfo.InvariantCulture),
            Width = InputWidth(156),
            VerticalAlignment = VerticalAlignment.Center,
            ClipValueToMinMax = true,
            NumberFormat = CultureInfo.InvariantCulture.NumberFormat,
            FontSize = P.FontSize,
        };
        Name(_box);
        ToolTip.SetTip(_box, Ctx.S("Increase", "Increase") + " / " + Ctx.S("Decrease", "Decrease") + ": ↑ ↓");
        _box.ValueChanged += (_, e) =>
        {
            if (e.NewValue is not decimal d) return;
            Changed(Spec.Key!, _integer ? (object)(int)Math.Round(d) : (double)d);
        };
        return _box;
    }

    public override void SetValue(string key, object? value)
    {
        if (value is null || value is string || value is bool) return;
        var d = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        if (_box.Value == d) return;
        using (Quiet()) _box.Value = d;
    }
}

internal sealed class DropdownView(FieldSpec spec, WindowContext ctx) : FieldView(spec, ctx)
{
    private ComboBox _combo = null!;
    private List<string> _options = new();

    public override IEnumerable<Control> Inputs => _refresh is null ? [_combo] : [_combo, _refresh];

    public override Control BuildControl()
    {
        _options = Spec.PropStrings("options").ToList();
        _combo = new ComboBox { Width = InputWidth(Ctx.Layout("dropdownWidth", 240)), ItemsSource = _options, VerticalAlignment = VerticalAlignment.Center, FontSize = P.FontSize };
        Name(_combo);
        _combo.SelectionChanged += (_, _) =>
        {
            if (_combo.SelectedItem is string s) Changed(Spec.Key!, s);
        };
        if (!Spec.PropBool("refreshable")) return _combo;
        _refresh = RefreshButton(Ctx, Spec);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { _combo, _refresh } };
    }

    private Button? _refresh;

    internal Button? RefreshButtonControl => _refresh;

    // Asks Streamer.bot for the list again (AddRefreshableDropdown, AddMultiSelect with a loader).
    internal static Button RefreshButton(WindowContext ctx, FieldSpec spec)
    {
        var b = CopyView.IconButton(ctx, "M19.5,12 A7.5,7.5 0 1 1 17.3,6.7 M19.5,4.5 V9 H15", (spec.Label ?? "") + ": " + ctx.S("Refresh", "Refresh list"));
        ToolTip.SetTip(b, ctx.S("Refresh", "Refresh list"));
        b.Click += (_, _) => ctx.SendCommand(Commands.Refresh, spec.Id);
        return b;
    }

    public override void Respec(FieldSpec spec)
    {
        base.Respec(spec);
        var current = _combo.SelectedItem as string;
        using (Quiet())
        {
            _options = spec.PropStrings("options").ToList();
            _combo.ItemsSource = _options;
            if (current is not null && _options.Contains(current)) _combo.SelectedItem = current;
        }
    }

    public override void SetValue(string key, object? value)
    {
        var s = value as string ?? "";
        using (Quiet())
        {
            if (s.Length > 0 && !_options.Contains(s))
            {
                _options = new List<string>(_options);
                _options.Insert(0, s);
                _combo.ItemsSource = _options;
            }
            _combo.SelectedItem = s.Length > 0 ? s : null;
        }
    }
}

internal sealed class ColorView(FieldSpec spec, WindowContext ctx) : FieldView(spec, ctx)
{
    private ColorPicker _picker = null!;
    private TextBlock _hex = null!;
    private bool _alpha;

    public override IEnumerable<Control> Inputs => [_picker];

    public override Control BuildControl()
    {
        _alpha = Spec.PropBool("alpha");
        _picker = new ColorPicker { IsAlphaEnabled = _alpha, IsAlphaVisible = _alpha, VerticalAlignment = VerticalAlignment.Center };
        Name(_picker);
        ToolTip.SetTip(_picker, Ctx.S("PickColor", "Pick a colour"));
        _hex = new TextBlock { FontFamily = P.MonoFont, FontSize = Math.Round(P.FontSize * 0.93), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 1), MinWidth = _alpha ? 78 : 60, Foreground = P["Text"] };
        _picker.ColorChanged += (_, e) =>
        {
            var hex = ToHex(e.NewColor, _alpha);
            _hex.Text = hex;
            Changed(Spec.Key!, hex);
        };
        return new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _picker, _hex } };
    }

    internal static string ToHex(Color c, bool alpha) =>
        "#" + (alpha ? c.A.ToString("X2", CultureInfo.InvariantCulture) : "") + c.R.ToString("X2", CultureInfo.InvariantCulture) + c.G.ToString("X2", CultureInfo.InvariantCulture) + c.B.ToString("X2", CultureInfo.InvariantCulture);

    public override void SetValue(string key, object? value)
    {
        var hex = value as string ?? "#FFFFFF";
        var c = Palette.ParseColor(hex);
        if (!_alpha) c = Color.FromArgb(255, c.R, c.G, c.B);
        _hex.Text = ToHex(c, _alpha);
        if (_picker.Color == c) return;
        using (Quiet()) _picker.Color = c;
    }
}

// Days, hours, minutes and seconds in one box, like the WinUI TimePicker. Stores dd:hh:mm:ss.
internal sealed class DurationView(FieldSpec spec, WindowContext ctx) : FieldView(spec, ctx)
{
    private TextBox? _d;
    private TextBox _h = null!, _m = null!, _s = null!;

    public override IEnumerable<Control> Inputs => _d is null ? [_h, _m, _s] : [_d, _h, _m, _s];

    public override Control BuildControl()
    {
        var segments = new StackPanel { Orientation = Orientation.Horizontal };
        if (Spec.PropBool("showDays", true)) _d = Segment(segments, Ctx.S("DayUnit", "d"), Ctx.S("Days", "Days"));
        _h = Segment(segments, Ctx.S("HourUnit", "h"), Ctx.S("Hours", "Hours"));
        _m = Segment(segments, Ctx.S("MinuteUnit", "m"), Ctx.S("Minutes", "Minutes"));
        _s = Segment(segments, Ctx.S("SecondUnit", "s"), Ctx.S("Seconds", "Seconds"));
        return new Border
        {
            Background = P["Input"],
            BorderBrush = P["ElevationBorder"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(P.Radius),
            Height = P.Metrics.ControlHeight + 2,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
            Child = segments,
        };
    }

    private TextBox Segment(Panel host, string unit, string name)
    {
        if (host.Children.Count > 0) host.Children.Add(new Border { Width = 1, Background = P["InputBorder"] });
        var g = new Panel { Width = 62 };
        ToolTip.SetTip(g, name);
        var tb = new TextBox
        {
            Text = "0",
            Padding = new Thickness(6, 5, 24, 6),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            TextAlignment = TextAlignment.Right,
            VerticalContentAlignment = VerticalAlignment.Center,
            MinHeight = 0,
            FontSize = P.FontSize,
        };
        Ui.Named(tb, Spec.Label + " " + name.ToLowerInvariant(), Spec.Description);
        tb.TextChanged += (_, _) => Changed(Spec.Key!, Current());
        tb.LostFocus += (_, _) => Normalize();
        tb.GotFocus += (_, _) => tb.SelectAll();
        tb.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Up) { Step(tb, 1); e.Handled = true; }
            else if (e.Key == Key.Down) { Step(tb, -1); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        g.Children.Add(tb);
        g.Children.Add(new TextBlock
        {
            Text = unit,
            Foreground = P["TextMuted"],
            FontSize = Math.Round(P.FontSize * 0.93),
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 11, 1),
        });
        host.Children.Add(g);
        return tb;
    }

    private static long Parse(TextBox? tb) =>
        tb is not null && long.TryParse(tb.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private long Total() => Math.Max(0, Duration.Total(Parse(_d), Parse(_h), Parse(_m), Parse(_s)));

    private string Current() => Duration.Format(Total());

    private void Step(TextBox tb, int delta)
    {
        long v = Parse(tb) + delta;
        if (v < 0 && Total() == 0) v = 0;
        using (Quiet()) tb.Text = v.ToString(CultureInfo.InvariantCulture);
        Normalize();
        tb.SelectAll();
    }

    private void Normalize()
    {
        Show(Total());
        Changed(Spec.Key!, Current());
    }

    private void Show(long total)
    {
        long d = 0;
        if (_d is not null) { d = total / 86400; total %= 86400; }
        long h = total / 3600; total %= 3600;
        long m = total / 60, s = total % 60;
        using (Quiet())
        {
            if (_d is not null) _d.Text = d.ToString(CultureInfo.InvariantCulture);
            _h.Text = h.ToString(CultureInfo.InvariantCulture);
            _m.Text = m.ToString(CultureInfo.InvariantCulture);
            _s.Text = s.ToString(CultureInfo.InvariantCulture);
        }
    }

    public override void SetValue(string key, object? value)
    {
        if (!Duration.TryParseSeconds(value as string, out var total)) total = 0;
        if (Total() == total && Current() == Duration.Format(total)) return;
        Show(total);
    }
}

internal sealed class ButtonView(FieldSpec spec, WindowContext ctx) : FieldView(spec, ctx)
{
    private Button _button = null!;

    public override IEnumerable<Control> Inputs => [_button];

    public override Control BuildControl()
    {
        _button = new Button { Content = Spec.PropStr("text", "Run"), MinWidth = InputWidth(120), VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (Spec.PropBool("primary")) _button.Classes.Add("accent");
        Ui.Named(_button, (Spec.Label is null ? "" : Spec.Label + ": ") + Spec.PropStr("text", "Run"), Spec.Description);
        _button.Click += (_, _) => Ctx.Sink.Send(MessageTypes.Click, new Dictionary<string, object>(StringComparer.Ordinal) { { "field", Spec.Id } });
        return _button;
    }
}
