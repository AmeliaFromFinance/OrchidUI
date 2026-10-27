using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using OrchidUI.Protocol;

namespace OrchidUI
{
    // Builds and shows a settings window for a Streamer.bot extension. The window itself is drawn by
    // orchid-host, a native app; everything else (storage, callbacks, validation, themes) happens
    // here, inside Streamer.bot.
    // Values are stored as JSON in the persisted global variable "orchid_{extensionName}".
    public sealed class Orchid
    {
        public const string FrameworkVersion = "1.0.0";

        private static readonly object RegistryLock = new object();
        private static readonly Dictionary<string, Orchid> OpenWindows = new Dictionary<string, Orchid>(StringComparer.OrdinalIgnoreCase);

        // orchid-sandbox --dump and the tests: receives the spec instead of a host being started.
        internal static Action<Orchid, WindowSpec> ShowHook;

        // How long a blocking dialog waits for an answer before giving up with -1.
        internal static TimeSpan DialogTimeout = TimeSpan.FromMinutes(10);

        private readonly CphBridge _cph;
        private readonly object _lock = new object();
        private readonly List<Field> _fields = new List<Field>();
        private readonly List<string> _tabs = new List<string>();
        private readonly Dictionary<string, string> _tabIcons = new Dictionary<string, string>();
        private readonly Dictionary<string, Field> _byKey = new Dictionary<string, Field>(StringComparer.Ordinal);
        private readonly Dictionary<string, object> _pending = new Dictionary<string, object>(StringComparer.Ordinal);
        private Dictionary<string, object> _saved;

        private volatile WindowController _controller;
        private volatile bool _starting;

        internal string HeaderImage, HeaderTitle, HeaderSubtitle, Subtitle, FooterText, RunOnSaveActionName;
        internal double HeaderHeight = 120;
        internal readonly List<KeyValuePair<string, string>> Links = new List<KeyValuePair<string, string>>();
        internal int ErrorCount;

        public Orchid(object cph, string extensionName, string version = null, bool debug = false)
        {
            if (string.IsNullOrWhiteSpace(extensionName)) throw new ArgumentException("extensionName is required.", "extensionName");
            _cph = new CphBridge(cph);
            ExtensionName = extensionName.Trim();
            Version = version;
            DebugLogging = debug;
            StorageKey = StorageKeyFor(ExtensionName);
            Theme = new OrchidTheme();
            Window = new WindowOptions();
            Layout = new LayoutOptions();
            Strings = new UiStrings();
            Values = new OrchidValues(this);
            _saved = Store.Load(_cph, StorageKey, LogWarn);
            LogDebug("Loaded " + _saved.Count + " stored values from '" + StorageKey + "'.");
        }

        public string ExtensionName { get; private set; }
        public string Version { get; private set; }
        public string StorageKey { get; private set; }
        public bool DebugLogging { get; set; }
        public OrchidValues Values { get; private set; }
        public object Cph { get { return _cph.Raw; } }
        public bool IsOpen { get { var c = _controller; return c != null && c.IsOpen; } }

        // Customisation. Change these before ShowUI().
        public OrchidTheme Theme { get; private set; }
        public WindowOptions Window { get; private set; }
        public LayoutOptions Layout { get; private set; }
        public UiStrings Strings { get; private set; }

        // Raised after settings are saved (on the window's callback thread).
        public event Action<OrchidSettings> Saved;

        public static string StreamerBotDirectory { get { return AppDomain.CurrentDomain.BaseDirectory; } }

        public static string StorageKeyFor(string extensionName) { return "orchid_" + (extensionName ?? "").Trim(); }

        internal List<Field> Fields { get { return _fields; } }
        internal List<string> Tabs { get { return _tabs; } }
        internal Dictionary<string, string> TabIcons { get { return _tabIcons; } }
        internal CphBridge Bridge { get { return _cph; } }
        internal object SyncRoot { get { return _lock; } }

        // Single instance

        // Brings an already-open window for this extension to the front and returns true.
        public static bool AlreadyOpened(string extensionName)
        {
            Orchid existing;
            lock (RegistryLock)
            {
                if (!OpenWindows.TryGetValue((extensionName ?? "").Trim(), out existing)) return false;
            }
            if (!existing.IsOpen && !existing._starting) return false;
            existing.BringToFront();
            return true;
        }

        public void BringToFront()
        {
            var c = _controller;
            if (c != null) c.BringToFront();
        }

        // Look & feel (call before ShowUI)

        public Orchid SetTheme(OrchidTheme theme)
        {
            if (theme != null) Theme = theme.Clone();
            return this;
        }

        // Orchid, Amethyst, Sakura, Synthwave, Graphite or Daylight.
        public Orchid SetTheme(string preset) { Theme = OrchidTheme.Preset(preset); return this; }

        public Orchid ConfigureTheme(Action<OrchidTheme> configure) { if (configure != null) configure(Theme); return this; }
        public Orchid ConfigureWindow(Action<WindowOptions> configure) { if (configure != null) configure(Window); return this; }
        public Orchid ConfigureLayout(Action<LayoutOptions> configure) { if (configure != null) configure(Layout); return this; }
        public Orchid ConfigureStrings(Action<UiStrings> configure) { if (configure != null) configure(Strings); return this; }

        public Orchid SetAccent(string accent, string accent2 = null)
        {
            Theme.Accent = accent;
            if (accent2 != null) Theme.Accent2 = accent2;
            return this;
        }

        public Orchid SetFont(string family, double size = 0)
        {
            if (!string.IsNullOrWhiteSpace(family)) Theme.FontFamily = family + ", " + OrchidTheme.DefaultFonts;
            if (size > 0) Theme.FontSize = size;
            return this;
        }

        // A .ttf/.otf file (absolute, or relative to the Streamer.bot folder). Handy on Linux where Segoe UI is missing.
        public Orchid SetFontFile(string path) { Theme.FontFile = path; return this; }

        public Orchid SetWindowSize(double width, double height)
        {
            if (width > 0) Window.Width = width;
            if (height > 0) Window.Height = height;
            return this;
        }

        public Orchid SetResizable(bool resizable) { Window.Resizable = resizable; return this; }

        public Orchid SetMinSize(double width, double height) { Window.MinWidth = width; Window.MinHeight = height; return this; }

        public Orchid SetMaxSize(double width, double height)
        {
            Window.MaxWidth = width > 0 ? width : double.PositiveInfinity;
            Window.MaxHeight = height > 0 ? height : double.PositiveInfinity;
            return this;
        }

        public Orchid SetNavigation(NavigationStyle style) { Layout.Navigation = style; return this; }
        public Orchid SetDensity(Density density) { Layout.Density = density; return this; }
        public Orchid SetAutoSave(bool autoSave) { Layout.AutoSave = autoSave; return this; }

        // Has no effect; see WindowOptions.Compatibility.
        public Orchid SetCompatibilityMode(bool? on) { Window.Compatibility = on; return this; }

        // Loads a JSON file: either flat theme keys, or sections { "preset", "theme", "window", "layout", "strings" }.
        public Orchid LoadThemeFile(string path)
        {
            try
            {
                string full = Path.IsPathRooted(path ?? "") ? path : Path.Combine(StreamerBotDirectory, path ?? "");
                if (!File.Exists(full)) { LogWarn("Theme file not found: " + full); return this; }
                ApplyConfig(Json.ParseObject(File.ReadAllText(full)), Path.GetFileName(full));
            }
            catch (Exception ex) { LogWarn("Theme file could not be loaded: " + ex.Message); }
            return this;
        }

        public Orchid LoadThemeJson(string json)
        {
            try { ApplyConfig(Json.ParseObject(json), "JSON"); }
            catch (Exception ex) { LogWarn("Theme JSON could not be parsed: " + ex.Message); }
            return this;
        }

        private void ApplyConfig(Dictionary<string, object> root, string source)
        {
            object preset;
            if (root.TryGetValue("preset", out preset) && preset is string) Theme = OrchidTheme.Preset((string)preset);
            var bad = new List<string>();
            bool sectioned = false;
            foreach (var section in new[] { "theme", "window", "layout", "strings" })
            {
                object v;
                if (!root.TryGetValue(section, out v) || !(v is IDictionary<string, object>)) continue;
                sectioned = true;
                var values = (IDictionary<string, object>)v;
                object target = section == "theme" ? (object)Theme : section == "window" ? Window : section == "layout" ? (object)Layout : Strings;
                bad.AddRange(ConfigApplier.Apply(target, values).Select(k => section + "." + k));
            }
            if (!sectioned) bad.AddRange(Theme.Apply(root));
            else bad.AddRange(root.Keys.Where(k => k != "preset" && !k.StartsWith("$") && new[] { "theme", "window", "layout", "strings" }.All(s => s != k)));
            if (bad.Count > 0) LogWarn("Ignored unknown or invalid keys in " + source + ": " + string.Join(", ", bad));
        }

        // Shorthands that set a few theme and window values at once.
        public Orchid SetPalette(string darkest, string dark, string light, string lightest)
        {
            var c1 = Rgba.Parse(darkest, Rgba.Parse("#4C2365", Rgba.Black));
            Theme = OrchidTheme.Amethyst();
            Theme.Background = Rgba.Mix(Rgba.Parse("#161616", Rgba.Black), c1, 0.2).ToHex(false);
            Theme.Accent = lightest;
            Theme.Accent2 = dark;
            return this;
        }

        public Orchid SetAccentColors(string accentHex, string deepHex)
        {
            Theme.Accent = accentHex;
            Theme.Accent2 = Rgba.Mix(Rgba.Parse(deepHex, Rgba.Black), Rgba.Parse(accentHex, Rgba.White), 0.45).ToHex(false);
            return this;
        }

        public Orchid SetBackdrop(bool mica) { Window.Backdrop = mica ? BackdropMode.Auto : BackdropMode.Solid; return this; }

        // Window content

        public Orchid SetSubtitle(string text) { Subtitle = text; return this; }
        public Orchid SetFooterText(string text) { FooterText = text; return this; }

        public Orchid AddLink(string text, string url)
        {
            if (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(url)) Links.Add(new KeyValuePair<string, string>(text, url));
            return this;
        }

        public Orchid AddHeader(string imageUrlOrPath, double height = 120)
        {
            HeaderImage = imageUrlOrPath;
            HeaderHeight = Math.Max(40, height);
            return this;
        }

        public Orchid AddHeaderText(string title, string subtitle = null)
        {
            HeaderTitle = title;
            HeaderSubtitle = subtitle;
            return this;
        }

        public Orchid RunActionOnSave(string actionName) { RunOnSaveActionName = actionName; return this; }

        public Orchid SetTabIcon(string tab, string icon)
        {
            tab = NormalizeTab(tab);
            EnsureTab(tab);
            _tabIcons[tab] = icon;
            return this;
        }

        public OrchidTab Tab(string name, string icon = null)
        {
            name = NormalizeTab(name);
            EnsureTab(name);
            if (icon != null) _tabIcons[name] = icon;
            return new OrchidTab(this, name);
        }

        private static string NormalizeTab(string tab) { return string.IsNullOrWhiteSpace(tab) ? "General" : tab.Trim(); }

        private void EnsureTab(string tab)
        {
            lock (_lock) { if (!_tabs.Contains(tab)) _tabs.Add(tab); }
        }

        // Layout elements

        public Field AddTitle(string text, string tab) { return Register(new TitleField(text), null, null, tab, null); }
        public Field AddDescription(string text, string tab) { return Register(new DescriptionField(text), null, null, tab, null); }
        public Field AddSeparator(string tab) { return Register(new SeparatorField(), null, null, tab, null); }
        public Field AddInlineSeparator(string tab) { return AddSeparator(tab); }
        public Field AddInfoBox(string text, string tab, InfoKind kind = InfoKind.Info) { return Register(new InfoField(text, kind), null, null, tab, null); }

        // Your own WPF element. It can't cross into the native window: OrchidUI shows a placeholder card and logs a warning.
        public Field AddCustom(string tab, Func<object> createElement) { return Register(new CustomField(createElement), null, null, tab, null); }

        // Inputs

        public Field AddTextbox(string label, string description, string tab, string key, string defaultValue = "", string placeholder = null)
        {
            var f = new TextField(defaultValue, 1, false, false) { PlaceholderText = placeholder };
            return Register(f, label, description, tab, key);
        }

        // Masked in the UI, stored as plain text.
        public Field AddPasswordBox(string label, string description, string tab, string key, string defaultValue = "")
        {
            return Register(new TextField(defaultValue, 1, true, false), label, description, tab, key);
        }

        public Field AddTextArea(string label, string description, string tab, string key, string defaultValue = "", int lines = 3)
        {
            return Register(new TextField(defaultValue, Math.Max(2, lines), false, false), label, description, tab, key);
        }

        // Single line, full card width (chat responses).
        public Field AddResponseBox(string label, string description, string tab, string key, string defaultValue = "")
        {
            return Register(new TextField(defaultValue, 1, false, true), label, description, tab, key);
        }

        // Read-only text with a copy button, e.g. an overlay URL. The function is re-run whenever the tab is shown.
        public Field AddCopyBox(string label, string description, string tab, Func<string> text)
        {
            return Register(new CopyField(text), label, description, tab, null);
        }

        public Field AddCopyBox(string label, string description, string tab, string text)
        {
            return AddCopyBox(label, description, tab, () => text);
        }

        public Field AddToggleSwitch(string label, string description, string tab, string key, bool defaultValue)
        {
            return Register(new ToggleField(defaultValue), label, description, tab, key);
        }

        // 2-6 options shown as a row of buttons. Stores the selected text.
        public Field AddSegmented(string label, string description, string tab, string key, string[] options, int defaultIndex = 0)
        {
            return Register(new SegmentedField(options, defaultIndex), label, description, tab, key);
        }

        public Field AddSlider(string label, string description, string tab, string key, int min, int max, int defaultValue)
        {
            return Register(new SliderField(min, max, defaultValue, false, false), label, description, tab, key);
        }

        // Stores "{key}" (int) and "{key}_enabled" (bool).
        public Field AddSliderWithToggleSwitch(string label, string description, string tab, string key, int min, int max, int defaultValue, bool enabledByDefault)
        {
            return Register(new SliderField(min, max, defaultValue, true, enabledByDefault), label, description, tab, key);
        }

        public Field AddDecimalStepper(string label, string description, string tab, string key, double min, double max, double step, double defaultValue)
        {
            return Register(new StepperField(min, max, step, defaultValue, false), label, description, tab, key);
        }

        public Field AddIntegerStepper(string label, string description, string tab, string key, int min, int max, int defaultValue, int step = 1)
        {
            return Register(new StepperField(min, max, Math.Max(1, step), defaultValue, true), label, description, tab, key);
        }

        public Field AddDropdown(string label, string description, string tab, string key, string[] options, int defaultIndex = 0)
        {
            return Register(new DropdownField(options, defaultIndex, null, null), label, description, tab, key);
        }

        public Field AddRefreshableDropdown(string label, string description, string tab, string key, Func<IEnumerable<string>> loadOptions, int defaultIndex = 0)
        {
            return Register(new DropdownField(SafeLoad(loadOptions, label), defaultIndex, loadOptions, null), label, description, tab, key);
        }

        public Field AddRefreshableDropdown(string label, string description, string tab, string key, string[] options, Action onRefresh, int defaultIndex = 0)
        {
            return Register(new DropdownField(options, defaultIndex, null, onRefresh), label, description, tab, key);
        }

        public Field AddMultiSelect(string label, string description, string tab, string key, string[] options, string[] defaultSelection = null)
        {
            return Register(new MultiSelectField(options, defaultSelection, null, null), label, description, tab, key);
        }

        public Field AddMultiSelect(string label, string description, string tab, string key, Func<IEnumerable<string>> loadOptions, string[] defaultSelection = null)
        {
            return Register(new MultiSelectField(SafeLoad(loadOptions, label), defaultSelection, loadOptions, null), label, description, tab, key);
        }

        // Stores "#RRGGBB" (or "#AARRGGBB" with allowAlpha).
        public Field AddColorPicker(string label, string description, string tab, string key, string defaultHex = "#FFFFFF", bool allowAlpha = false)
        {
            return Register(new ColorField(defaultHex, allowAlpha), label, description, tab, key);
        }

        public Field AddColorpicker(string label, string description, string tab, string key, string defaultHex = "#FFFFFF")
        {
            return AddColorPicker(label, description, tab, key, defaultHex, false);
        }

        // Stores "dd:hh:mm:ss".
        public Field AddDurationPicker(string label, string description, string tab, string key, int days, int hours, int minutes, int seconds, bool showDays = true)
        {
            return Register(new DurationField(days, hours, minutes, seconds, showDays), label, description, tab, key);
        }

        // onAction adds a second button; read the chosen path inside it with GetPendingValue<string>(key).
        public Field AddFilePicker(string label, string description, string tab, string key, string defaultPath = "",
            string filter = null, string initialDirectory = null, Action onAction = null, string actionText = "Run")
        {
            return Register(new PathField(false, defaultPath, filter, initialDirectory, onAction, actionText), label, description, tab, key);
        }

        public Field AddFolderPicker(string label, string description, string tab, string key, string defaultPath = "")
        {
            return Register(new PathField(true, defaultPath, null, null, null, null), label, description, tab, key);
        }

        public Field AddButton(string label, string description, string tab, string buttonText, Action onClick, bool primary = false)
        {
            return Register(new ButtonField(buttonText, onClick, primary), label, description, tab, null);
        }

        public Field AddTextList(string label, string description, string tab, string key, string[] defaults = null, string placeholder = null)
        {
            var rows = (defaults ?? new string[0]).Select(d => new[] { d ?? "" }).ToArray();
            var f = new TableField(new[] { Column.Text("Value") }, rows, true, null) { PlaceholderText = placeholder };
            return Register(f, label, description, tab, key);
        }

        public Field AddTable(string label, string description, string tab, string key, Column[] columns, string[][] defaults = null)
        {
            return Register(new TableField(columns, defaults, false, null), label, description, tab, key);
        }

        // Rows of [dropdown | text]. Presets use "Choice|Text".
        public Field AddSelectionTextList(string label, string description, string tab, string key, string[] choices, string[] presets = null,
            string choiceHeader = "Type", string textHeader = "Text")
        {
            var cols = new[] { Column.Choice(choiceHeader, choices, 1.3), Column.Text(textHeader, 3) };
            return AddTable(label, description, tab, key, cols, SplitPresets(presets));
        }

        // Rows of [dropdown | number | text]. Presets use "Choice|Amount|Text".
        public Field AddSelectionAmountTextList(string label, string description, string tab, string key, string[] choices, string[] presets = null,
            string choiceHeader = "Type", string amountHeader = "Amount", string textHeader = "Text")
        {
            var cols = new[] { Column.Choice(choiceHeader, choices, 1.3), Column.Number(amountHeader, 0, 1000000000, 0.8), Column.Text(textHeader, 2.4) };
            return AddTable(label, description, tab, key, cols, SplitPresets(presets));
        }

        public Field AddImageInput(string label, string description, string tab, string key, string defaultValue = "")
        {
            return Register(new ImageField(defaultValue), label, description, tab, key);
        }

        private static string[][] SplitPresets(string[] presets)
        {
            return (presets ?? new string[0]).Select(p => (p ?? "").Split('|')).ToArray();
        }

        internal IEnumerable<string> SafeLoad(Func<IEnumerable<string>> loader, string label)
        {
            if (loader == null) return new string[0];
            try { return (loader() ?? new string[0]).ToList(); }
            catch (Exception ex)
            {
                LogWarn("Loading options for '" + label + "' failed: " + ex.Message);
                return new string[0];
            }
        }

        private Field Register(Field f, string label, string description, string tab, string key)
        {
            f.Owner = this;
            f.Label = label;
            f.Description = description;
            f.Tab = NormalizeTab(tab);
            f.Key = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
            if (_controller != null)
                LogWarn("'" + (label ?? key) + "' was added after ShowUI() and will not appear. Add all fields before calling ShowUI().");
            lock (_lock)
            {
                if (!_tabs.Contains(f.Tab)) _tabs.Add(f.Tab);
                foreach (var k in f.Keys)
                {
                    if (_byKey.ContainsKey(k)) LogWarn("Duplicate key '" + k + "', the later field wins. Every key must be unique.");
                    _byKey[k] = f;
                    object raw;
                    _pending[k] = _saved.TryGetValue(k, out raw) ? Coerce(f, k, raw) : ValueConverter.Clone(f.DefaultFor(k));
                }
                _fields.Add(f);
            }
            return f;
        }

        private static object Coerce(Field f, string key, object raw)
        {
            return ValueConverter.ConvertOrDefault(raw, f.TypeFor(key), ValueConverter.Clone(f.DefaultFor(key)));
        }

        // Dynamic updates (any thread)

        public void UpdateDropdown(string key, IEnumerable<string> options, int? selectIndex = null)
        {
            Field f;
            lock (_lock) _byKey.TryGetValue(key ?? "", out f);
            var list = (options ?? new string[0]).ToList();
            var dd = f as DropdownField;
            var ms = f as MultiSelectField;
            if (dd == null && ms == null)
            {
                LogWarn("UpdateDropdown: no dropdown with key '" + key + "'.");
                return;
            }
            var c = _controller;
            if (c != null && c.IsOpen && f.Built)
            {
                c.Post(() => c.UpdateOptions(f, list, selectIndex));
                return;
            }
            if (dd != null) dd.ReplaceOptions(list);
            else ms.ReplaceOptions(list);
        }

        public void ShowToast(string message, InfoKind kind = InfoKind.Info)
        {
            var c = _controller;
            if (c != null) c.ShowToast(message, kind);
        }

        // Values

        // Saved value (what other actions see).
        public T GetValue<T>(string key, T fallback = default(T))
        {
            object raw;
            Field f;
            lock (_lock)
            {
                if (!_saved.TryGetValue(key ?? "", out raw))
                {
                    if (!_byKey.TryGetValue(key ?? "", out f)) return fallback;
                    raw = f.DefaultFor(key);
                }
            }
            return ValueConverter.To(raw, fallback);
        }

        // Current value in the window, saved or not. Use this inside button callbacks.
        public T GetPendingValue<T>(string key, T fallback = default(T)) { return ValueConverter.To(GetPendingRaw(key), fallback); }

        public T GetValueFrom<T>(string extensionName, string key, T fallback = default(T))
        {
            if (string.Equals(extensionName, ExtensionName, StringComparison.OrdinalIgnoreCase)) return GetValue(key, fallback);
            return OrchidSettings.Load(_cph.Raw, extensionName).Get(key, fallback);
        }

        // Sets and immediately saves one value; the window updates if open.
        public void SetValue(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            Field f;
            object v;
            Dictionary<string, object> merged;
            lock (_lock)
            {
                _byKey.TryGetValue(key, out f);
                v = f != null ? Coerce(f, key, value) : ValueConverter.Clone(value);
                _pending[key] = v;
                merged = new Dictionary<string, object>(_saved, StringComparer.Ordinal);
                merged[key] = ValueConverter.Clone(v);
            }
            try
            {
                Store.Write(_cph, StorageKey, merged);
                lock (_lock) _saved = merged;
            }
            catch (Exception ex)
            {
                LogError("SetValue('" + key + "') could not be saved: " + ex.Message);
                return;
            }
            var c = _controller;
            if (f != null && f.Built && c != null && c.IsOpen)
            {
                c.Post(() =>
                {
                    lock (_lock)
                    {
                        // The control shows the normalised form; as when a window opens, that form
                        // counts as saved (the stored bytes don't change).
                        var shown = f.Normalize(key, ValueConverter.Clone(v));
                        _pending[key] = shown;
                        var saved = new Dictionary<string, object>(_saved, StringComparer.Ordinal);
                        saved[key] = ValueConverter.Clone(shown);
                        _saved = saved;
                    }
                    c.PushValues(new[] { key });
                });
            }
        }

        public OrchidSettings GetSettings()
        {
            lock (_lock) return new OrchidSettings(ExtensionName, new Dictionary<string, object>(_saved, StringComparer.Ordinal), _cph);
        }

        internal object GetPendingRaw(string key)
        {
            object v;
            lock (_lock) return key != null && _pending.TryGetValue(key, out v) ? v : null;
        }

        internal Field FieldForKey(string key)
        {
            Field f;
            lock (_lock) return key != null && _byKey.TryGetValue(key, out f) ? f : null;
        }

        // Showing the window

        // Starts orchid-host and waits until its window is up (or it fails). blocking: true also waits until it's closed.
        public void ShowUI(bool blocking = false)
        {
            lock (RegistryLock)
            {
                Orchid existing;
                if (OpenWindows.TryGetValue(ExtensionName, out existing) && (existing.IsOpen || existing._starting))
                {
                    existing.BringToFront();
                    return;
                }
                OpenWindows[ExtensionName] = this;
                _starting = true;
            }

            WindowController controller = null;
            try
            {
                ApplyEnvironmentTheme();
                if (_tabs.Count == 0) AddDescription("Nothing to configure yet.", "General");
                EnsureDefaultsPersisted();
                controller = new WindowController(this);
                var hook = ShowHook;
                if (hook != null)
                {
                    hook(this, controller.BuildSpec());
                    return;
                }
                _controller = controller;
                if (!controller.Start())
                {
                    _controller = null;
                    return;
                }
            }
            catch (Exception ex)
            {
                LogError("Could not open the settings window: " + ex);
                _controller = null;
                return;
            }
            finally
            {
                _starting = false;
                if (_controller == null) Unregister();
            }

            Log("Settings window opened (" + OrchidPlatform.Describe() + ", " + controller.LinkDescription + ").");
            if (blocking) controller.WaitClosed();
        }

        // ORCHIDUI_THEME lets end users restyle every OrchidUI window: a preset name or a path to a theme JSON.
        private void ApplyEnvironmentTheme()
        {
            var env = OrchidPlatform.Env("ORCHIDUI_THEME");
            if (env == null) return;
            if (OrchidTheme.PresetNames.Any(n => n.Equals(env, StringComparison.OrdinalIgnoreCase))) Theme = OrchidTheme.Preset(env);
            else LoadThemeFile(env);
        }

        public void Close()
        {
            var c = _controller;
            if (c != null) c.Post(c.RequestClose);
        }

        internal void OnWindowClosed(WindowController controller)
        {
            // A window that was replaced (closed and reopened quickly) mustn't unregister its successor.
            var current = _controller;
            if (current != null && current != controller) return;
            _controller = null;
            Unregister();
        }

        private void Unregister()
        {
            lock (RegistryLock)
            {
                Orchid existing;
                if (OpenWindows.TryGetValue(ExtensionName, out existing) && ReferenceEquals(existing, this))
                    OpenWindows.Remove(ExtensionName);
            }
        }

        // Runs user code; exceptions become a log entry plus a friendly dialog.
        internal void Safe(Action a, string context)
        {
            try { a(); }
            catch (Exception ex)
            {
                LogError("Error in " + context + ": " + ex);
                try { ShowMessage(Strings.ErrorTitle, UiStrings.F(Strings.ErrorBody, "Error in " + context + ":\n\n" + ex.Message)); }
                catch { }
            }
        }

        // Internal state

        private void EnsureDefaultsPersisted()
        {
            Dictionary<string, object> merged;
            int added = 0;
            lock (_lock)
            {
                merged = new Dictionary<string, object>(_saved, StringComparer.Ordinal);
                foreach (var kv in _pending)
                {
                    if (merged.ContainsKey(kv.Key)) continue;
                    merged[kv.Key] = ValueConverter.Clone(kv.Value);
                    added++;
                }
            }
            if (added == 0) return;
            try
            {
                Store.Write(_cph, StorageKey, merged);
                lock (_lock) _saved = merged;
                LogDebug("Stored " + added + " default values.");
            }
            catch (Exception ex) { LogWarn("Could not store default values: " + ex.Message); }
        }

        // Loads every value in its field's form ("#ffffff" -> "#FFFFFF", "90" -> "00:00:01:30").
        // Nothing has been edited yet, so a clean value's normalised form counts as saved.
        internal void NormaliseForWindow()
        {
            foreach (var f in _fields)
            {
                f.Built = true;
                try
                {
                    lock (_lock)
                    {
                        foreach (var k in f.Keys)
                        {
                            object pending;
                            _pending.TryGetValue(k, out pending);
                            var v = f.Normalize(k, ValueConverter.Clone(pending));
                            bool wasClean = !_saved.ContainsKey(k) || ValueConverter.LooseEquals(pending, _saved[k]);
                            _pending[k] = v;
                            if (wasClean && _saved.ContainsKey(k)) _saved[k] = ValueConverter.Clone(v);
                        }
                    }
                }
                catch (Exception ex) { LogWarn("Could not load '" + (f.Label ?? f.Key) + "' into the window: " + ex.Message); }
            }
        }

        // A value from the window: stored in the form the field keeps. Returns that form.
        internal object ApplyChange(Field f, string key, object value)
        {
            object v = f.Normalize(key, value);
            lock (_lock) _pending[key] = v;
            return v;
        }

        internal void RunChangeHandlers(Field f)
        {
            object primary = null;
            lock (_lock)
            {
                if (f.Key != null) _pending.TryGetValue(f.Key, out primary);
            }
            foreach (var handler in f.ChangeHandlers.ToArray())
            {
                var h = handler;
                var value = primary;
                Safe(() => h(value), "OnChange of '" + (f.Key ?? f.Label) + "'");
            }
        }

        internal bool IsKeyDirty(string key)
        {
            lock (_lock)
            {
                object a, b;
                _pending.TryGetValue(key, out a);
                if (!_saved.TryGetValue(key, out b)) return true;
                return !ValueConverter.LooseEquals(a, b);
            }
        }

        internal bool IsDirty
        {
            get
            {
                string[] keys;
                lock (_lock) keys = _fields.Where(f => f.Built).SelectMany(f => f.Keys).ToArray();
                return keys.Any(IsKeyDirty);
            }
        }

        internal bool SaveInternal(bool quiet)
        {
            Dictionary<string, object> merged;
            lock (_lock)
            {
                merged = new Dictionary<string, object>(_saved, StringComparer.Ordinal);
                foreach (var kv in _pending) merged[kv.Key] = ValueConverter.Clone(kv.Value);
            }
            try { Store.Write(_cph, StorageKey, merged); }
            catch (Exception ex)
            {
                LogError("Saving failed: " + ex);
                ShowMessage(Strings.ErrorTitle, ex.Message);
                return false;
            }
            lock (_lock) _saved = merged;
            if (quiet) LogDebug("Settings saved."); else Log("Settings saved.");

            var c = _controller;
            if (c != null) c.MarkSaved(quiet);

            var handler = Saved;
            if (handler != null)
            {
                var snapshot = GetSettings();
                Safe(() => handler(snapshot), "Saved event");
            }
            if (!string.IsNullOrEmpty(RunOnSaveActionName))
            {
                // Off the window's thread: the action may take a while or call back into this window.
                string action = RunOnSaveActionName;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        if (!_cph.RunAction(action)) LogWarn("Action '" + action + "' could not be run after saving.");
                    }
                    catch (Exception ex) { LogWarn("Running '" + action + "' failed: " + ex.Message); }
                });
            }
            return true;
        }

        private void ApplyToField(Field f, string key, object value)
        {
            var v = f.Normalize(key, Coerce(f, key, value));
            lock (_lock) _pending[key] = v;
        }

        internal void ImportValue(Field f, string key, object value) { ApplyToField(f, key, value); }

        // Each returns the keys it changed.
        internal IEnumerable<string> ResetField(Field f)
        {
            foreach (var k in f.Keys) ApplyToField(f, k, f.DefaultFor(k));
            return f.Keys.ToList();
        }

        internal IEnumerable<string> RevertToSaved(List<Field> changed)
        {
            var keys = new List<string>();
            foreach (var f in _fields.Where(x => x.Built && x.Keys.Any()))
            {
                foreach (var k in f.Keys)
                {
                    object raw;
                    lock (_lock) _saved.TryGetValue(k, out raw);
                    ApplyToField(f, k, raw ?? f.DefaultFor(k));
                    keys.Add(k);
                }
                changed.Add(f);
            }
            return keys;
        }

        internal string UiStateKey { get { return StorageKey + ".window"; } }

        internal UiState LoadUiState()
        {
            try { return UiState.From(Json.ParseObject(_cph.GetGlobalString(UiStateKey))); }
            catch { return null; }
        }

        internal void SaveUiState(UiState state)
        {
            if (state == null) return;
            _cph.SetGlobal(UiStateKey, Json.Serialize(state.ToDictionary(), false));
        }

        // Dialogs (work with or without the window open, from any thread)

        public void ShowMessage(string title, string message) { ShowChoice(title, message, Strings.Ok); }

        public void AddPopupWindow(string title, string message) { ShowMessage(title, message); }

        public bool ShowConfirm(string title, string message, string yes = null, string no = null)
        {
            return ShowChoice(title, message, no ?? Strings.No, yes ?? Strings.Yes) == 1;
        }

        // The last button is the highlighted one. Returns the clicked index, or -1 if dismissed or timed out.
        public int ShowChoice(string title, string message, params string[] buttons)
        {
            if (buttons == null || buttons.Length == 0) buttons = new[] { Strings.Ok };
            int primary = buttons.Length - 1;
            var c = _controller;
            if (c != null && c.IsOpen) return c.Dialog(title, message, buttons, primary, DialogTimeout);
            return WindowController.StandaloneDialog(this, title, message, buttons, primary, DialogTimeout);
        }

        // Logging

        public void Log(string message) { _cph.Log("LogInfo", "[" + ExtensionName + "] " + message); }
        internal void LogWarn(string message) { _cph.Log("LogWarn", "[" + ExtensionName + "] " + message); }
        internal void LogError(string message) { _cph.Log("LogError", "[" + ExtensionName + "] " + message); }
        internal void LogDebug(string message) { if (DebugLogging) _cph.Log("LogInfo", "[" + ExtensionName + "][debug] " + message); }

        // Password values are masked; everything else is written as stored JSON.
        public void LogSettings()
        {
            KeyValuePair<string, object>[] items;
            lock (_lock) items = _pending.OrderBy(k => k.Key, StringComparer.Ordinal).ToArray();
            Log("---- " + items.Length + " settings ----");
            foreach (var kv in items) Log(kv.Key + " = " + (IsSecret(kv.Key) ? Mask(kv.Value) : Json.Serialize(kv.Value, false)));
        }

        public void LogExistingSettings() { LogSettings(); }

        internal bool IsSecret(string key)
        {
            var t = FieldForKey(key) as TextField;
            return t != null && t.IsPassword;
        }

        internal static string Mask(object value)
        {
            var s = value as string;
            return string.IsNullOrEmpty(s) ? "\"\"" : "\"••••••\" (masked)";
        }

        // Platform details for bug reports.
        public void LogEnvironment()
        {
            foreach (var line in Diagnostics().Split('\n')) Log(line);
        }

        internal string Diagnostics()
        {
            var sb = new StringBuilder();
            sb.Append("Extension: ").Append(ExtensionName).Append(' ').Append(Version ?? "").Append('\n');
            sb.Append("OrchidUI: ").Append(FrameworkVersion).Append(" (native host)").Append('\n');
            sb.Append("Platform: ").Append(OrchidPlatform.Describe()).Append('\n');
            sb.Append("Windows build reported: ").Append(OrchidPlatform.WindowsBuild).Append('\n');
            sb.Append(".NET CLR: ").Append(Environment.Version).Append(Environment.Is64BitProcess ? " (64-bit)" : " (32-bit)").Append('\n');
            sb.Append("Host: ").Append(HostLauncher.FindHost()).Append('\n');
            var c = _controller;
            if (c != null) sb.Append("Link: ").Append(c.LinkDescription).Append('\n');
            foreach (var v in new[] { "ORCHIDUI_COMPAT", "ORCHIDUI_CHROME", "ORCHIDUI_RENDER", "ORCHIDUI_BACKDROP", "ORCHIDUI_SCALE", "ORCHIDUI_ANIMATIONS", "ORCHIDUI_THEME", "ORCHIDUI_HOST" })
            {
                var val = OrchidPlatform.Env(v);
                if (val != null) sb.Append(v).Append('=').Append(val).Append('\n');
            }
            sb.Append("Streamer.bot folder: ").Append(StreamerBotDirectory).Append('\n');
            sb.Append("Storage key: ").Append(StorageKey);
            return sb.ToString();
        }
    }
}
