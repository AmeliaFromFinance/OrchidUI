using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OrchidUI.Protocol;

namespace OrchidUI
{
    // One class per Add* method. Each has its defaults and value rules (Normalize: the stored form
    // of a value) and describes itself to the host.

    internal sealed class TitleField : Field
    {
        private readonly string _text;
        public TitleField(string text) { _text = text ?? ""; }
        internal override string Kind { get { return FieldKinds.Title; } }
        internal override void Describe(FieldSpec spec) { spec.Props["text"] = _text; }
    }

    internal sealed class DescriptionField : Field
    {
        private readonly string _text;
        public DescriptionField(string text) { _text = text ?? ""; }
        internal override string Kind { get { return FieldKinds.Description; } }
        internal override void Describe(FieldSpec spec) { spec.Props["text"] = _text; }
    }

    internal sealed class SeparatorField : Field
    {
        internal override string Kind { get { return FieldKinds.Separator; } }
    }

    internal sealed class InfoField : Field
    {
        private readonly string _text;
        private readonly InfoKind _kind;
        public InfoField(string text, InfoKind kind) { _text = text ?? ""; _kind = kind; }
        internal override string Kind { get { return FieldKinds.Info; } }

        internal override void Describe(FieldSpec spec)
        {
            spec.Props["text"] = _text;
            spec.Props["kind"] = _kind.ToString().ToLowerInvariant();
        }
    }

    // A WPF element can't cross into another process: the host shows a placeholder card instead.
    internal sealed class CustomField : Field
    {
        public CustomField(Func<object> factory) { Factory = factory; }
        internal Func<object> Factory { get; private set; }
        internal override string Kind { get { return FieldKinds.Custom; } }
    }

    internal sealed class TextField : Field
    {
        private readonly int _lines;
        private readonly bool _password;
        private readonly bool _wide;

        public TextField(string def, int lines, bool password, bool wide)
        {
            _lines = Math.Max(1, lines);
            _password = password;
            _wide = wide;
            DefaultValue = def ?? "";
        }

        internal bool IsPassword { get { return _password; } }
        internal override CardLayout NaturalLayout { get { return _lines > 1 || _wide ? CardLayout.Stacked : CardLayout.Inline; } }
        internal override string Kind { get { return FieldKinds.Text; } }
        internal override object Normalize(string key, object value) { return ValueConverter.To(value, ""); }

        internal override void Describe(FieldSpec spec)
        {
            spec.Props["lines"] = _lines;
            spec.Props["password"] = _password;
            spec.Props["wide"] = _wide;
        }
    }

    internal sealed class CopyField : Field
    {
        private readonly Func<string> _text;
        public CopyField(Func<string> text) { _text = text; }
        internal override string Kind { get { return FieldKinds.Copy; } }

        // Re-run whenever the tab is shown.
        internal string Evaluate()
        {
            string v = "";
            Owner.Safe(() => v = _text != null ? _text() ?? "" : "", "AddCopyBox '" + Label + "'");
            return v;
        }
    }

    internal sealed class ToggleField : Field
    {
        public ToggleField(bool def) { DefaultValue = def; ValueType = typeof(bool); }
        internal override string Kind { get { return FieldKinds.Toggle; } }
        internal override object Normalize(string key, object value) { return ValueConverter.To(value, false); }
    }

    internal sealed class SegmentedField : Field
    {
        private readonly string[] _options;

        public SegmentedField(string[] options, int defaultIndex)
        {
            _options = (options ?? new string[0]).Where(o => o != null).Distinct().ToArray();
            DefaultValue = _options.Length == 0 ? "" : _options[Math.Max(0, Math.Min(_options.Length - 1, defaultIndex))];
        }

        internal override string Kind { get { return FieldKinds.Segmented; } }

        internal override object Normalize(string key, object value)
        {
            string s = ValueConverter.To(value, "");
            return _options.Contains(s) ? s : (string)DefaultValue;
        }

        internal override void Describe(FieldSpec spec) { spec.Props["options"] = _options; }
    }

    internal sealed class SliderField : Field
    {
        private readonly int _min, _max, _def;
        private readonly bool _hasToggle, _toggleDef;

        public SliderField(int min, int max, int def, bool hasToggle, bool toggleDef)
        {
            if (max < min) { int t = min; min = max; max = t; }
            _min = min;
            _max = max;
            _def = Clamp(def);
            _hasToggle = hasToggle;
            _toggleDef = toggleDef;
            DefaultValue = _def;
            ValueType = typeof(int);
        }

        private int Clamp(int v) { return Math.Max(_min, Math.Min(_max, v)); }

        internal string EnabledKey { get { return Key + "_enabled"; } }

        internal override IEnumerable<string> Keys
        {
            get { return _hasToggle ? new[] { Key, EnabledKey } : new[] { Key }; }
        }

        internal override object DefaultFor(string key) { return _hasToggle && key == EnabledKey ? (object)_toggleDef : _def; }
        internal override Type TypeFor(string key) { return _hasToggle && key == EnabledKey ? typeof(bool) : typeof(int); }
        internal override string Kind { get { return FieldKinds.Slider; } }

        internal override object Normalize(string key, object value)
        {
            if (_hasToggle && key == EnabledKey) return ValueConverter.To(value, _toggleDef);
            return Clamp(ValueConverter.To(value, _def));
        }

        internal override string DisplayFor(object value)
        {
            if (Formatter == null) return null;
            try { return Formatter(ValueConverter.To(value, _def)); }
            catch { return ""; }
        }

        internal override void Describe(FieldSpec spec)
        {
            spec.Props["min"] = _min;
            spec.Props["max"] = _max;
            spec.Props["largeChange"] = Math.Max(1, Math.Round((_max - _min) / 20.0));
            spec.Props["toggle"] = _hasToggle;
            if (_hasToggle) spec.Props["enabledKey"] = EnabledKey;
        }
    }

    internal sealed class StepperField : Field
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly double _min, _max, _step;
        private readonly bool _integer;
        private readonly int _decimals;

        public StepperField(double min, double max, double step, double def, bool integer)
        {
            if (max < min) { double t = min; min = max; max = t; }
            if (step <= 0) step = integer ? 1 : 0.1;
            _min = min;
            _max = max;
            _step = step;
            _integer = integer;
            _decimals = integer ? 0 : Math.Min(6, Math.Max(Decimals(step), Decimals(def)));
            ValueType = integer ? typeof(int) : typeof(double);
            DefaultValue = Box(Clamp(Math.Round(def, _decimals)));
        }

        private static int Decimals(double d)
        {
            string s = Math.Abs(d).ToString("R", Inv);
            if (s.Contains("E")) return 4;
            int i = s.IndexOf('.');
            return i < 0 ? 0 : s.Length - i - 1;
        }

        private double Clamp(double v) { return Math.Max(_min, Math.Min(_max, v)); }
        private object Box(double v) { return _integer ? (object)(int)Math.Round(v) : Math.Round(v, _decimals); }

        internal override string Kind { get { return FieldKinds.Stepper; } }

        internal override object Normalize(string key, object value)
        {
            return Box(Clamp(Math.Round(ValueConverter.To(value, 0.0), _decimals)));
        }

        internal override void Describe(FieldSpec spec)
        {
            spec.Props["min"] = _min;
            spec.Props["max"] = _max;
            spec.Props["step"] = _step;
            spec.Props["decimals"] = _decimals;
            spec.Props["integer"] = _integer;
        }
    }

    internal sealed class DropdownField : Field
    {
        private List<string> _options;
        private readonly Func<IEnumerable<string>> _loader;
        private readonly Action _onRefresh;

        public DropdownField(IEnumerable<string> options, int defaultIndex, Func<IEnumerable<string>> loader, Action onRefresh)
        {
            _options = Clean(options);
            _loader = loader;
            _onRefresh = onRefresh;
            DefaultValue = _options.Count == 0 ? "" : _options[Math.Max(0, Math.Min(_options.Count - 1, defaultIndex))];
        }

        internal bool Refreshable { get { return _loader != null || _onRefresh != null; } }
        internal Func<IEnumerable<string>> Loader { get { return _loader; } }
        internal Action OnRefresh { get { return _onRefresh; } }
        internal override string Kind { get { return FieldKinds.Dropdown; } }

        internal static List<string> Clean(IEnumerable<string> options)
        {
            return options == null ? new List<string>() : options.Where(o => o != null).Distinct().ToList();
        }

        internal List<string> Options { get { return _options; } }

        // A stored value that isn't an option is added to the top of the list and selected;
        // an empty value selects the first option.
        internal override object Normalize(string key, object value)
        {
            string s = ValueConverter.To(value, "");
            if (s.Length > 0 && !_options.Contains(s))
            {
                _options = new List<string>(_options);
                _options.Insert(0, s);
            }
            if (s.Length > 0) return s;
            return _options.Count > 0 ? _options[0] : "";
        }

        // UpdateDropdown while the window is open.
        internal string SetOptions(IEnumerable<string> options, int? selectIndex, string current)
        {
            var list = Clean(options);
            if (selectIndex.HasValue && selectIndex.Value >= 0 && selectIndex.Value < list.Count)
                current = list[selectIndex.Value];
            else if (!string.IsNullOrEmpty(current) && !list.Contains(current))
                list.Insert(0, current);
            _options = list;
            if (!string.IsNullOrEmpty(current)) return current;
            return _options.Count > 0 ? _options[0] : "";
        }

        // Before the window exists, only the list is swapped.
        internal void ReplaceOptions(IEnumerable<string> options) { _options = Clean(options); }

        internal override void Describe(FieldSpec spec)
        {
            spec.Props["options"] = _options.ToArray();
            if (Refreshable)
            {
                spec.Props["api"] = "AddRefreshableDropdown";
                spec.Props["refreshable"] = true;
            }
        }
    }

    internal sealed class MultiSelectField : Field
    {
        private List<string> _options;
        private readonly Func<IEnumerable<string>> _loader;
        private readonly Action _onRefresh;

        public MultiSelectField(IEnumerable<string> options, string[] defaults, Func<IEnumerable<string>> loader, Action onRefresh)
        {
            _options = DropdownField.Clean(options);
            _loader = loader;
            _onRefresh = onRefresh;
            ValueType = typeof(string[]);
            DefaultValue = (defaults ?? new string[0]).Where(d => d != null).Distinct().ToArray();
        }

        internal override CardLayout NaturalLayout { get { return CardLayout.Expander; } }
        internal override string Kind { get { return FieldKinds.MultiSelect; } }
        internal Func<IEnumerable<string>> Loader { get { return _loader; } }
        internal Action OnRefresh { get { return _onRefresh; } }
        internal List<string> Options { get { return _options; } }
        internal void ReplaceOptions(IEnumerable<string> options) { _options = DropdownField.Clean(options); }

        internal override string SummaryFor(object value, UiStrings strings)
        {
            var arr = value as Array;
            int n = arr == null ? 0 : arr.Length;
            return n == 0 ? strings.NoneSelected : UiStrings.F(strings.CountSelected, n);
        }

        internal override object Normalize(string key, object value)
        {
            var arr = ValueConverter.To(value, new string[0]);
            return arr.Where(a => !string.IsNullOrEmpty(a)).Distinct().ToArray();
        }

        internal override void Describe(FieldSpec spec)
        {
            spec.Props["api"] = "AddMultiSelect";
            spec.Props["options"] = _options.ToArray();
            if (_loader != null || _onRefresh != null) spec.Props["refreshable"] = true;
        }
    }

    internal sealed class ColorField : Field
    {
        private readonly bool _alpha;

        public ColorField(string def, bool alpha)
        {
            _alpha = alpha;
            Rgba c;
            if (!Rgba.TryParse(def, out c)) c = Rgba.White;
            if (!alpha) c = c.Opaque();
            DefaultValue = c.ToHex(alpha);
        }

        internal override string Kind { get { return FieldKinds.Color; } }

        internal override object Normalize(string key, object value)
        {
            Rgba c;
            if (!Rgba.TryParse(ValueConverter.To(value, ""), out c)) Rgba.TryParse((string)DefaultValue, out c);
            if (!_alpha) c = c.Opaque();
            return c.ToHex(_alpha);
        }

        internal override void Describe(FieldSpec spec) { spec.Props["alpha"] = _alpha; }
    }

    internal sealed class DurationField : Field
    {
        private readonly bool _showDays;

        public DurationField(int days, int hours, int minutes, int seconds, bool showDays)
        {
            _showDays = showDays;
            DefaultValue = DurationUtil.Format(DurationUtil.Total(days, hours, minutes, seconds));
        }

        internal override string Kind { get { return FieldKinds.Duration; } }

        internal override object Normalize(string key, object value)
        {
            long total;
            if (!DurationUtil.TryParseSeconds(ValueConverter.To(value, ""), out total)) total = 0;
            return DurationUtil.Format(Math.Max(0, total));
        }

        internal override void Describe(FieldSpec spec) { spec.Props["showDays"] = _showDays; }
    }

    internal sealed class PathField : Field
    {
        private readonly bool _folder;
        private readonly string _filter;
        private readonly string _initialDir;
        private readonly string _actionText;

        public PathField(bool folder, string def, string filter, string initialDir, Action action, string actionText)
        {
            _folder = folder;
            _filter = filter;
            _initialDir = initialDir;
            _actionText = string.IsNullOrEmpty(actionText) ? "Run" : actionText;
            OnAction = action;
            DefaultValue = def ?? "";
        }

        // The second button (AddFilePicker's onAction), run in Streamer.bot like a button's lambda.
        internal Action OnAction { get; private set; }
        internal string ActionText { get { return _actionText; } }

        internal override CardLayout NaturalLayout { get { return CardLayout.Stacked; } }
        internal override string Kind { get { return FieldKinds.Path; } }
        internal override object Normalize(string key, object value) { return ValueConverter.To(value, ""); }

        internal override void Describe(FieldSpec spec)
        {
            spec.Props["api"] = _folder ? "AddFolderPicker" : "AddFilePicker";
            spec.Props["folder"] = _folder;
            if (!string.IsNullOrEmpty(_filter)) spec.Props["filter"] = _filter;
            var start = HostPaths.ToHost(_initialDir);
            if (start != null) spec.Props["start"] = start;
            if (OnAction != null) spec.Props["actionText"] = _actionText;
        }
    }

    internal sealed class ButtonField : Field
    {
        private readonly string _text;
        private readonly bool _primary;

        public ButtonField(string text, Action onClick, bool primary)
        {
            _text = string.IsNullOrEmpty(text) ? "Run" : text;
            OnClick = onClick;
            _primary = primary;
        }

        internal Action OnClick { get; private set; }
        internal string Text { get { return _text; } }
        internal override string Kind { get { return FieldKinds.Button; } }

        internal override void Describe(FieldSpec spec)
        {
            spec.Props["text"] = _text;
            spec.Props["primary"] = _primary;
        }
    }

    internal sealed class TableField : Field
    {
        private readonly Column[] _cols;
        private readonly bool _flat;

        public TableField(Column[] cols, string[][] defaults, bool flat, string addText)
        {
            _cols = (cols == null || cols.Length == 0) ? new[] { Column.Text("Value") } : cols;
            _flat = flat;
            var rows = (defaults ?? new string[0][]).Select(r => r ?? new string[0]).ToArray();
            if (flat)
            {
                ValueType = typeof(string[]);
                DefaultValue = rows.Select(r => r.Length > 0 ? r[0] : "").ToArray();
            }
            else
            {
                ValueType = typeof(string[][]);
                DefaultValue = rows;
            }
        }

        internal override CardLayout NaturalLayout { get { return CardLayout.Expander; } }
        internal override string Kind { get { return FieldKinds.List; } }
        internal bool Flat { get { return _flat; } }

        internal override string SummaryFor(object value, UiStrings strings)
        {
            var arr = value as Array;
            int n = arr == null ? 0 : arr.Length;
            return n == 0 ? strings.Empty : UiStrings.F(_flat ? strings.ItemCount : strings.RowCount, n);
        }

        // Every row gets one cell per column, and an empty choice cell
        // selects the column's first option.
        internal override object Normalize(string key, object value)
        {
            var rows = _flat
                ? ValueConverter.To(value, new string[0]).Select(s => new[] { s }).ToArray()
                : ValueConverter.To(value, new string[0][]).Select(r => r ?? new string[0]).ToArray();
            var normalised = rows.Select(r => _cols.Select((c, i) => Cell(c, i < r.Length ? (r[i] ?? "") : "")).ToArray()).ToArray();
            if (_flat) return normalised.Select(r => r.Length > 0 ? r[0] : "").ToArray();
            return normalised;
        }

        private static Dictionary<string, object> ColumnSpec(Column c)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "header", c.Header ?? "" },
                { "kind", c.Kind == Column.ColumnKind.Number ? "number" : c.Kind == Column.ColumnKind.Choice ? "choice" : "text" },
                { "weight", c.Weight > 0 ? c.Weight : 1 },
            };
            if (c.Kind == Column.ColumnKind.Number) { d["min"] = c.Min; d["max"] = c.Max; }
            if (c.Kind == Column.ColumnKind.Choice) d["options"] = (c.Options ?? new string[0]).Where(o => o != null).ToArray();
            if (c.Placeholder != null) d["placeholder"] = c.Placeholder;
            return d;
        }

        private static string Cell(Column c, string v)
        {
            if (c.Kind != Column.ColumnKind.Choice) return v;
            if (v.Length > 0) return v;
            return c.Options.Length > 0 ? c.Options[0] : "";
        }

        internal override void Describe(FieldSpec spec)
        {
            spec.Props["api"] = _flat ? "AddTextList" : "AddTable";
            spec.Props["flat"] = _flat;
            spec.Props["columns"] = _cols.Select(c => (object)ColumnSpec(c)).ToArray();
        }
    }

    internal sealed class ImageField : Field
    {
        public ImageField(string def) { DefaultValue = def ?? ""; }
        internal override CardLayout NaturalLayout { get { return CardLayout.Stacked; } }
        internal override string Kind { get { return FieldKinds.Image; } }
        internal override object Normalize(string key, object value) { return ValueConverter.To(value, ""); }

        internal override void Describe(FieldSpec spec)
        {
            spec.Props["api"] = "AddImageInput";
            spec.Props["filter"] = "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp";
        }
    }

    // Paths between Streamer.bot (Windows paths, Z:\ for Unix ones under Wine) and orchid-host,
    // which runs natively and so sees Unix paths under Wine.
    internal static class HostPaths
    {
        // A folder for the host's file dialog to start in, or null. Relative paths are in
        // Streamer.bot's folder, as everywhere else.
        public static string ToHost(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                string full = System.IO.Path.IsPathRooted(path.Trim()) ? path.Trim() : System.IO.Path.Combine(Orchid.StreamerBotDirectory, path.Trim());
                full = System.IO.Path.GetFullPath(full);
                return OrchidPlatform.IsWine ? WinePaths.ToUnix(full) : full;
            }
            catch (Exception) { return null; }
        }

        // A path the user picked in the host's dialog, as Streamer.bot sees it.
        public static string FromHost(string path)
        {
            if (string.IsNullOrEmpty(path)) return path ?? "";
            if (OrchidPlatform.IsWine && path.StartsWith("/", StringComparison.Ordinal)) return WinePaths.FromUnix(path);
            return path;
        }
    }
}
