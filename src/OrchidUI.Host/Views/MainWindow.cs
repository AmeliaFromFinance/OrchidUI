using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OrchidUI.Host.Fields;
using OrchidUI.Host.Theming;
using OrchidUI.Protocol;

namespace OrchidUI.Host.Views;

// The settings window, built in code from a WindowSpec. It holds no settings state of its own:
// what it shows comes from the shim, and what the user does goes back as messages.
internal sealed class MainWindow : Window
{
    private const double PaneCompactWidth = 48;

    private readonly WindowSpec _spec;
    private readonly WindowContext _ctx;
    private readonly Palette P;
    private readonly string _navigation;
    private readonly List<FieldView> _views = new();
    private readonly Dictionary<string, FieldView> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ScrollViewer> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StackPanel> _pageStacks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (ToggleButton Button, Border Badge, TextBlock BadgeText)> _nav = new(StringComparer.Ordinal);
    private readonly List<Control> _paneLabels = new();
    private readonly List<DialogEntry> _dialogs = new();
    private readonly DispatcherTimer _toastTimer;

    private LayoutTransformControl _zoomHost = null!;
    private Panel _overlay = null!;
    private Panel _dialogHost = null!;
    private Border? _pane;
    private TextBox? _search;
    private Control? _searchHost;
    private Button? _searchButton;
    private Border _toast = null!;
    private Panel _toastIcon = null!;
    private TextBlock _toastText = null!;
    private TextBlock _status = null!;
    private Ellipse _statusDot = null!;
    private Control _statusIcon = null!;
    private Button? _discard, _save, _saveClose;
    private Control _noResults = null!;
    private TextBlock _noResultsText = null!;
    private string? _current;
    private double _zoom;
    private bool _paneOpen = true, _userToggledPane, _allowClose, _closeAsked;
    private PixelPoint _normalPosition;
    private Size _normalSize;

    private sealed class DialogEntry
    {
        public required int Request;
        public required Control Card;
        public required Button? Primary;
    }

    public MainWindow(WindowSpec spec, ILinkSink sink)
    {
        _spec = spec;
        P = new Palette(spec.Theme, Json.Str(spec.Layout, "density", "normal"));
        _ctx = new WindowContext(spec, P, sink)
        {
            Toast = (m, k) => ShowToast(m, k),
            CopyToClipboard = async text =>
            {
                if (Clipboard is { } c) await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(c, text);
            },
            CurrentState = CurrentState,
        };
        _navigation = Json.Str(spec.Layout, "navigation", "none");
        _zoom = spec.Zoom > 0 ? spec.Zoom : 1;
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, _ctx.Layout("toastSeconds", 3.2))) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); _toast.IsVisible = false; };

        P.ApplyTo(Resources, this);
        Theming.OrchidStyles.AccentResources(Resources, P);
        Theming.OrchidStyles.CheckedResources(Resources, P, "SubtleHover", "Text");
        Styles.Add(Theming.OrchidStyles.Build(P));
        LoadFontFile();
        Title = spec.Title;
        FontFamily = P.Font;
        FontSize = P.FontSize;
        Foreground = P["Text"];
        Background = P["WindowBg"];
        ApplyWindowOptions();
        Content = BuildRoot();
        SetZoom(_zoom, false);
        BuildPages();
        foreach (var (key, value) in spec.Values) SetValue(key, value);
        foreach (var s in spec.States) if (_byId.TryGetValue(s.Id, out var v)) v.ApplyState(s);
        ApplyStatus(spec.Status);

        var start = spec.StartTab ?? spec.Tabs.FirstOrDefault()?.Id;
        if (start is not null) SelectTab(start);

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Closing += OnClosing;
        PositionChanged += (_, _) => { if (WindowState == WindowState.Normal) _normalPosition = Position; };
        SizeChanged += (_, _) =>
        {
            if (WindowState == WindowState.Normal) _normalSize = new Size(Width, Height);
            AutoPane();
        };
        ScalingChanged += (_, _) => HostLog.Info("window scale now " + RenderScaling.ToString("0.##", CultureInfo.InvariantCulture) + "x; size stays " + Width.ToString("0", CultureInfo.InvariantCulture) + "x" + Height.ToString("0", CultureInfo.InvariantCulture) + " DIP");
        Opened += (_, _) =>
        {
            _normalPosition = Position;
            _normalSize = new Size(Width, Height);
            AutoPane();
            HostLog.Info("window " + Width.ToString("0", CultureInfo.InvariantCulture) + "x" + Height.ToString("0", CultureInfo.InvariantCulture) + " DIP @" + RenderScaling.ToString("0.##", CultureInfo.InvariantCulture) + "x");
        };
    }

    internal IReadOnlyList<FieldView> Views => _views;
    internal string? CurrentTab => _current;
    internal double Zoom => _zoom;
    internal bool AllowClose => _allowClose;
    internal TextBox? SearchBox => _search;
    internal string StatusText => _status.Text ?? "";
    internal bool ToastVisible => _toast.IsVisible;
    internal string ToastText => _toastText.Text ?? "";
    internal int OpenDialogs => _dialogs.Count;
    internal ScrollViewer Page(string tabId) => _pages[tabId];

    // Window setup

    private void ApplyWindowOptions()
    {
        var w = _spec.Window;
        double minW = Json.Num(w, "minWidth", 460), minH = Json.Num(w, "minHeight", 420);
        double maxW = Json.Num(w, "maxWidth"), maxH = Json.Num(w, "maxHeight");
        MinWidth = minW;
        MinHeight = minH;
        if (maxW > 0) MaxWidth = Math.Max(minW, maxW);
        if (maxH > 0) MaxHeight = Math.Max(minH, maxH);
        Width = Json.Num(w, "width", 1180);
        Height = Json.Num(w, "height", 800);
        CanResize = Json.Bool(w, "resizable", true);
        CanMinimize = Json.Bool(w, "canMinimize", true);
        CanMaximize = Json.Bool(w, "canMaximize", true);
        Topmost = Json.Bool(w, "topmost");
        ShowInTaskbar = Json.Bool(w, "showInTaskbar", true);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (w.ContainsKey("x") && w.ContainsKey("y")) PlaceAt(Json.Num(w, "x"), Json.Num(w, "y"), Json.Num(w, "screenScale"));
        if (Json.Bool(w, "maximized")) WindowState = WindowState.Maximized;

        if (OperatingSystem.IsWindows())
        {
            var backdrop = Json.Str(w, "backdrop", "mica");
            if (backdrop != "solid")
            {
                TransparencyLevelHint = backdrop == "acrylic"
                    ? new[] { WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None }
                    : new[] { WindowTransparencyLevel.Mica, WindowTransparencyLevel.None };
                Background = P["MicaTint"];
            }
            if (Json.Str(w, "chrome", "native") == "custom") ExtendClientAreaToDecorationsHint = true;
        }

        try { Icon = MakeIcon(Json.Obj(w, "icon")); }
        catch (Exception ex) { HostLog.Warn("window icon: " + ex.Message); }
    }

    // Bounds are stored in device-independent pixels; a screen at 150 % turns them into physical ones.
    private void PlaceAt(double x, double y, double screenScale)
    {
        var screens = Screens?.All;
        if (screens is null) return;
        var p = ChoosePosition(screens.Select(s => (s.Bounds, s.Scaling)).ToList(), x, y, Width, Height, screenScale);
        if (p is null) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = p.Value;
    }

    // The physical position for a saved DIP position, or null to centre. With mixed DPI the DIP
    // rectangles of different screens can overlap, so the screen with the saved scale is tried first.
    internal static PixelPoint? ChoosePosition(IReadOnlyList<(PixelRect Bounds, double Scaling)> screens, double x, double y, double w, double h, double savedScale)
    {
        var ordered = screens.OrderBy(s => savedScale > 0 && Math.Abs(s.Scaling - savedScale) < 0.01 ? 0 : 1).ToList();
        foreach (var (bounds, scaling) in ordered)
        {
            var dipBounds = new Rect(bounds.X / scaling, bounds.Y / scaling, bounds.Width / scaling, bounds.Height / scaling);
            var r = new Rect(x, y, w, h).Intersect(dipBounds);
            if (r.Width >= 120 && r.Height >= 60)
                return new PixelPoint((int)Math.Round(x * scaling), (int)Math.Round(y * scaling));
        }
        return null;
    }

    private WindowIcon? MakeIcon(Dictionary<string, object>? icon)
    {
        var data = ImageData.FromJson(icon);
        if (data?.Base64 is not null) return new WindowIcon(new MemoryStream(Convert.FromBase64String(data.Base64)));
        // The default badge: the extension's initial on the theme's gradient.
        var badge = Badge(64, 14);
        badge.Measure(new Size(64, 64));
        badge.Arrange(new Rect(0, 0, 64, 64));
        var bmp = new RenderTargetBitmap(new PixelSize(64, 64));
        bmp.Render(badge);
        return new WindowIcon(bmp);
    }

    private Control Badge(double size, double radius)
    {
        var name = _spec.Extension ?? "";
        string initial = name.Length > 0 ? name[..1].ToUpperInvariant() : "O";
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(radius),
            Background = P["AppIcon"],
            Child = new TextBlock
            {
                Text = initial,
                Foreground = P["OnAccent"],
                FontFamily = P.HeadingFont,
                FontWeight = FontWeight.Bold,
                FontSize = Math.Round(size * 0.56),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private void LoadFontFile()
    {
        if (string.IsNullOrEmpty(_spec.Theme.FontFileData)) return;
        try
        {
            var family = UserFonts.Add(Convert.FromBase64String(_spec.Theme.FontFileData), _spec.Theme.FontFileName ?? "font");
            if (family is null) return;
            P.Font = new FontFamily(family + ", " + P.Font);
            if (string.IsNullOrWhiteSpace(_spec.Theme.HeadingFontFamily) || _spec.Theme.HeadingFontFamily.StartsWith("Segoe UI Variable Display", StringComparison.Ordinal))
                P.HeadingFont = P.Font;
        }
        catch (Exception ex) { HostLog.Warn("font file: " + ex.Message); }
    }

    // Layout

    private Control BuildRoot()
    {
        var root = new Grid();
        root.Children.Add(BuildBody());
        _overlay = new Panel { IsVisible = false };
        _overlay.Children.Add(new Border { Background = P["Overlay"] });
        _dialogHost = new Panel();
        _overlay.Children.Add(_dialogHost);
        root.Children.Add(_overlay);
        _zoomHost = new LayoutTransformControl { Child = root };
        return _zoomHost;
    }

    private Control BuildBody()
    {
        var layer = BuildLayer();
        if (_navigation == "sidebar")
        {
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            bool compact = Json.Str(_spec.Layout, "pane", "auto") == "compact";
            _pane = new Border { Width = compact ? PaneCompactWidth : _ctx.Layout("paneWidth", 280), ClipToBounds = true, Background = P["Pane"], Child = BuildPane() };
            if (compact) { _paneOpen = false; ApplyPaneLabels(false); }
            body.Children.Add(_pane);
            Grid.SetColumn(layer, 1);
            body.Children.Add(layer);
            return body;
        }
        if (_navigation == "tabs")
        {
            var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            body.Children.Add(BuildTabStrip());
            Grid.SetRow(layer, 1);
            body.Children.Add(layer);
            return body;
        }
        return layer;
    }

    private Control BuildPane()
    {
        var dock = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6) };
        var top = new StackPanel();
        if (Json.Str(_spec.Layout, "pane", "auto") != "expanded")
        {
            var burger = NavButton(new IconSpec { Path = "M4,6.5 H20 M4,12 H20 M4,17.5 H20" }, _ctx.S("CollapseNav", "Collapse / expand navigation"), () =>
            {
                _userToggledPane = true;
                TogglePane(!_paneOpen);
            }, false);
            burger.Width = 40;
            burger.HorizontalAlignment = HorizontalAlignment.Left;
            top.Children.Add(burger);
        }
        else top.Children.Add(new Border { Height = 8 });
        if (_ctx.LayoutFlag("showSearch", true))
        {
            _searchHost = BuildSearchBox(new Thickness(8, 4, 8, 8));
            top.Children.Add(_searchHost);
            _searchButton = NavButton(SearchIcon, _ctx.S("Search", "Search settings"), () =>
            {
                _userToggledPane = true;
                TogglePane(true);
                _search?.Focus();
            }, true);
            _searchButton.IsVisible = false;
            top.Children.Add(_searchButton);
        }
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);

        // With the OS title bar, links live at the bottom of the sidebar.
        var footer = new StackPanel();
        if (_spec.Links.Count > 0)
        {
            footer.Children.Add(new Border { Height = 1, Background = P["Divider"], Margin = new Thickness(12, 4, 12, 4) });
            foreach (var link in _spec.Links)
            {
                var url = link.Value;
                footer.Children.Add(NavButton(ExternalIcon, link.Key, () => Ui.OpenUrl(url), true));
            }
        }
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);

        var navStack = new StackPanel();
        foreach (var tab in _spec.Tabs)
        {
            var item = NavItem(tab, true);
            navStack.Children.Add(item.Button);
        }
        dock.Children.Add(new ScrollViewer { Content = navStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        return dock;
    }

    private static readonly IconSpec SearchIcon = new() { Path = "M18,11 A7,7 0 1 1 4,11 A7,7 0 1 1 18,11 Z M16.2,16.2 L20.5,20.5" };
    private static readonly IconSpec ExternalIcon = new() { Path = "M14,4 H20 V10 M20,4 L11,13 M18,14 V19 A1,1 0 0 1 17,20 H5 A1,1 0 0 1 4,19 V7 A1,1 0 0 1 5,6 H10" };
    private static readonly IconSpec CheckIcon = new() { Path = "M5,12.5 L10,17.5 L19,7" };

    private Control BuildSearchBox(Thickness margin)
    {
        var grid = new Panel { Margin = margin };
        _search = new TextBox { PlaceholderText = _ctx.S("Search", "Search settings"), Padding = new Thickness(10, 5, 34, 6), FontSize = P.FontSize };
        ToolTip.SetTip(_search, _ctx.S("SearchTooltip", "Search all settings (Ctrl+F)"));
        Ui.Named(_search, _ctx.S("Search", "Search settings"));
        _search.TextChanged += (_, _) => ApplyFilter();
        grid.Children.Add(_search);
        var glyph = Ui.Icon(SearchIcon, 14, P["TextSecondary"])!;
        glyph.HorizontalAlignment = HorizontalAlignment.Right;
        glyph.Margin = new Thickness(0, 0, 11, 0);
        grid.Children.Add(glyph);
        return grid;
    }

    private Control BuildTabStrip()
    {
        var strip = new Border
        {
            BorderBrush = P["Divider"],
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(Math.Max(8, _ctx.Layout("contentPadding", 36) - 12), 0, 12, 0),
            Background = P["Pane"],
        };
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var tab in _spec.Tabs) tabs.Children.Add(NavItem(tab, false).Button);
        g.Children.Add(new ScrollViewer { Content = tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        if (_ctx.LayoutFlag("showSearch", true))
        {
            _searchHost = BuildSearchBox(new Thickness(12, 6, 0, 6));
            _searchHost.Width = 220;
            _searchHost.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_searchHost, 1);
            g.Children.Add(_searchHost);
        }
        strip.Child = g;
        return strip;
    }

    private Button NavButton(IconSpec icon, string text, Action onClick, bool label)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*") };
        var ic = Ui.Icon(icon, 16);
        if (ic is not null) { ic.HorizontalAlignment = HorizontalAlignment.Left; g.Children.Add(ic); }
        if (label)
        {
            var tb = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(tb, 1);
            g.Children.Add(tb);
            _paneLabels.Add(tb);
        }
        var b = new Button
        {
            Content = g,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(4, 1, 4, 1),
            Padding = new Thickness(12, 8, 12, 8),
            Foreground = P["Text"],
        };
        Ui.Named(b, text);
        b.Click += (_, _) => onClick();
        return b;
    }

    private (ToggleButton Button, Border Badge, TextBlock BadgeText) NavItem(TabSpec tab, bool sidebar)
    {
        var badgeText = new TextBlock { FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = P["OnAccent"], HorizontalAlignment = HorizontalAlignment.Center };
        var badge = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = P["Accent"],
            MinWidth = 16,
            Padding = new Thickness(5, 0, 5, 1),
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            Child = badgeText,
        };
        Control content;
        if (sidebar)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*,Auto") };
            var ic = Ui.Icon(tab.Icon, 16) ?? new TextBlock { Text = tab.Name.Length > 0 ? tab.Name[..1].ToUpperInvariant() : "?", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            ic.HorizontalAlignment = HorizontalAlignment.Left;
            g.Children.Add(ic);
            var label = new TextBlock { Text = tab.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1);
            g.Children.Add(label);
            _paneLabels.Add(label);
            Grid.SetColumn(badge, 2);
            g.Children.Add(badge);
            content = g;
        }
        else
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = Ui.Icon(tab.Icon, 16);
            if (ic is not null) { ic.Margin = new Thickness(0, 0, 8, 0); sp.Children.Add(ic); }
            sp.Children.Add(new TextBlock { Text = tab.Name, VerticalAlignment = VerticalAlignment.Center });
            badge.Margin = new Thickness(8, 0, 0, 0);
            sp.Children.Add(badge);
            content = sp;
        }
        var rb = new ToggleButton
        {
            Content = content,
            Tag = tab.Id,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = sidebar ? new Thickness(4, 1, 4, 1) : new Thickness(0, 0, 4, 0),
            Padding = sidebar ? new Thickness(12, 8, 12, 8) : new Thickness(12, 10, 12, 10),
        };
        rb.Classes.Add(sidebar ? "nav" : "tab");
        Ui.Named(rb, tab.Name);
        // Acts like a radio button: clicking the selected item keeps it selected.
        rb.IsCheckedChanged += (_, _) =>
        {
            if (rb.IsChecked == true) SelectTab(tab.Id);
            else if (_current == tab.Id) rb.IsChecked = true;
        };
        var item = (rb, badge, badgeText);
        _nav[tab.Id] = item;
        return item;
    }

    private void TogglePane(bool open)
    {
        if (_pane is null) return;
        _paneOpen = open;
        _pane.Width = open ? _ctx.Layout("paneWidth", 280) : PaneCompactWidth;
        ApplyPaneLabels(open);
    }

    private void ApplyPaneLabels(bool open)
    {
        if (_searchHost is not null && _navigation == "sidebar") _searchHost.IsVisible = open;
        if (_searchButton is not null) _searchButton.IsVisible = !open;
        foreach (var l in _paneLabels) l.IsVisible = open;
    }

    private void AutoPane()
    {
        if (_pane is null || Json.Str(_spec.Layout, "pane", "auto") != "auto" || _userToggledPane || Bounds.Width <= 0) return;
        bool want = Bounds.Width / Math.Max(0.5, _zoom) >= _ctx.Layout("compactPaneBelow", 1000);
        if (want != _paneOpen) TogglePane(want);
    }

    private Control BuildLayer()
    {
        var layer = new Border { Background = P["Surface"], BorderBrush = P["CardBorder"] };
        if (_navigation == "sidebar")
        {
            layer.BorderThickness = new Thickness(1, 1, 0, 0);
            layer.CornerRadius = new CornerRadius(P.LargeRadius, 0, 0, 0);
        }
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        var host = new Panel();
        double pad = Math.Max(0, _ctx.Layout("contentPadding", 36));
        double maxWidth = _ctx.Layout("contentMaxWidth", 1000);
        foreach (var tab in _spec.Tabs)
        {
            var stack = new StackPanel { Margin = new Thickness(pad, 28, pad, 40), MaxWidth = maxWidth > 0 ? maxWidth + pad * 2 : double.PositiveInfinity, HorizontalAlignment = HorizontalAlignment.Left };
            var sv = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, IsVisible = false };
            stack.Width = double.NaN;
            stack.HorizontalAlignment = HorizontalAlignment.Stretch;
            _pages[tab.Id] = sv;
            _pageStacks[tab.Id] = stack;
            host.Children.Add(sv);
        }
        var nr = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        nr.Children.Add(Ui.Icon(SearchIcon, 32, P["TextMuted"])!);
        _noResultsText = new TextBlock { Foreground = P["TextSecondary"], Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        nr.Children.Add(_noResultsText);
        _noResults = nr;
        host.Children.Add(nr);
        grid.Children.Add(host);

        var bar = BuildCommandBar();
        Grid.SetRow(bar, 1);
        grid.Children.Add(bar);

        var toast = BuildToast();
        Grid.SetRowSpan(toast, 2);
        grid.Children.Add(toast);
        layer.Child = grid;
        return layer;
    }

    private Control BuildCommandBar()
    {
        var bar = new Border
        {
            BorderBrush = P["Divider"],
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(Math.Max(12, _ctx.Layout("contentPadding", 36)), 12, 16, 12),
        };
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        // A dock, not a horizontal stack: the footer text gets what's left of the width and is cut
        // with "…" there, instead of running under the buttons.
        var left = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        if (_navigation != "sidebar" && _spec.Links.Count > 0)
        {
            foreach (var link in _spec.Links)
            {
                var url = link.Value;
                var b = new Button { Content = link.Key, FontSize = P.SmallFontSize, Padding = new Thickness(10, 4, 10, 5), Margin = new Thickness(0, 0, 8, 0), Background = Brushes.Transparent };
                ToolTip.SetTip(b, url);
                Ui.Named(b, link.Key);
                b.Click += (_, _) => Ui.OpenUrl(url);
                left.Children.Add(b);
            }
        }
        _statusDot = new Ellipse { Width = 8, Height = 8, Fill = P["Accent"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 1, 4, 0) };
        _statusIcon = Ui.Icon(CheckIcon, 14, P["TextMuted"])!;
        _status = new TextBlock { Margin = new Thickness(8, 0, 0, 1), VerticalAlignment = VerticalAlignment.Center, FontSize = Math.Round(P.FontSize * 0.93) };
        Ui.Named(_status, "Status");
        if (_ctx.LayoutFlag("showStatus", true))
        {
            left.Children.Add(_statusDot);
            left.Children.Add(_statusIcon);
            left.Children.Add(_status);
        }
        if (!string.IsNullOrEmpty(_spec.Footer))
        {
            var ft = Ui.RichText(_spec.Footer, P, P["TextMuted"], P.SmallFontSize);
            ft.TextWrapping = TextWrapping.NoWrap;
            ft.TextTrimming = TextTrimming.CharacterEllipsis;
            ft.VerticalAlignment = VerticalAlignment.Center;
            ft.Margin = new Thickness(_ctx.LayoutFlag("showStatus", true) ? 20 : 0, 0, 12, 1);
            left.Children.Add(ft);
            FooterText = ft;
        }
        else left.Children.Add(new Panel());
        foreach (var c in left.Children.Take(left.Children.Count - 1)) DockPanel.SetDock(c, Dock.Left);
        g.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        var more = BuildMoreButton();
        if (more is not null) right.Children.Add(more);
        if (_ctx.AutoSave)
        {
            var done = new Button { Content = _ctx.S("Done", "Done"), MinWidth = 120, HorizontalContentAlignment = HorizontalAlignment.Center };
            done.Classes.Add("accent");
            done.Click += (_, _) => _ctx.SendCommand(Commands.Done);
            right.Children.Add(done);
        }
        else
        {
            _discard = new Button { Content = _ctx.S("Discard", "Discard"), MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
            _discard.Click += (_, _) => _ctx.SendCommand(Commands.Discard);
            _save = new Button { Content = _ctx.S("Save", "Save"), MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
            ToolTip.SetTip(_save, CommandKeyName + "+S");
            _save.Click += (_, _) => _ctx.SendCommand(Commands.Save);
            _saveClose = new Button { Content = _ctx.S("SaveAndClose", "Save & close"), MinWidth = 120, HorizontalContentAlignment = HorizontalAlignment.Center };
            _saveClose.Classes.Add("accent");
            _saveClose.Click += (_, _) => _ctx.SendCommand(Commands.SaveAndClose);
            if (_ctx.LayoutFlag("showDiscard", true)) right.Children.Add(_discard);
            if (_ctx.LayoutFlag("showSave", true)) right.Children.Add(_save);
            if (_ctx.LayoutFlag("showSaveAndClose", true)) right.Children.Add(_saveClose);
            else if (right.Children.Count > 0) ((Control)right.Children[^1]).Margin = new Thickness(0);
        }
        Grid.SetColumn(right, 1);
        g.Children.Add(right);
        bar.Child = g;
        return bar;
    }

    internal TextBlock? FooterText { get; private set; }

    // The More menu. Import and Export pick the file here; the shim reads or writes it.
    private const string MoreIcon = "M5,12 H5.01 M12,12 H12.01 M19,12 H19.01";

    internal Button? MoreButton { get; private set; }

    private Button? BuildMoreButton()
    {
        var items = new List<MenuItem>();
        MenuItem Item(string key, string fallback, Action click)
        {
            var mi = new MenuItem { Header = _ctx.S(key, fallback) };
            mi.Click += (_, _) => click();
            items.Add(mi);
            return mi;
        }
        if (_ctx.LayoutFlag("showImportExport", false))
        {
            Item("Import", "Import settings", () => _ = ImportAsync());
            Item("Export", "Export settings", () => _ = ExportAsync());
        }
        if (_ctx.LayoutFlag("showResetAll", false)) Item("ResetAll", "Reset to defaults", () => _ctx.SendCommand(Commands.ResetAll));
        if (_ctx.LayoutFlag("showAbout", false)) Item("About", "About", () => _ctx.SendCommand(Commands.About));
        if (items.Count == 0) return null;
        var b = CopyView.IconButton(_ctx, MoreIcon, _ctx.S("More", "More"));
        b.Margin = new Thickness(0, 0, 8, 0);
        if (Ui.Icon(new IconSpec { Path = MoreIcon, Stroke = 3 }, 18) is { } icon) b.Content = icon;
        b.Flyout = new MenuFlyout { ItemsSource = items, Placement = PlacementMode.TopEdgeAlignedRight };
        MoreButton = b;
        return b;
    }

    // For tests: the file a dialog would return.
    internal static Func<string, Task<string?>>? FileDialogOverride;

    private async Task ImportAsync()
    {
        var path = FileDialogOverride is not null ? await FileDialogOverride("import") : await PickJsonAsync(save: false);
        if (path is not null) SendFileCommand(Commands.Import, path);
    }

    private async Task ExportAsync()
    {
        var path = FileDialogOverride is not null ? await FileDialogOverride("export") : await PickJsonAsync(save: true);
        if (path is not null) SendFileCommand(Commands.Export, path);
    }

    private void SendFileCommand(string name, string path)
    {
        var payload = new Dictionary<string, object>(StringComparer.Ordinal) { { "name", name }, { "path", path }, { "native", true } };
        _ctx.Sink.Send(MessageTypes.Command, payload);
    }

    private async Task<string?> PickJsonAsync(bool save)
    {
        try
        {
            var sp = StorageProvider;
            var filters = Fields.PathView.Filters(_ctx.S("JsonFiles", "JSON (*.json)|*.json") + "|" + _ctx.S("AllFiles", "All files (*.*)|*.*"));
            if (save)
            {
                if (!sp.CanSave) throw new NotSupportedException("no save dialog");
                var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = _ctx.S("Export", "Export settings"),
                    SuggestedFileName = SafeFileName(_spec.Extension) + " settings.json",
                    DefaultExtension = "json",
                    FileTypeChoices = filters,
                    ShowOverwritePrompt = true,
                });
                return file?.TryGetLocalPath();
            }
            if (!sp.CanOpen) throw new NotSupportedException("no open dialog");
            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions { Title = _ctx.S("Import", "Import settings"), AllowMultiple = false, FileTypeFilter = filters });
            return files.FirstOrDefault()?.TryGetLocalPath();
        }
        catch (Exception ex)
        {
            HostLog.Warn("file dialog: " + ex.Message);
            ShowToast(_ctx.S("NoFileDialog", "The file dialog couldn't open here. Type the path instead."), "warning");
            return null;
        }
    }

    private static string SafeFileName(string? name)
    {
        var s = string.IsNullOrWhiteSpace(name) ? "OrchidUI" : name.Trim();
        foreach (var c in System.IO.Path.GetInvalidFileNameChars().Concat(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' })) s = s.Replace(c, '_');
        return s;
    }

    // From the shim (About's Copy diagnostics).
    internal Task CopyText(string text) => _ctx.Copy(text);

    private Control BuildToast()
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        _toastIcon = new Panel { Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(_toastIcon);
        _toastText = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Foreground = P["Text"] };
        Grid.SetColumn(_toastText, 1);
        g.Children.Add(_toastText);
        var close = CopyView.IconButton(_ctx, "M6,6 L18,18 M18,6 L6,18", _ctx.S("Close", "Close"));
        close.Margin = new Thickness(12, -6, -8, -6);
        close.Click += (_, _) => { _toastTimer.Stop(); _toast.IsVisible = false; };
        Grid.SetColumn(close, 2);
        g.Children.Add(close);
        _toast = new Border
        {
            Background = P["Popup"],
            BorderBrush = P["PopupBorder"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(P.LargeRadius),
            Padding = new Thickness(16, 12, 16, 12),
            MinWidth = 300,
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(24, 0, 24, 76),
            BoxShadow = P.Theme.Shadows ? BoxShadows.Parse("0 6 24 0 #73000000") : default,
            IsVisible = false,
            Child = g,
        };
        Avalonia.Automation.AutomationProperties.SetLiveSetting(_toastText, Avalonia.Automation.AutomationLiveSetting.Polite);
        return _toast;
    }

    private Control? BuildBanner()
    {
        var image = _spec.HeaderImage;
        if (image is not null)
        {
            var border = new Border
            {
                Height = _spec.HeaderHeight,
                Margin = new Thickness(0, 0, 0, 28),
                CornerRadius = new CornerRadius(P.LargeRadius),
                Background = P["Card"],
                BorderBrush = P["CardBorder"],
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
            };
            _ = Images.LoadAsync(image, bmp => border.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill }, () => border.IsVisible = false, 2400, "Header image");
            return border;
        }
        if (string.IsNullOrEmpty(_spec.HeaderTitle)) return null;
        double r = P.LargeRadius;
        var hero = new Panel { MinHeight = 168, ClipToBounds = true };
        hero.Children.Add(new Border { Background = P["Hero"], CornerRadius = new CornerRadius(r) });
        hero.Children.Add(new Ellipse { Width = 300, Height = 300, Fill = P["HeroOrb2"], HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -120, -60, 0) });
        hero.Children.Add(new Ellipse { Width = 190, Height = 190, Fill = P["HeroOrb1"], HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 150, -110) });
        hero.Children.Add(new Border { CornerRadius = new CornerRadius(r), BorderBrush = P["HeroBorder"], BorderThickness = new Thickness(1) });
        var text = new StackPanel { Margin = new Thickness(32, 28, 32, 28), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = _spec.HeaderTitle, FontFamily = P.HeadingFont, FontSize = P.Theme.HeroTitleSize, FontWeight = P.HeadingWeight, Foreground = P["HeroText"], TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(_spec.HeaderSubtitle))
        {
            var sub = Ui.RichText(_spec.HeaderSubtitle, P, P["HeroTextMuted"], P.FontSize, P["HeroText"], P["HeroBorder"]);
            sub.Margin = new Thickness(0, 8, 0, 0);
            sub.MaxWidth = 560;
            sub.HorizontalAlignment = HorizontalAlignment.Left;
            text.Children.Add(sub);
        }
        hero.Children.Add(text);
        return new Border { CornerRadius = new CornerRadius(r), ClipToBounds = true, Margin = new Thickness(0, 0, 0, 28), Child = hero };
    }

    // Pages and cards

    private void BuildPages()
    {
        var banner = BuildBanner();
        var firstTab = _spec.Tabs.FirstOrDefault()?.Id;
        bool showTitle = _ctx.LayoutFlag("showPageTitle", true);
        foreach (var tab in _spec.Tabs)
        {
            var stack = _pageStacks[tab.Id];
            if (banner is not null && tab.Id == firstTab) stack.Children.Add(banner);
            if (showTitle)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = tab.Name,
                    FontFamily = P.HeadingFont,
                    FontSize = P.Theme.PageTitleSize,
                    FontWeight = P.HeadingWeight,
                    Foreground = P["Text"],
                    Margin = new Thickness(0, 0, 0, 12),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }
        }
        var started = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spec in _spec.Fields)
        {
            if (!_pageStacks.TryGetValue(spec.Tab, out var stack)) continue;
            var view = FieldView.Create(spec, _ctx);
            Control row;
            try { row = BuildRow(view); }
            catch (Exception ex)
            {
                HostLog.Error("building " + spec.Id + " (" + spec.Kind + ") failed: " + ex);
                row = new TextBlock { Text = "Could not show '" + (spec.Label ?? spec.Key) + "': " + ex.Message, Foreground = P["Danger"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            }
            if (started.Add(spec.Tab) && spec.Kind == FieldKinds.Title)
            {
                var m = row.Margin;
                row.Margin = new Thickness(m.Left, showTitle ? 6 : 0, m.Right, m.Bottom);
            }
            view.Row = row;
            stack.Children.Add(row);
            _views.Add(view);
            _byId[spec.Id] = view;
        }
    }

    private Control BuildRow(FieldView view)
    {
        var control = view.BuildControl();
        if (!view.IsCard) return control ?? new Panel();
        var spec = view.Spec;
        var m = P.Metrics;
        double indent = spec.Icon is null ? 0 : 40;
        var card = new Border
        {
            Background = P["Card"],
            BorderBrush = P["CardBorder"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(P.Radius),
            Margin = new Thickness(0, 0, 0, m.CardGap),
        };
        if (spec.Layout == "inline")
        {
            card.Padding = new Thickness(m.CardPadX, m.CardPadY, m.CardPadX, m.CardPadY);
            card.MinHeight = m.CardMinHeight;
            card.Child = CardHeader(view, control, true);
        }
        else
        {
            card.Padding = new Thickness(m.CardPadX, m.CardPadY, m.CardPadX, m.CardPadY + 2);
            var sp = new StackPanel();
            sp.Children.Add(CardHeader(view, spec.Layout == "expander" ? ExpanderToggle(view, control) : null, false));
            if (control is not null)
            {
                control.Margin = new Thickness(indent, 12, 0, 0);
                sp.Children.Add(control);
            }
            card.Child = sp;
        }
        card.PointerEntered += (_, _) => card.Background = P["CardHover"];
        card.PointerExited += (_, _) => card.Background = P["Card"];
        view.Card = card;

        if (spec.Keys.Count > 0)
        {
            var reset = new MenuItem { Header = _ctx.S("ResetToDefault", "Reset to default") };
            reset.Click += (_, _) => _ctx.SendCommand(Commands.ResetField, spec.Id);
            var copy = new MenuItem { Header = string.Format(CultureInfo.InvariantCulture, _ctx.S("CopyKey", "Copy key ({0})"), spec.Key) };
            copy.Click += async (_, _) => await _ctx.Copy(spec.Key ?? "");
            card.ContextMenu = new ContextMenu { ItemsSource = new[] { reset, copy } };
        }
        return card;
    }

    // Lists, tables and multi-selects: a summary ("3 item(s)") and a button that shows or hides the
    // control under the header; Collapsed() starts it hidden.
    internal const string ChevronDown = "M6,9 L12,15 L18,9";
    internal const string ChevronRight = "M9,6 L15,12 L9,18";

    private Control ExpanderToggle(FieldView view, Control? body)
    {
        var spec = view.Spec;
        var summary = new TextBlock { Foreground = P["TextSecondary"], FontSize = P.SmallFontSize, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        view.SummaryText = summary;
        var toggle = new DisclosureButton
        {
            Padding = new Thickness(7),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Ui.Named(toggle, spec.Label ?? "");
        toggle.Apply = o =>
        {
            if (body is not null) body.IsVisible = o;
            toggle.Content = Ui.Icon(new IconSpec { Path = o ? ChevronDown : ChevronRight }, 16);
            ToolTip.SetTip(toggle, o ? _ctx.S("Collapse", "Hide") : _ctx.S("Expand", "Show"));
        };
        view.Expand = () => toggle.IsExpanded = true;
        view.Disclosure = toggle;
        toggle.Apply(false);
        toggle.IsExpanded = !spec.Collapsed;
        return new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { summary, toggle } };
    }

    // Icon | label + description | control. Inline controls drop below the label when the card gets narrow.
    private Grid CardHeader(FieldView view, Control? right, bool responsive)
    {
        var spec = view.Spec;
        var g = new Grid { MinHeight = 36, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        var ic = Ui.Icon(spec.Icon, 20, P["Text"]);
        if (ic is not null)
        {
            ic.Margin = new Thickness(2, 0, 18, 0);
            ic.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(ic);
        }
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0), MinWidth = 120 };
        var labelRow = new StackPanel { Orientation = Orientation.Horizontal };
        var label = new TextBlock { Text = spec.Label ?? "Custom content", TextWrapping = TextWrapping.Wrap, FontSize = P.FontSize, Foreground = P["Text"] };
        if (!string.IsNullOrEmpty(spec.Tooltip)) ToolTip.SetTip(label, spec.Tooltip);
        labelRow.Children.Add(label);
        if (spec.Keys.Count > 0 && _ctx.LayoutFlag("showDirtyMarkers", true) && !_ctx.AutoSave)
        {
            var dot = new Ellipse { Width = 6, Height = 6, Fill = P["Accent"], Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
            ToolTip.SetTip(dot, _ctx.S("Unsaved", "Unsaved change"));
            labelRow.Children.Add(dot);
            view.DirtyDot = dot;
        }
        texts.Children.Add(labelRow);
        if (!string.IsNullOrEmpty(spec.Description))
        {
            var desc = Ui.RichText(spec.Description, P, P["TextSecondary"], P.SmallFontSize);
            desc.Margin = new Thickness(0, 2, 0, 0);
            texts.Children.Add(desc);
        }
        var error = new TextBlock { Foreground = P["Danger"], FontSize = P.SmallFontSize, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), IsVisible = false };
        Avalonia.Automation.AutomationProperties.SetLiveSetting(error, Avalonia.Automation.AutomationLiveSetting.Assertive);
        texts.Children.Add(error);
        view.ErrorText = error;
        Grid.SetColumn(texts, 1);
        g.Children.Add(texts);

        if (right is not null)
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            right.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(right, 2);
            g.Children.Add(right);
            if (responsive)
            {
                bool stacked = false;
                double natural = 0;
                g.SizeChanged += (_, e) =>
                {
                    if (!stacked) natural = right.DesiredSize.Width;
                    if (natural <= 0) return;
                    double need = natural + 200 + (ic?.Bounds.Width ?? 0);
                    bool want = e.NewSize.Width < need;
                    if (want == stacked) return;
                    stacked = want;
                    Grid.SetRow(right, want ? 1 : 0);
                    Grid.SetColumn(right, want ? 1 : 2);
                    Grid.SetColumnSpan(right, want ? 2 : 1);
                    right.HorizontalAlignment = want ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                    right.Margin = want ? new Thickness(0, 10, 0, 0) : new Thickness(0);
                };
            }
        }
        return g;
    }

    // Tabs, search, zoom

    internal void SelectTab(string tabId)
    {
        if (!_pages.ContainsKey(tabId)) return;
        bool changed = _current != tabId;
        _current = tabId;
        foreach (var (id, page) in _pages) page.IsVisible = id == tabId && !_noResults.IsVisible;
        foreach (var (id, nav) in _nav) nav.Button.IsChecked = id == tabId;
        if (changed)
        {
            _pages[tabId].ScrollToHome();
            var payload = new Dictionary<string, object>(StringComparer.Ordinal) { { "tab", tabId } };
            var state = CurrentState();
            if (state is not null) payload["state"] = state;
            _ctx.Sink.Send(MessageTypes.TabShown, payload);
        }
    }

    private void SelectRelativeTab(int delta)
    {
        if (_spec.Tabs.Count < 2 || _current is null) return;
        int i = _spec.Tabs.FindIndex(t => t.Id == _current);
        int next = ((i + delta) % _spec.Tabs.Count + _spec.Tabs.Count) % _spec.Tabs.Count;
        SelectTab(_spec.Tabs[next].Id);
    }

    private void ApplyFilter()
    {
        if (_search is null) return;
        string q = (_search.Text ?? "").Trim();
        bool searching = q.Length > 0;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var names = _spec.Tabs.ToDictionary(t => t.Id, t => t.Name, StringComparer.Ordinal);
        foreach (var v in _views)
        {
            v.SearchMatch = !searching || (v.IsCard && v.Matches(q, names.GetValueOrDefault(v.Spec.Tab, "")));
            v.UpdateVisibility();
            if (searching && v.SearchMatch && v.StateVisible) counts[v.Spec.Tab] = counts.GetValueOrDefault(v.Spec.Tab) + 1;
        }
        foreach (var tab in _spec.Tabs)
        {
            if (!_nav.TryGetValue(tab.Id, out var item)) continue;
            int c = counts.GetValueOrDefault(tab.Id);
            item.Badge.IsVisible = searching && c > 0;
            item.BadgeText.Text = c.ToString(CultureInfo.InvariantCulture);
            item.Button.Opacity = searching && c == 0 ? 0.45 : 1;
        }
        bool none = searching && counts.Count == 0;
        _noResults.IsVisible = none;
        _noResultsText.Text = string.Format(CultureInfo.InvariantCulture, _ctx.S("NoResults", "No settings match “{0}”"), q);
        if (searching && _current is not null && !counts.ContainsKey(_current))
        {
            var first = _spec.Tabs.FirstOrDefault(t => counts.ContainsKey(t.Id));
            if (first is not null) SelectTab(first.Id);
        }
        if (_current is not null) _pages[_current].IsVisible = !none;
    }

    private void SetZoom(double zoom, bool announce)
    {
        _zoom = Math.Max(0.5, Math.Min(3, Math.Round(zoom, 2)));
        _zoomHost.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        if (announce) ShowToast(string.Format(CultureInfo.InvariantCulture, _ctx.S("Zoom", "Zoom {0}%"), Math.Round(_zoom * 100)), "info");
        AutoPane();
    }

    private static string CommandKeyName => OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl";

    private static bool IsCommand(KeyModifiers m) => OperatingSystem.IsMacOS() ? m.HasFlag(KeyModifiers.Meta) || m.HasFlag(KeyModifiers.Control) : m.HasFlag(KeyModifiers.Control);

    internal void OnKey(object? sender, KeyEventArgs e)
    {
        if (_dialogs.Count > 0)
        {
            if (e.Key == Key.Escape) { FinishDialog(_dialogs[^1].Request, -1); e.Handled = true; }
            return;
        }
        bool cmd = IsCommand(e.KeyModifiers);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool allowZoom = Json.Bool(_spec.Window, "allowZoom", true);
        if (cmd && e.Key == Key.S)
        {
            _ctx.SendCommand(Commands.Save);
            e.Handled = true;
        }
        else if (cmd && e.Key == Key.F && _search is not null)
        {
            if (!_paneOpen) TogglePane(true);
            _search.Focus();
            _search.SelectAll();
            e.Handled = true;
        }
        else if (cmd && e.Key == Key.Tab)
        {
            SelectRelativeTab(shift ? -1 : 1);
            e.Handled = true;
        }
        else if (cmd && e.Key >= Key.D1 && e.Key <= Key.D9)
        {
            int i = e.Key - Key.D1;
            if (i < _spec.Tabs.Count) SelectTab(_spec.Tabs[i].Id);
            e.Handled = true;
        }
        else if (cmd && allowZoom && (e.Key == Key.OemPlus || e.Key == Key.Add))
        {
            SetZoom(_zoom + 0.1, true);
            e.Handled = true;
        }
        else if (cmd && allowZoom && (e.Key == Key.OemMinus || e.Key == Key.Subtract))
        {
            SetZoom(_zoom - 0.1, true);
            e.Handled = true;
        }
        else if (cmd && allowZoom && (e.Key == Key.D0 || e.Key == Key.NumPad0))
        {
            SetZoom(Json.Num(_spec.Window, "scale", 1), true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _search is not null && _search.IsKeyboardFocusWithin && !string.IsNullOrEmpty(_search.Text))
        {
            _search.Text = "";
            e.Handled = true;
        }
    }

    // From the shim

    internal void SetValue(string key, object? value)
    {
        foreach (var v in _views.Where(v => v.Spec.Keys.Contains(key))) v.Push(key, value);
    }

    internal void ApplyPatch(Dictionary<string, object> payload)
    {
        var fields = Json.Arr(payload, "fields");
        if (fields is not null)
        {
            foreach (var f in fields.OfType<Dictionary<string, object>>().Select(FieldSpec.FromJson))
                if (_byId.TryGetValue(f.Id, out var view)) view.Respec(f);
        }
        var values = Json.Obj(payload, "values");
        if (values is not null) foreach (var (k, v) in values) SetValue(k, v);
        var states = Json.Arr(payload, "states");
        if (states is not null)
        {
            foreach (var s in states.OfType<Dictionary<string, object>>().Select(FieldState.FromJson))
                if (_byId.TryGetValue(s.Id, out var view)) view.ApplyState(s);
            if (!string.IsNullOrEmpty(_search?.Text)) ApplyFilter();
        }
        var status = StatusSpec.FromJson(Json.Obj(payload, "status"));
        if (status is not null) ApplyStatus(status);
    }

    private void ApplyStatus(StatusSpec s)
    {
        if (_discard is not null) _discard.IsEnabled = s.CanDiscard;
        if (_save is not null) _save.IsEnabled = s.CanSave;
        if (_saveClose is not null) _saveClose.IsEnabled = s.CanSave;
        bool attention = s.Errors > 0 || (s.Dirty && !_ctx.AutoSave);
        _statusDot.Fill = s.Errors > 0 ? P["Danger"] : P["Accent"];
        _statusDot.IsVisible = attention;
        _statusIcon.IsVisible = !attention;
        _status.Text = s.Text;
        _status.Foreground = s.Tone switch { "danger" => P["Danger"], "normal" => P["Text"], _ => P["TextMuted"] };
    }

    internal void ShowToast(string message, string kind)
    {
        _toastIcon.Children.Clear();
        _toastIcon.Children.Add(Ui.SeverityIcon(kind, P));
        _toastText.Text = message;
        _toast.IsVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    internal void JumpTo(string fieldId)
    {
        if (!_byId.TryGetValue(fieldId, out var view)) return;
        SelectTab(view.Spec.Tab);
        view.Expand?.Invoke();
        Dispatcher.UIThread.Post(() =>
        {
            view.Row.BringIntoView();
            view.Inputs.FirstOrDefault()?.Focus();
        }, DispatcherPriority.Loaded);
    }

    internal void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (!Topmost) { Topmost = true; Topmost = false; }
    }

    // In-window dialogs: no extra top-level window.

    internal void ShowDialog(int request, string title, string message, string[] buttons, int primary)
    {
        var card = DialogCard.Build(P, _ctx, title, message, buttons, primary, i => FinishDialog(request, i), out var primaryButton);
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Center;
        card.Margin = new Thickness(24);
        if (_dialogs.Count > 0) _dialogs[^1].Card.IsVisible = false;
        _dialogs.Add(new DialogEntry { Request = request, Card = card, Primary = primaryButton });
        _dialogHost.Children.Add(card);
        _overlay.IsVisible = true;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Dispatcher.UIThread.Post(() => primaryButton?.Focus(), DispatcherPriority.Input);
    }

    internal void CancelDialog(int request) => RemoveDialog(request);

    private void FinishDialog(int request, int index)
    {
        if (!RemoveDialog(request)) return;
        _ctx.Sink.Send(MessageTypes.DialogResult, new Dictionary<string, object>(StringComparer.Ordinal) { { "request", request }, { "index", index } });
    }

    private bool RemoveDialog(int request)
    {
        var entry = _dialogs.FirstOrDefault(d => d.Request == request);
        if (entry is null) return false;
        _dialogs.Remove(entry);
        _dialogHost.Children.Remove(entry.Card);
        if (_dialogs.Count == 0) _overlay.IsVisible = false;
        else _dialogs[^1].Card.IsVisible = true;
        return true;
    }

    // Closing: the shim decides (it may ask to save first).

    internal Dictionary<string, object> CurrentState()
    {
        double scaling = Screens?.ScreenFromWindow(this)?.Scaling ?? RenderScaling;
        if (scaling <= 0) scaling = 1;
        var pos = WindowState == WindowState.Normal ? Position : _normalPosition;
        var size = WindowState == WindowState.Normal ? new Size(Width, Height) : _normalSize;
        var d = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            { "w", size.Width },
            { "h", size.Height },
            { "max", WindowState == WindowState.Maximized },
            { "zoom", _zoom },
        };
        if (_current is not null) d["tab"] = _current;
        d["x"] = pos.X / scaling;
        d["y"] = pos.Y / scaling;
        d["scale"] = scaling;
        return d;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_dialogs.Count > 0)
        {
            foreach (var d in _dialogs.ToArray()) FinishDialog(d.Request, -1);
        }
        _closeAsked = true;
        _ctx.Sink.Send(MessageTypes.CloseRequest, new Dictionary<string, object>(StringComparer.Ordinal) { { "state", CurrentState() } });
    }

    internal bool CloseAsked => _closeAsked;

    internal void CloseNow()
    {
        _allowClose = true;
        Close();
    }
}
