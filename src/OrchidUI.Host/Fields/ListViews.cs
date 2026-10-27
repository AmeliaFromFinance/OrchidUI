using System.Collections;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OrchidUI.Host.Views;
using OrchidUI.Protocol;

namespace OrchidUI.Host.Fields;

// AddTextList (flat: one text per row, an "Add…" box under it) and AddTable / AddSelectionTextList /
// AddSelectionAmountTextList (rows of text, number and choice cells, an "Add row" button). Every edit
// sends the whole list: string[] when flat, string[][] otherwise, cells as strings.
internal sealed class ListView(FieldSpec spec, WindowContext ctx) : FieldView(spec, ctx)
{
    internal sealed record Column(string Header, string Kind, double Weight, double Min, double Max, string[] Options, string? Placeholder);

    internal const string UpIcon = "M6,15 L12,9 L18,15";
    internal const string DownIcon = "M6,9 L12,15 L18,9";
    internal const string RemoveIcon = "M6.5,6.5 L17.5,17.5 M17.5,6.5 L6.5,17.5";

    private readonly List<string[]> _rows = new();
    private readonly List<Control> _rowInputs = new();
    private List<Column> _cols = new();
    private bool _flat;
    private StackPanel _rowsPanel = null!;
    private TextBlock _empty = null!;
    private TextBox? _addBox;
    private Button _add = null!;

    public override IEnumerable<Control> Inputs => _addBox is null ? [.. _rowInputs, _add] : [.. _rowInputs, _addBox, _add];

    internal IReadOnlyList<string[]> Rows => _rows;
    internal Panel RowsPanel => _rowsPanel;
    internal TextBox? AddBox => _addBox;
    internal Button AddButton => _add;

    public override Control BuildControl()
    {
        _flat = Spec.PropBool("flat");
        _cols = ParseColumns(Spec.Props.TryGetValue("columns", out var c) ? c : null);
        if (_cols.Count == 0) _cols.Add(new Column("Value", "text", 1, 0, 0, [], null));
        if (_flat) _cols = [_cols[0] with { Kind = "text" }];

        var root = new StackPanel { Spacing = 6 };
        if (!_flat) root.Children.Add(HeaderRow());
        _rowsPanel = new StackPanel { Spacing = 6 };
        root.Children.Add(_rowsPanel);
        _empty = new TextBlock { Text = Ctx.S("NoEntries", "No entries yet."), Foreground = P["TextMuted"], FontSize = P.SmallFontSize, Margin = new Thickness(2, 2, 0, 2) };
        root.Children.Add(_empty);
        root.Children.Add(Footer());
        Rebuild();
        return root;
    }

    internal static List<Column> ParseColumns(object? raw)
    {
        var list = new List<Column>();
        if (raw is not IEnumerable items || raw is string) return list;
        foreach (var item in items)
        {
            if (item is string header) { list.Add(new Column(header, "text", 1, 0, 0, [], null)); continue; }
            if (item is not Dictionary<string, object> d) continue;
            var kind = Json.Str(d, "kind", "text");
            var options = Json.Arr(d, "options")?.OfType<string>().ToArray() ?? [];
            list.Add(new Column(Json.Str(d, "header", ""), kind is "number" or "choice" ? kind : "text",
                Math.Max(0.1, Json.Num(d, "weight", 1)), Json.Num(d, "min", 0), Json.Num(d, "max", 1e9), options, Json.Str(d, "placeholder")));
        }
        return list;
    }

    private Grid RowGrid()
    {
        var g = new Grid { ColumnSpacing = 6 };
        foreach (var col in _cols) g.ColumnDefinitions.Add(new ColumnDefinition(col.Weight, GridUnitType.Star));
        // Fixed, so rows line up when a move button isn't there.
        for (int i = 0; i < 3; i++) g.ColumnDefinitions.Add(new ColumnDefinition(RowButtonWidth, GridUnitType.Pixel));
        return g;
    }

    private Control HeaderRow()
    {
        var g = RowGrid();
        for (int i = 0; i < _cols.Count; i++)
        {
            var tb = new TextBlock { Text = _cols[i].Header, Foreground = P["TextSecondary"], FontSize = P.SmallFontSize, Margin = new Thickness(2, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(tb, i);
            g.Children.Add(tb);
        }
        // Room for the row buttons, so headers line up with the cells.
        var spacer = new Border { Width = 3 * RowButtonWidth };
        Grid.SetColumn(spacer, _cols.Count);
        Grid.SetColumnSpan(spacer, 3);
        g.Children.Add(spacer);
        return g;
    }

    private Control Footer()
    {
        if (_flat)
        {
            _addBox = new TextBox { PlaceholderText = Spec.Placeholder ?? Ctx.S("AddPlaceholder", "Add…"), FontSize = P.FontSize };
            Ui.Named(_addBox, Spec.Label + " " + Ctx.S("AddItem", "Add item"), Spec.Description);
            _addBox.AddHandler(InputElement.KeyDownEvent, (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                AddFromBox();
                e.Handled = true;
            }, RoutingStrategies.Tunnel);
            _add = new Button { Content = Ctx.S("AddItem", "Add item"), VerticalAlignment = VerticalAlignment.Stretch };
            Ui.Named(_add, Spec.Label + ": " + Ctx.S("AddItem", "Add item"));
            _add.Click += (_, _) => AddFromBox();
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6, Margin = new Thickness(0, 4, 0, 0) };
            g.Children.Add(_addBox);
            Grid.SetColumn(_add, 1);
            g.Children.Add(_add);
            return g;
        }
        _add = new Button { Content = "+  " + Ctx.S("AddRow", "Add row"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        Ui.Named(_add, Spec.Label + ": " + Ctx.S("AddRow", "Add row"));
        _add.Click += (_, _) => AddRow();
        return _add;
    }

    private void AddFromBox()
    {
        var text = _addBox?.Text ?? "";
        if (text.Trim().Length == 0) return;
        _rows.Add([text]);
        _addBox!.Text = "";
        Rebuild();
        Send();
        _addBox.Focus();
    }

    internal void AddRow()
    {
        var row = _cols.Select(c => c.Kind switch
        {
            "choice" => c.Options.FirstOrDefault() ?? "",
            "number" => Number(Math.Max(c.Min, Math.Min(c.Max, 0))),
            _ => "",
        }).ToArray();
        _rows.Add(row);
        Rebuild();
        Send();
        _rowInputs.Skip((_rows.Count - 1) * (_cols.Count + 3)).FirstOrDefault()?.Focus();
    }

    private void Rebuild()
    {
        _rowsPanel.Children.Clear();
        _rowInputs.Clear();
        using (Quiet())
        {
            for (int i = 0; i < _rows.Count; i++) _rowsPanel.Children.Add(RowControl(_rows[i], i));
        }
        _empty.IsVisible = _rows.Count == 0;
    }

    private Control RowControl(string[] row, int index)
    {
        var g = RowGrid();
        string rowName = Spec.Label + " " + (_flat ? Ctx.S("Item", "item") : Ctx.S("Row", "row")) + " " + (index + 1).ToString(CultureInfo.InvariantCulture);
        for (int c = 0; c < _cols.Count; c++)
        {
            var cell = Cell(row, c, rowName);
            Grid.SetColumn(cell, c);
            g.Children.Add(cell);
            _rowInputs.Add(cell);
        }
        var up = RowButton(UpIcon, Ctx.S("MoveUp", "Move up"), rowName, index > 0, () => Move(row, -1));
        var down = RowButton(DownIcon, Ctx.S("MoveDown", "Move down"), rowName, index < _rows.Count - 1, () => Move(row, 1));
        var remove = RowButton(RemoveIcon, Ctx.S("Remove", "Remove"), rowName, true, () => Remove(row));
        int col = _cols.Count;
        foreach (var b in new[] { up, down, remove })
        {
            Grid.SetColumn(b, col++);
            g.Children.Add(b);
            _rowInputs.Add(b);
        }
        return g;
    }

    private const double RowButtonWidth = 30;

    private Button RowButton(string icon, string what, string rowName, bool enabled, Action click)
    {
        var b = CopyView.IconButton(Ctx, icon, what + " (" + rowName + ")");
        ToolTip.SetTip(b, what);
        // An unavailable move isn't there at all: not drawn, not tabbed to, not read out.
        b.IsVisible = enabled;
        b.Click += (_, _) => click();
        return b;
    }

    private Control Cell(string[] row, int c, string rowName)
    {
        var col = _cols[c];
        string name = rowName + (_flat || col.Header.Length == 0 ? "" : ", " + col.Header);
        string value = c < row.Length ? row[c] ?? "" : "";
        switch (col.Kind)
        {
            case "choice":
            {
                var options = col.Options.ToList();
                if (value.Length > 0 && !options.Contains(value)) options.Insert(0, value);
                var combo = new ComboBox { ItemsSource = options, SelectedItem = value.Length > 0 ? value : options.FirstOrDefault(), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = P.FontSize };
                Ui.Named(combo, name, Spec.Description);
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is not string s) return;
                    row[c] = s;
                    Send();
                };
                return combo;
            }
            case "number":
            {
                var box = new NumericUpDown
                {
                    Minimum = (decimal)Math.Max(-7.9e27, col.Min),
                    Maximum = (decimal)Math.Min(7.9e27, col.Max),
                    Increment = 1,
                    TextConverter = CellNumber.Instance,
                    ClipValueToMinMax = true,
                    ShowButtonSpinner = false,
                    Value = decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    FontSize = P.FontSize,
                };
                Ui.Named(box, name, Spec.Description);
                box.ValueChanged += (_, e) =>
                {
                    row[c] = e.NewValue is decimal v ? Number((double)v) : "";
                    Send();
                };
                return box;
            }
            default:
            {
                var box = new TextBox { Text = value, PlaceholderText = col.Placeholder ?? (_flat ? Spec.Placeholder : null), FontSize = P.FontSize };
                Ui.Named(box, name, Spec.Description);
                box.TextChanged += (_, _) =>
                {
                    row[c] = box.Text ?? "";
                    Send();
                };
                return box;
            }
        }
    }

    private static string Number(double v) => v.ToString("0.##########", CultureInfo.InvariantCulture);

    // A number cell's text: stored with a dot, and typed with a dot or a decimal comma.
    internal sealed class CellNumber : Avalonia.Data.Converters.IValueConverter
    {
        public static readonly CellNumber Instance = new();

        // NumericUpDown turns text into a value with Convert, and back with ConvertBack.
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is decimal d ? d.ToString("0.##########", CultureInfo.InvariantCulture) : "";

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var text = (value as string ?? "").Trim().Replace(" ", "").Replace("\u00A0", "");
            if (text.Length == 0) return null;
            // "12,5" is a decimal comma; with both, the last one is the decimal point ("1.234,5",
            // "1,234.5"). "1,000" alone is a thousand, unless the culture writes decimals with a comma.
            int comma = text.LastIndexOf(','), dot = text.LastIndexOf('.');
            bool thousands = dot < 0 && comma >= 0 && text.Length - comma - 1 == 3 && culture.NumberFormat.NumberDecimalSeparator != ",";
            if (comma > dot && !thousands) text = text.Replace(".", "").Replace(',', '.');
            else text = text.Replace(",", "");
            return decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }

    private void Move(string[] row, int delta)
    {
        int i = _rows.IndexOf(row), j = i + delta;
        if (i < 0 || j < 0 || j >= _rows.Count) return;
        (_rows[i], _rows[j]) = (_rows[j], _rows[i]);
        Rebuild();
        Send();
        // Keep the keyboard on the row that moved.
        int stride = _cols.Count + 3, button = _cols.Count + (delta < 0 ? 0 : 1);
        var target = _rowInputs.ElementAtOrDefault(j * stride + button);
        if (target is { IsVisible: true }) target.Focus();
        else _rowInputs.ElementAtOrDefault(j * stride)?.Focus();
    }

    private void Remove(string[] row)
    {
        int i = _rows.IndexOf(row);
        if (i < 0) return;
        _rows.RemoveAt(i);
        Rebuild();
        Send();
        int stride = _cols.Count + 3;
        var next = _rowInputs.ElementAtOrDefault(Math.Min(i, _rows.Count - 1) * stride);
        (next ?? (Control?)_addBox ?? _add).Focus();
    }

    private object Current() => _flat
        ? _rows.Select(r => r.Length > 0 ? r[0] : "").ToArray()
        : _rows.Select(r => r.ToArray()).ToArray();

    private void Send() => Changed(Spec.Key!, Current());

    internal static List<string[]> Parse(object? value, bool flat, int columns)
    {
        var rows = new List<string[]>();
        if (value is not IEnumerable items || value is string) return rows;
        foreach (var item in items)
        {
            if (flat) { rows.Add([Text(item)]); continue; }
            var cells = item is IEnumerable e && item is not string ? e.Cast<object?>().Select(Text).ToList() : new List<string> { Text(item) };
            while (cells.Count < columns) cells.Add("");
            rows.Add(cells.ToArray());
        }
        return rows;
    }

    // Same rows and columns: the cells take the new values where they are (a cell being typed in
    // keeps the keyboard). False when only a rebuild will do.
    private bool UpdateInPlace(List<string[]> rows)
    {
        if (rows.Count != _rows.Count || rows.Where((r, i) => r.Length != _rows[i].Length).Any()) return false;
        int stride = _cols.Count + 3;
        for (int i = 0; i < rows.Count; i++)
            for (int c = 0; c < Math.Min(_cols.Count, rows[i].Length); c++)
                if (_rowInputs.ElementAtOrDefault(i * stride + c) is ComboBox combo && rows[i][c].Length > 0 && combo.ItemsSource is IList<string> items && !items.Contains(rows[i][c]))
                    return false;
        using (Quiet())
        {
            for (int i = 0; i < rows.Count; i++)
            {
                for (int c = 0; c < rows[i].Length; c++)
                {
                    if (_rows[i][c] == rows[i][c]) continue;
                    _rows[i][c] = rows[i][c];
                    // Cells past the columns are kept but have no control.
                    if (c >= _cols.Count) continue;
                    switch (_rowInputs.ElementAtOrDefault(i * stride + c))
                    {
                        case TextBox box: box.Text = rows[i][c]; break;
                        case NumericUpDown num: num.Value = decimal.TryParse(rows[i][c], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null; break;
                        case ComboBox combo: combo.SelectedItem = rows[i][c].Length > 0 ? rows[i][c] : (combo.ItemsSource as IList<string>)?.FirstOrDefault(); break;
                    }
                }
            }
        }
        return true;
    }

    private static string Text(object? v) => v switch
    {
        null => "",
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

    public override void SetValue(string key, object? value)
    {
        var rows = Parse(value, _flat, _cols.Count);
        if (rows.Count == _rows.Count && rows.Zip(_rows).All(p => p.First.SequenceEqual(p.Second))) return;
        if (!UpdateInPlace(rows))
        {
            _rows.Clear();
            _rows.AddRange(rows);
            Rebuild();
        }
        Agree(Spec.Key!, Current());
    }
}

// AddMultiSelect: the chosen options as chips (× removes one), and a box listing the rest to add
// from. A refresh button asks Streamer.bot for a new list (the loader, or onRefresh). Stores string[]
// in the order chosen.
internal sealed class MultiSelectView(FieldSpec spec, WindowContext ctx) : FieldView(spec, ctx)
{
    private readonly List<string> _selected = new();
    private readonly List<Control> _chipInputs = new();
    private List<string> _options = new();
    private WrapPanel _chips = null!;
    private TextBlock _empty = null!;
    private ComboBox _picker = null!;
    private Button? _refresh;

    public override IEnumerable<Control> Inputs => _refresh is null ? [.. _chipInputs, _picker] : [.. _chipInputs, _picker, _refresh];

    internal IReadOnlyList<string> Selected => _selected;
    internal ComboBox Picker => _picker;
    internal Button? RefreshButton => _refresh;

    public override Control BuildControl()
    {
        _options = Spec.PropStrings("options").ToList();
        var root = new StackPanel { Spacing = 8 };
        _chips = new WrapPanel { Orientation = Orientation.Horizontal };
        _empty = new TextBlock { Text = Ctx.S("NothingSelected", "Nothing selected yet."), Foreground = P["TextMuted"], FontSize = P.SmallFontSize, Margin = new Thickness(2, 2, 0, 2) };
        root.Children.Add(_chips);
        root.Children.Add(_empty);

        _picker = new ComboBox { PlaceholderText = Spec.Placeholder ?? Ctx.S("AddPlaceholder", "Add…"), Width = InputWidth(Ctx.Layout("dropdownWidth", 240)), FontSize = P.FontSize };
        Ui.Named(_picker, Spec.Label + " " + Ctx.S("AddItem", "Add item"), Spec.Description);
        // What's highlighted is added when the list closes on it (a click, or Enter in the list), or
        // with Enter on the closed box; arrow keys and the wheel only move through the options, and
        // Escape adds nothing. Opening clears the highlight, so closing without a choice adds nothing.
        _picker.DropDownOpened += (_, _) => ClearPick();
        _picker.DropDownClosed += (_, _) =>
        {
            if (_cancelPick) { _cancelPick = false; ClearPick(); return; }
            Dispatcher.UIThread.Post(CommitPick);
        };
        _picker.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && _picker.IsDropDownOpen) _cancelPick = true;
            else if (e.Key == Key.Enter && !_picker.IsDropDownOpen && _picker.SelectedItem is string)
            {
                CommitPick();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        _picker.LostFocus += (_, _) =>
        {
            if (!_picker.IsDropDownOpen && !_picker.IsKeyboardFocusWithin) ClearPick();
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Children.Add(_picker);
        if (Spec.PropBool("refreshable"))
        {
            _refresh = DropdownView.RefreshButton(Ctx, Spec);
            row.Children.Add(_refresh);
        }
        root.Children.Add(row);
        Rebuild();
        return root;
    }

    private void Rebuild()
    {
        _chips.Children.Clear();
        _chipInputs.Clear();
        foreach (var item in _selected.ToList())
        {
            var remove = CopyView.IconButton(Ctx, ListView.RemoveIcon, Ctx.S("Remove", "Remove") + " " + item + " (" + Spec.Label + ")");
            remove.Padding = new Thickness(3);
            remove.Click += (_, _) =>
            {
                int i = _selected.IndexOf(item);
                _selected.Remove(item);
                Rebuild();
                Send();
                (_chipInputs.ElementAtOrDefault(Math.Min(i, _chipInputs.Count - 1)) ?? _picker).Focus();
            };
            _chipInputs.Add(remove);
            _chips.Children.Add(new Border
            {
                Background = P["Chip"],
                BorderBrush = P["ChipBorder"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(12, 3, 4, 3),
                Margin = new Thickness(0, 0, 6, 6),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children = { new TextBlock { Text = item, Foreground = P["Text"], FontSize = P.FontSize, VerticalAlignment = VerticalAlignment.Center }, remove },
                },
            });
        }
        _empty.IsVisible = _selected.Count == 0;
        using (Quiet())
        {
            _picker.ItemsSource = _options.Where(o => !_selected.Contains(o)).ToList();
            _picker.SelectedItem = null;
        }
    }

    private bool _cancelPick;

    private void CommitPick()
    {
        if (_picker.SelectedItem is not string s) return;
        if (!_selected.Contains(s)) _selected.Add(s);
        Rebuild();
        Send();
        _picker.Focus();
    }

    private void ClearPick()
    {
        using (Quiet()) _picker.SelectedItem = null;
    }

    private void Send() => Changed(Spec.Key!, _selected.ToArray());

    public override void Respec(FieldSpec spec)
    {
        base.Respec(spec);
        _options = spec.PropStrings("options").ToList();
        Rebuild();
    }

    public override void SetValue(string key, object? value)
    {
        var items = value is System.Collections.IEnumerable e && value is not string ? e.Cast<object?>().Select(o => o?.ToString() ?? "").Where(s => s.Length > 0).Distinct().ToList() : new List<string>();
        if (items.SequenceEqual(_selected)) return;
        _selected.Clear();
        _selected.AddRange(items);
        Rebuild();
        Agree(Spec.Key!, _selected.ToArray());
    }
}
