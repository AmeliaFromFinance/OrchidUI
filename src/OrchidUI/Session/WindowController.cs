using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading;
using OrchidUI.Protocol;

namespace OrchidUI
{
    // The shim's half of the window: everything that decides something (state, validation,
    // saving, closing) happens here; orchid-host only draws and reports input. All of it runs on
    // the session's callback thread, which plays the part of a UI thread.
    internal sealed class WindowController : ISessionHandler
    {
        // Anything bigger wouldn't fit the 1 MiB "open" frame once base64-encoded.
        internal const long MaxImageBytes = 1024 * 1024;
        internal const long MaxFontBytes = 1024 * 1024;

        private readonly Orchid O;
        private readonly OrchidTheme _theme;
        private readonly WindowOptions W;
        private readonly LayoutOptions L;
        private readonly UiStrings S;
        private readonly bool _dialogOnly;
        private readonly Dictionary<string, Field> _byId = new Dictionary<string, Field>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _tabIdByName = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _tabNameById = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, FieldState> _last = new Dictionary<string, FieldState>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _copyText = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly ManualResetEvent _closedEvent = new ManualResetEvent(false);
        private StatusSpec _lastStatus;
        private WindowSpec _spec;
        private SessionLink _link;
        private Timer _autoSave;
        private DateTime _savedAt = DateTime.MinValue;
        private Dictionary<string, object> _hostState;
        private string _navigation;
        private bool _forceClose;
        private bool _closeSent;

        public WindowController(Orchid owner, bool dialogOnly = false)
        {
            O = owner;
            _dialogOnly = dialogOnly;
            _theme = owner.Theme.Clone();
            W = owner.Window.Clone();
            L = owner.Layout.Clone();
            S = owner.Strings.Clone();
        }

        public bool IsOpen { get { var l = _link; return l != null && l.IsOpen && !_closeSent; } }
        public string LinkDescription { get { var l = _link; return l == null ? "not connected" : l.Describe; } }

        // Starting

        public bool Start()
        {
            BuildSpec();
            IListener listener;
            string transport = ChooseTransport();
            string id = SessionFile.NewId();
            int port = 0;
            string pipe = null;
            if (transport == "pipe")
            {
                pipe = "orchidui-" + id;
                listener = new PipeListenerTransport(pipe);
            }
            else
            {
                var tcp = new TcpListenerTransport();
                port = tcp.Port;
                listener = tcp;
            }

            SessionFile file;
            try
            {
                var dir = SessionDirectory.Resolve();
                if (dir.Warning != null) O.LogWarn(dir.Warning);
                file = SessionFile.Write(dir, id, transport, pipe, port, Tokens.New());
            }
            catch (Exception ex)
            {
                listener.Dispose();
                O.LogError("Could not create the session file for orchid-host: " + ex.Message);
                return false;
            }

            _link = new SessionLink(listener, file, this, O.LogDebug);
            _link.Start();

            if (OrchidPlatform.EnvBool("ORCHIDUI_NO_LAUNCH") == true)
            {
                O.Log("Waiting for orchid-host --watch to pick up the window (ORCHIDUI_NO_LAUNCH is set).");
            }
            else
            {
                var result = HostLauncher.Launch(HostLauncher.FindHost(), file, O.LogWarn);
                if (result.Error != null)
                {
                    // An untrusted binary is never a reason to wait for --watch instead.
                    if (!OrchidPlatform.IsWine || result.Untrusted)
                    {
                        O.LogError(result.Error);
                        _link.Close("orchid-host wasn't started", false);
                        return false;
                    }
                    O.LogError(result.Error + " " + WatchHint());
                }
                else if (!WaitForHost(result))
                {
                    return false;
                }
            }

            if (!_link.WaitOpened(SessionLink.ConnectTimeout + TimeSpan.FromSeconds(5)))
            {
                _link.Close("orchid-host didn't open the window in time", true);
                return false;
            }
            return true;
        }

        // Under Wine start.exe returns immediately, and when start /unix is broken nothing connects.
        private bool WaitForHost(LaunchResult result)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(OrchidPlatform.IsWine ? 8 : 30);
            while (DateTime.UtcNow < deadline)
            {
                if (_link.WaitOpened(TimeSpan.FromMilliseconds(200))) return true;
                if (_link.HasEnded) return false;
                if (!result.Detached && result.Process != null && result.Process.HasExited)
                {
                    int code = result.Process.ExitCode;
                    O.LogError("orchid-host exited before opening the window (exit code " + code.ToString(CultureInfo.InvariantCulture) + "). See " + HostLauncher.LogLocation() + ".");
                    _link.Close("orchid-host exited", false);
                    return false;
                }
            }
            if (OrchidPlatform.IsWine && !_link.IsOpen)
            {
                // Not always Wine's bug: the host may have started and refused the session; its log says.
                O.LogError("orchid-host hasn't connected 8 s after 'start /unix'. If it started, its log says why: " + HostLauncher.LogLocation()
                    + ". If that log has nothing new, this Wine can't start Linux programs (Wine 9.5 to 10.0-rc2 can't). " + WatchHint());
            }
            return true;
        }

        private static string WatchHint()
        {
            return "Run 'orchid-host --watch' in a terminal on your Linux or macOS desktop; the window opens as soon as it's running.";
        }

        private static string ChooseTransport()
        {
            var forced = (OrchidPlatform.Env("ORCHIDUI_TRANSPORT") ?? "").ToLowerInvariant();
            if (forced == "pipe" || forced == "tcp") return forced;
            if (OrchidPlatform.IsWine) return "tcp";
            return OrchidPlatform.IsWindowsOs ? "pipe" : "tcp";
        }

        public void WaitClosed() { _closedEvent.WaitOne(); }

        public void Post(Action action)
        {
            var l = _link;
            if (l == null) { action(); return; }
            l.Run(action);
        }

        // The spec

        public WindowSpec BuildSpec()
        {
            if (_spec != null) return _spec;
            if (!_dialogOnly) O.NormaliseForWindow();
            var state = _dialogOnly ? null : O.LoadUiState();
            var spec = new WindowSpec
            {
                Extension = O.ExtensionName,
                Version = O.Version,
                Subtitle = O.Subtitle,
                HeaderTitle = O.HeaderTitle,
                HeaderSubtitle = O.HeaderSubtitle,
                HeaderHeight = O.HeaderHeight,
                Footer = O.FooterText,
                Links = O.Links.ToList(),
            };
            if (!string.IsNullOrWhiteSpace(O.HeaderImage)) spec.HeaderImage = LoadImage(O.HeaderImage, "Header image");

            _navigation = (L.Navigation == NavigationStyle.Auto ? (O.Tabs.Count > 1 ? NavigationStyle.Sidebar : NavigationStyle.None) : L.Navigation).ToString().ToLowerInvariant();
            spec.Title = !string.IsNullOrWhiteSpace(W.Title) ? W.Title : UiStrings.F(S.WindowTitle, O.ExtensionName);
            bool customChrome = W.Chrome == ChromeMode.Custom && OrchidPlatform.IsWindowsOs && !OrchidPlatform.IsWine;
            if (!customChrome && !string.IsNullOrEmpty(O.Version) && string.IsNullOrWhiteSpace(W.Title))
                spec.Title += "  ·  v" + O.Version.TrimStart('v', 'V');

            int n = 0;
            foreach (var tab in O.Tabs)
            {
                string tabId = "t" + (++n).ToString(CultureInfo.InvariantCulture);
                _tabIdByName[tab] = tabId;
                _tabNameById[tabId] = tab;
                string icon;
                O.TabIcons.TryGetValue(tab, out icon);
                spec.Tabs.Add(new TabSpec { Id = tabId, Name = tab, Icon = IconResolver.ToSpec(icon), Order = n });
            }

            int f = 0;
            if (!_dialogOnly)
            {
                foreach (var field in O.Fields)
                {
                    field.Id = "f" + (++f).ToString(CultureInfo.InvariantCulture);
                    _byId[field.Id] = field;
                    string tabId;
                    if (!_tabIdByName.TryGetValue(field.Tab, out tabId)) continue;
                    if (field is CustomField)
                        O.LogWarn("AddCustom on tab '" + field.Tab + "' can't show a WPF element in the native window; a placeholder card is shown instead.");
                    else if (field.Kind == FieldKinds.Unsupported)
                        O.LogDebug("'" + (field.Label ?? field.Key) + "' (" + field.ToSpec(tabId).PropStr("api", "?") + ") isn't drawn by OrchidUI yet; its value is kept.");
                    var copy = field as CopyField;
                    if (copy != null) _copyText[field.Id] = copy.Evaluate();
                    spec.Fields.Add(field.ToSpec(tabId));
                }
            }

            spec.Theme = ThemeSpecFor(_theme, O.LogWarn);
            spec.Window = WindowDict(state, customChrome);
            spec.Layout = LayoutDict();
            foreach (var fi in typeof(UiStrings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var v = fi.GetValue(S) as string;
                if (v != null) spec.Strings[fi.Name] = v;
            }

            double scale = Math.Max(0.5, Math.Min(3, OrchidPlatform.EnvDouble("ORCHIDUI_SCALE") ?? W.Scale));
            spec.Window["scale"] = scale;
            spec.Zoom = scale;
            if (W.AllowZoom && state != null && state.Zoom > 0) spec.Zoom = Math.Max(0.5, Math.Min(3, state.Zoom));

            string start = L.StartTab;
            if (L.RememberTab && state != null && !string.IsNullOrEmpty(state.Tab)) start = state.Tab;
            if (start == null || !_tabIdByName.ContainsKey(start)) start = O.Tabs.FirstOrDefault();
            spec.StartTab = start == null ? null : _tabIdByName[start];

            lock (O.SyncRoot)
            {
                foreach (var field in O.Fields.Where(x => x.Built))
                {
                    foreach (var k in field.Keys)
                    {
                        var raw = O.GetPendingRaw(k);
                        spec.Values[k] = raw;
                    }
                }
            }
            spec.States = ComputeStates();
            foreach (var s in spec.States) _last[s.Id] = s;
            spec.Status = _lastStatus = ComputeStatus();
            if (_dialogOnly) spec.Window["mode"] = "dialog";
            // Previews left out of the open frame follow once the host is ready.
            _previewsWaiting = FitPreviews(spec.States, () => Encoding.UTF8.GetByteCount(Json.Serialize(new Dictionary<string, object>(StringComparer.Ordinal) { { "spec", spec.ToJson() } }, false))) > 0;
            FitInOneFrame(spec, O.LogWarn);
            _spec = spec;
            return spec;
        }

        // The whole spec travels in one "open" frame (1 MiB at most). Files embedded as base64
        // (font, header image, window icon) are dropped, largest first, until it fits; each one
        // dropped is logged, and the window opens without it.
        internal const int SpecBudgetBytes = FrameCodec.MaxFrameBytes - 16 * 1024;

        internal static void FitInOneFrame(WindowSpec spec, Action<string> warn)
        {
            Func<int> size = () => Encoding.UTF8.GetByteCount(Json.Serialize(new Dictionary<string, object>(StringComparer.Ordinal) { { "spec", spec.ToJson() } }, false));
            if (size() <= SpecBudgetBytes) return;
            var candidates = new List<KeyValuePair<long, Action>>();
            if (spec.Theme != null && spec.Theme.FontFileData != null)
            {
                var name = spec.Theme.FontFileName;
                candidates.Add(new KeyValuePair<long, Action>(spec.Theme.FontFileData.Length, () =>
                {
                    spec.Theme.FontFileData = null;
                    spec.Theme.FontFileName = null;
                    warn("Font file " + name + " is too big to send to the settings window (keep it under about 600 KB); the theme's font list is used instead.");
                }));
            }
            if (spec.HeaderImage != null && spec.HeaderImage.Base64 != null)
            {
                candidates.Add(new KeyValuePair<long, Action>(spec.HeaderImage.Base64.Length, () =>
                {
                    spec.HeaderImage = null;
                    warn("Header image is too big to send to the settings window (keep it under about 600 KB, or use an https URL); the banner is shown without it.");
                }));
            }
            object iconObj;
            var icon = spec.Window.TryGetValue("icon", out iconObj) ? iconObj as Dictionary<string, object> : null;
            var iconData = icon == null ? null : Json.Str(icon, "data");
            if (iconData != null)
            {
                candidates.Add(new KeyValuePair<long, Action>(iconData.Length, () =>
                {
                    spec.Window.Remove("icon");
                    warn("Window icon is too big to send to the settings window (keep it under about 600 KB); the default icon is used.");
                }));
            }
            foreach (var c in candidates.OrderByDescending(x => x.Key))
            {
                c.Value();
                if (size() <= SpecBudgetBytes) return;
            }
        }

        public Dictionary<string, object> BuildOpenPayload()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal) { { "spec", BuildSpec().ToJson() } };
        }

        internal static ThemeSpec ThemeSpecFor(OrchidTheme theme, Action<string> warn)
        {
            var resolved = ThemeResolver.Resolve(theme);
            var t = new ThemeSpec
            {
                IsDark = resolved.IsDark,
                FontFamily = string.IsNullOrWhiteSpace(theme.FontFamily) ? OrchidTheme.DefaultFonts : theme.FontFamily,
                HeadingFontFamily = string.IsNullOrWhiteSpace(theme.HeadingFontFamily) ? OrchidTheme.DefaultHeadingFonts : theme.HeadingFontFamily,
                MonoFontFamily = string.IsNullOrWhiteSpace(theme.MonoFontFamily) ? OrchidTheme.DefaultMonoFonts : theme.MonoFontFamily,
                FontSize = theme.FontSize,
                SmallFontSize = theme.SmallFontSize,
                PageTitleSize = theme.PageTitleSize,
                SectionTitleSize = theme.SectionTitleSize,
                HeroTitleSize = theme.HeroTitleSize,
                HeadingWeight = string.IsNullOrWhiteSpace(theme.HeadingWeight) ? "SemiBold" : theme.HeadingWeight,
                CornerRadius = Math.Max(0, theme.CornerRadius),
                LargeCornerRadius = Math.Max(0, theme.LargeCornerRadius),
                BorderThickness = Math.Max(0, theme.BorderThickness),
                Shadows = theme.Shadows,
            };
            foreach (var kv in resolved.Colors) t.Colors[kv.Key] = kv.Value.Argb;
            foreach (var g in resolved.Gradients)
            {
                t.Gradients.Add(new GradientData
                {
                    Key = g.Key, X1 = g.X1, Y1 = g.Y1, X2 = g.X2, Y2 = g.Y2,
                    Colors = g.Colors.Select(c => c.Argb).ToArray(),
                    Offsets = g.Offsets.ToArray(),
                });
            }
            if (!string.IsNullOrWhiteSpace(theme.FontFile))
            {
                var bytes = ReadUserFile(theme.FontFile, MaxFontBytes, "Font file", warn);
                if (bytes != null)
                {
                    t.FontFileName = Path.GetFileName(theme.FontFile.Trim());
                    t.FontFileData = Convert.ToBase64String(bytes);
                }
            }
            return t;
        }

        private Dictionary<string, object> WindowDict(UiState state, bool customChrome)
        {
            double minW = W.MinWidth > 0 ? W.MinWidth : (_navigation == "sidebar" ? 720 : 460);
            double minH = W.MinHeight > 0 ? W.MinHeight : 420;
            double maxW = Math.Max(minW, W.MaxWidth);
            double maxH = Math.Max(minH, W.MaxHeight);
            var st = W.RememberBounds ? state : null;
            double width = W.Width, height = W.Height;
            if (st != null && W.Resizable && st.W > 0 && st.H > 0) { width = st.W; height = st.H; }
            // ORCHIDUI_BACKDROP overrides the window's backdrop.
            var backdrop = W.Backdrop;
            BackdropMode fromEnv;
            var backdropEnv = OrchidPlatform.Env("ORCHIDUI_BACKDROP");
            if (backdropEnv != null && Enum.TryParse(backdropEnv, true, out fromEnv)) backdrop = fromEnv;
            var d = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "width", Math.Max(minW, Math.Min(maxW, width)) },
                { "height", Math.Max(minH, Math.Min(maxH, height)) },
                { "minWidth", minW },
                { "minHeight", minH },
                { "maxWidth", double.IsInfinity(maxW) ? 0 : maxW },
                { "maxHeight", double.IsInfinity(maxH) ? 0 : maxH },
                { "resizable", W.Resizable },
                { "canMinimize", W.CanMinimize },
                { "canMaximize", W.Resizable && W.CanMaximize },
                { "topmost", W.Topmost },
                { "showInTaskbar", W.ShowInTaskbar },
                { "allowZoom", W.AllowZoom },
                { "chrome", customChrome ? "custom" : "native" },
                { "backdrop", backdrop == BackdropMode.Solid ? "solid" : backdrop == BackdropMode.Acrylic ? "acrylic" : backdrop == BackdropMode.MicaAlt ? "micaAlt" : "mica" },
            };
            double? left = W.Left, top = W.Top;
            if (st != null && st.HasPosition) { left = st.X; top = st.Y; }
            if (left.HasValue && top.HasValue) { d["x"] = left.Value; d["y"] = top.Value; }
            if (st != null && st.HasPosition && st.Scale > 0) d["screenScale"] = st.Scale;
            bool canMax = W.Resizable && W.CanMaximize;
            d["maximized"] = canMax && (W.StartMaximized || (st != null && st.Maximized));
            if (!string.IsNullOrWhiteSpace(W.IconPath))
            {
                var icon = LoadImage(W.IconPath, "Window icon");
                if (icon != null) d["icon"] = icon.ToJson();
            }
            return d;
        }

        private Dictionary<string, object> LayoutDict()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "navigation", _navigation },
                { "pane", L.Pane.ToString().ToLowerInvariant() },
                { "paneWidth", L.PaneWidth },
                { "compactPaneBelow", L.CompactPaneBelow },
                { "contentMaxWidth", L.ContentMaxWidth },
                { "contentPadding", L.ContentPadding },
                { "density", L.Density.ToString().ToLowerInvariant() },
                { "inputWidth", L.InputWidth },
                { "dropdownWidth", L.DropdownWidth },
                { "sliderWidth", L.SliderWidth },
                { "showPageTitle", L.ShowPageTitle },
                { "showSearch", L.ShowSearch },
                { "showStatus", L.ShowStatus },
                { "showDiscard", L.ShowDiscard },
                { "showSave", L.ShowSave },
                { "showSaveAndClose", L.ShowSaveAndClose },
                { "showDirtyMarkers", L.ShowDirtyMarkers },
                { "showTitleBarName", L.ShowTitleBarName },
                { "animations", OrchidPlatform.EnvBool("ORCHIDUI_ANIMATIONS") ?? L.Animations },
                { "autoSave", L.AutoSave },
                { "toastSeconds", L.ToastSeconds },
                { "showImportExport", L.ShowImportExport },
                { "showResetAll", L.ShowResetAll },
                { "showAbout", L.ShowAbout },
            };
        }

        // Header images and icons: an http(s) URL the host fetches itself, or a file the shim reads
        // (a regular file, size-capped) and sends as bytes. The host never gets a path.
        private ImageData LoadImage(string source, string what)
        {
            var s = source.Trim();
            if (ImageData.IsWebUrl(s)) return new ImageData { Url = s };
            var bytes = ReadUserFile(s, MaxImageBytes, what, O.LogWarn);
            if (bytes == null) return null;
            return new ImageData { Mime = MimeFor(s), Base64 = Convert.ToBase64String(bytes) };
        }

        internal static byte[] ReadUserFile(string path, long maxBytes, string what, Action<string> warn)
        {
            try
            {
                string problem;
                var full = UserFilePath(path, out problem);
                if (full == null) { warn(what + " " + problem + ": " + path.Trim()); return null; }
                var bytes = ReadCapped(full, maxBytes);
                if (bytes == null)
                {
                    warn(what + " is larger than " + SizeText(maxBytes) + " and was skipped: " + full);
                    return null;
                }
                return bytes;
            }
            catch (Exception ex)
            {
                warn(what + " could not be read: " + ex.Message);
                return null;
            }
        }

        private static string SizeText(long bytes)
        {
            return bytes >= 1024 * 1024
                ? (bytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture) + " MB"
                : (bytes / 1024).ToString(CultureInfo.InvariantCulture) + " KB";
        }

        // A user-named file as a full local path (relative to the Streamer.bot folder). Null, with the
        // reason, for anything but a regular local file. Network shares can stall on a dead server or
        // hand out the Windows sign-in; devices and /dev, /proc or /sys seen through Wine can be endless.
        internal static string UserFilePath(string path, out string problem)
        {
            problem = null;
            string p = (path ?? "").Trim();
            Uri uri;
            // Only a file: URL is decoded; a plain path keeps its % and # as they are.
            if (p.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(p, UriKind.Absolute, out uri) && uri.IsFile)
            {
                if (uri.IsUnc) { problem = "is on a network share, which isn't read"; return null; }
                p = uri.LocalPath;
            }
            if (p.Length == 0) { problem = "is empty"; return null; }
            if (IsShareOrDevice(p)) { problem = "is on a network share or a device, which isn't read"; return null; }
            string full = Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(Orchid.StreamerBotDirectory, p));
            if (IsShareOrDevice(full) || IsUnixSystemPath(full)) { problem = "is on a network share or a device, which isn't read"; return null; }
            var info = new FileInfo(full);
            if (!info.Exists) { problem = Directory.Exists(full) ? "is a folder" : "not found"; return null; }
            if ((info.Attributes & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
            {
                problem = "isn't a regular file";
                return null;
            }
            return full;
        }

        private static bool IsShareOrDevice(string p)
        {
            return p.StartsWith(@"\\", StringComparison.Ordinal) || p.StartsWith("//", StringComparison.Ordinal)
                || p.StartsWith(@"\/", StringComparison.Ordinal) || p.StartsWith(@"/\", StringComparison.Ordinal);
        }

        // /dev, /proc and /sys, directly (a Unix path) or through Wine's Z: drive.
        private static bool IsUnixSystemPath(string full)
        {
            var p = full.Replace('\\', '/').ToLowerInvariant();
            if (p.StartsWith("z:", StringComparison.Ordinal)) p = p.Substring(2);
            return p.StartsWith("/dev/", StringComparison.Ordinal) || p.StartsWith("/proc/", StringComparison.Ordinal)
                || p.StartsWith("/sys/", StringComparison.Ordinal);
        }

        // At most maxBytes, whatever the file's size claims to be (a named pipe or a file that grows
        // while it's read); null when there's more.
        internal static byte[] ReadCapped(string full, long maxBytes)
        {
            using (var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920))
                return ReadCapped(stream, maxBytes);
        }

        internal static byte[] ReadCapped(Stream stream, long maxBytes)
        {
            using (var ms = new MemoryStream())
            {
                var buffer = new byte[81920];
                int n;
                while ((n = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, maxBytes + 1 - ms.Length))) > 0)
                {
                    ms.Write(buffer, 0, n);
                    if (ms.Length > maxBytes) return null;
                }
                return ms.ToArray();
            }
        }

        private static string MimeFor(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".bmp": return "image/bmp";
                case ".ico": return "image/x-icon";
                case ".webp": return "image/webp";
                default: return "application/octet-stream";
            }
        }

        // Field state (visibility, enabled, errors), sent as FieldState patches

        private bool ConditionVisible(Field f)
        {
            if (f.VisibleWhenPredicate == null) return true;
            try { return f.VisibleWhenPredicate(O.Values); }
            catch { return true; }
        }

        private List<FieldState> ComputeStates()
        {
            var list = new List<FieldState>();
            int errors = 0;
            foreach (var f in O.Fields.Where(x => x.Built && x.Id != null))
            {
                bool visible = ConditionVisible(f);
                var st = new FieldState { Id = f.Id, Visible = visible };
                if (f.EnabledWhenPredicate != null)
                {
                    try { st.Enabled = f.EnabledWhenPredicate(O.Values); }
                    catch { st.Enabled = true; }
                }
                st.Dirty = f.Keys.Any(O.IsKeyDirty);
                if (f.Validators.Count > 0)
                {
                    string error = visible && f.Key != null ? f.RunValidators(O.GetPendingRaw(f.Key)) : null;
                    f.CurrentError = error;
                    st.Error = error;
                    if (error != null) errors++;
                }
                if (f.Key != null)
                {
                    st.Display = f.DisplayFor(O.GetPendingRaw(f.Key));
                    st.Summary = f.SummaryFor(O.GetPendingRaw(f.Key), S);
                }
                if (f is ImageField) Preview(f, st);
                string text;
                if (_copyText.TryGetValue(f.Id, out text)) st.Text = text;
                list.Add(st);
            }
            O.ErrorCount = errors;
            return list;
        }

        // AddImageInput's preview: an http(s) URL the host fetches, or a file the shim reads (Windows
        // or Wine path, as the value is). Kept per field until the value changes.
        internal const long MaxPreviewBytes = 512 * 1024;
        private readonly Dictionary<string, KeyValuePair<string, ImageData>> _previews = new Dictionary<string, KeyValuePair<string, ImageData>>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _previewNotes = new Dictionary<string, string>(StringComparer.Ordinal);

        private void Preview(Field f, FieldState st)
        {
            var source = (O.GetPendingRaw(f.Key) as string ?? "").Trim();
            st.ImageSource = source;
            KeyValuePair<string, ImageData> cached;
            if (!_previews.TryGetValue(f.Id, out cached) || cached.Key != source)
            {
                ImageData image = null;
                string note = null;
                if (ImageData.IsWebUrl(source)) image = new ImageData { Url = source };
                else if (source.Length > 0)
                {
                    image = LoadPreview(source, out note);
                }
                cached = new KeyValuePair<string, ImageData>(source, image);
                _previews[f.Id] = cached;
                if (note == null) _previewNotes.Remove(f.Id); else _previewNotes[f.Id] = note;
            }
            st.Image = cached.Value;
            string n;
            if (_previewNotes.TryGetValue(f.Id, out n)) st.Display = n;
        }

        // Only an image file is read for a preview (by its name and its first bytes), so a path typed
        // into an image input never sends anything else to the host.
        private static readonly string[] PreviewExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico" };

        private ImageData LoadPreview(string path, out string note)
        {
            note = null;
            try
            {
                string problem;
                var full = UserFilePath(path, out problem);
                if (full == null) { note = problem == "not found" ? S.FileNotFound : S.NoPreview; return null; }
                if (!PreviewExtensions.Contains(Path.GetExtension(full).ToLowerInvariant())) { note = S.NoPreview; return null; }
                if (new FileInfo(full).Length > MaxPreviewBytes) { note = S.TooLargeToPreview; return null; }
                var bytes = ReadCapped(full, MaxPreviewBytes);
                if (bytes == null) { note = S.TooLargeToPreview; return null; }
                if (!LooksLikeImage(bytes)) { note = S.NoPreview; return null; }
                return new ImageData { Mime = MimeFor(full), Base64 = Convert.ToBase64String(bytes) };
            }
            catch (Exception) { note = S.NoPreview; return null; }
        }

        internal static bool LooksLikeImage(byte[] b)
        {
            Func<int, string, bool> at = (i, ascii) => b.Length >= i + ascii.Length && Encoding.ASCII.GetString(b, i, ascii.Length) == ascii;
            return (b.Length >= 8 && b[0] == 0x89 && at(1, "PNG\r\n"))
                || (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
                || at(0, "GIF87a") || at(0, "GIF89a")
                || at(0, "BM")
                || (at(0, "RIFF") && at(8, "WEBP"))
                || (b.Length >= 4 && b[0] == 0 && b[1] == 0 && b[2] == 1 && b[3] == 0);
        }

        private StatusSpec ComputeStatus()
        {
            bool dirty = O.IsDirty;
            int errors = O.ErrorCount;
            var s = new StatusSpec { Dirty = dirty, Errors = errors, CanDiscard = dirty, CanSave = errors == 0 };
            if (errors > 0)
            {
                s.Text = UiStrings.F(S.NeedsAttention, errors);
                s.Tone = "danger";
            }
            else if (dirty && !L.AutoSave)
            {
                s.Text = S.UnsavedChanges;
                s.Tone = "normal";
            }
            else
            {
                s.Text = L.AutoSave && _savedAt == DateTime.MinValue ? S.AutoSaved
                    : _savedAt == DateTime.MinValue ? S.AllSaved
                    : UiStrings.F(S.SavedAt, _savedAt.ToString(L.AutoSave ? "HH:mm:ss" : "HH:mm", CultureInfo.CurrentCulture));
                s.Tone = "muted";
            }
            return s;
        }

        // Re-evaluates every callback and sends what changed, plus any values the host must show.
        private void Refresh(IEnumerable<string> pushKeys = null, IEnumerable<Field> respec = null)
        {
            var states = ComputeStates();
            var status = ComputeStatus();
            var changed = new List<FieldState>();
            // A preview goes with a state only when what it shows changed; the host keeps it otherwise.
            var withImage = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in states)
            {
                FieldState old;
                bool known = _last.TryGetValue(s.Id, out old);
                if (known && old.SameAs(s)) continue;
                if (s.ImageSource != null && (!known || old.ImageSource != s.ImageSource)) withImage.Add(s.Id);
                _last[s.Id] = s;
                changed.Add(s);
            }
            var payload = new Dictionary<string, object>(StringComparer.Ordinal);
            Action putStates = () =>
            {
                if (changed.Count == 0) return;
                payload["states"] = changed.Select(s =>
                {
                    var d = s.ToJson();
                    if (!withImage.Contains(s.Id)) d.Remove("image");
                    return (object)d;
                }).ToArray();
            };
            putStates();
            var st = status.ToJson();
            if (_lastStatus == null || Json.Serialize(_lastStatus.ToJson(), false) != Json.Serialize(st, false))
            {
                payload["status"] = st;
                _lastStatus = status;
            }
            if (pushKeys != null)
            {
                var values = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var k in pushKeys.Distinct()) values[k] = O.GetPendingRaw(k);
                if (values.Count > 0) payload["values"] = values;
            }
            if (respec != null)
            {
                var specs = respec.Select(f => (object)f.ToSpec(_tabIdByName[f.Tab]).ToJson()).ToArray();
                if (specs.Length > 0) payload["fields"] = specs;
            }
            int sent = withImage.Count;
            int leftOut = sent == 0 ? 0 : FitPreviews(changed.Where(x => withImage.Contains(x.Id)), () =>
            {
                putStates();
                return Encoding.UTF8.GetByteCount(Json.Serialize(payload, false));
            });
            bool other = payload.ContainsKey("values") || payload.ContainsKey("fields");
            if (payload.Count > 0 && _link != null) _link.Send(MessageTypes.Patch, payload);
            // What was left out goes in the next frames, as long as each one gets something through.
            if (leftOut > 0 && (leftOut < sent || other)) Post(() => Refresh());
        }

        private bool _previewsWaiting;

        // Previews are what make a frame big (each up to MaxPreviewBytes, as base64). While over
        // budget the largest is dropped and its source marked, so the next refresh resends it.
        // Returns how many were dropped.
        private const string LeftOutMark = "\u0000left out";

        private int FitPreviews(IEnumerable<FieldState> states, Func<int> size)
        {
            if (size() <= SpecBudgetBytes) return 0;
            int n = 0;
            foreach (var s in states.Where(x => x.Image != null && x.Image.Base64 != null).OrderByDescending(x => x.Image.Base64.Length).ToList())
            {
                s.Image = null;
                s.Display = S.LoadingPreview;
                s.ImageSource += LeftOutMark;
                n++;
                if (size() <= SpecBudgetBytes) break;
            }
            return n;
        }

        // Messages from the host (callback thread)

        public void Handle(Frame frame)
        {
            var p = frame.Payload;
            var state = Json.Obj(p, "state");
            if (state != null) _hostState = state;
            switch (frame.Type)
            {
                case MessageTypes.Ready:
                    if (_previewsWaiting)
                    {
                        _previewsWaiting = false;
                        Refresh();
                    }
                    break;
                case MessageTypes.Change:
                {
                    var value = Json.Get(p, "value");
                    // A path from the host's file dialog is shown as-is but stored as Streamer.bot sees
                    // it (Z:\ under Wine).
                    var shown = value;
                    if (Json.Bool(p, "native") && value is string) value = HostPaths.FromHost((string)value);
                    OnChange(Json.Str(p, "field"), Json.Str(p, "key"), value, shown);
                    break;
                }
                case MessageTypes.Click:
                    OnClick(Json.Str(p, "field"));
                    break;
                case MessageTypes.Command:
                {
                    var path = Json.Str(p, "path");
                    if (path != null && Json.Bool(p, "native")) path = HostPaths.FromHost(path);
                    OnCommand(Json.Str(p, "name"), Json.Str(p, "field"), path);
                    break;
                }
                case MessageTypes.TabShown:
                    OnTabShown(Json.Str(p, "tab"));
                    break;
                case MessageTypes.CloseRequest:
                    CloseChecks();
                    break;
                case MessageTypes.Log:
                    var message = Json.Str(p, "message", "");
                    if (message.Length > 2000) message = message.Substring(0, 2000) + "…";
                    if (Json.Str(p, "level") == "error") O.LogWarn("orchid-host: " + message);
                    else O.LogDebug("orchid-host: " + message);
                    break;
            }
        }

        private Field Find(string id)
        {
            Field f;
            return id != null && _byId.TryGetValue(id, out f) && f.Built ? f : null;
        }

        private void OnChange(string id, string key, object value, object shown)
        {
            var f = Find(id);
            if (f == null || key == null || !f.Keys.Contains(key)) return;
            var stored = O.ApplyChange(f, key, value);
            bool pushBack = Json.Serialize(stored, false) != Json.Serialize(shown, false);
            O.RunChangeHandlers(f);
            Refresh(pushBack ? new[] { key } : null);
            KickAutoSave();
        }

        private void OnClick(string id)
        {
            var f = Find(id);
            var b = f as ButtonField;
            var path = f as PathField;
            if (b != null && b.OnClick != null) O.Safe(b.OnClick, "'" + b.Text + "'");
            else if (path != null && path.OnAction != null) O.Safe(path.OnAction, "'" + path.ActionText + "'");
            else return;
            Refresh();
        }

        private void OnTabShown(string tabId)
        {
            string name;
            if (tabId == null || !_tabNameById.TryGetValue(tabId, out name)) return;
            foreach (var cf in O.Fields.OfType<CopyField>().Where(x => x.Built && x.Tab == name && x.Id != null))
                _copyText[cf.Id] = cf.Evaluate();
            Refresh();
        }

        private void OnCommand(string name, string fieldId, string path = null)
        {
            switch (name)
            {
                case Commands.Save:
                    TrySave();
                    break;
                case Commands.SaveAndClose:
                    if (!TrySave()) return;
                    _forceClose = true;
                    SendClose();
                    break;
                case Commands.Done:
                    if (O.ErrorCount > 0)
                    {
                        ShowToast(S.FixBeforeSaving, InfoKind.Danger);
                        JumpToFirstError();
                        return;
                    }
                    AutoSaveNow();
                    if (!O.IsDirty)
                    {
                        _forceClose = true;
                        SendClose();
                    }
                    break;
                case Commands.Discard:
                {
                    var changed = new List<Field>();
                    var keys = O.RevertToSaved(changed).ToList();
                    foreach (var f in changed) O.RunChangeHandlers(f);
                    Refresh(keys);
                    ShowToast(S.ChangesDiscarded, InfoKind.Info);
                    break;
                }
                case Commands.Refresh:
                    RefreshOptions(Find(fieldId));
                    break;
                case Commands.ResetAll:
                    ResetAll();
                    break;
                case Commands.Export:
                    Export(path);
                    break;
                case Commands.Import:
                    Import(path);
                    break;
                case Commands.About:
                    About();
                    break;
                case Commands.ResetField:
                {
                    var f = Find(fieldId);
                    if (f == null || !f.Keys.Any()) return;
                    var keys = O.ResetField(f).ToList();
                    O.RunChangeHandlers(f);
                    Refresh(keys);
                    KickAutoSave();
                    break;
                }
            }
        }

        // The window's More menu: Reset to defaults, Export, Import, About.

        private void ResetAll()
        {
            if (Dialog(S.ResetAllTitle, UiStrings.F(L.AutoSave ? S.ResetAllBodyAutoSave : S.ResetAllBody, O.ExtensionName), new[] { S.Cancel, S.Reset }, 1, Orchid.DialogTimeout) != 1) return;
            var keys = new List<string>();
            var fields = O.Fields.Where(f => f.Built && f.Keys.Any()).ToList();
            foreach (var f in fields) keys.AddRange(O.ResetField(f));
            foreach (var f in fields) O.RunChangeHandlers(f);
            Refresh(keys);
            ShowToast(L.AutoSave ? S.ResetAllDoneAutoSave : S.ResetAllDone, InfoKind.Info);
            KickAutoSave();
        }

        // The window's values (saved or not) as the same JSON the settings are stored as.
        private void Export(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var target = ExportPath(path);
                // ".json" was added: the dialog didn't ask about replacing that file, so don't.
                if (!string.Equals(target, Path.GetFullPath(path.Trim()), StringComparison.OrdinalIgnoreCase) && File.Exists(target))
                    throw new IOException(Path.GetFileName(target) + " already exists. Choose it in the dialog to replace it.");
                path = target;
                var data = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var f in O.Fields.Where(x => x.Built))
                    foreach (var k in f.Keys) data[k] = O.GetPendingRaw(k);
                File.WriteAllText(path, Json.Serialize(data, true));
                O.Log("Settings exported to " + path + ".");
                ShowToast(UiStrings.F(S.ExportDone, Path.GetFileName(path)), InfoKind.Success);
            }
            catch (Exception ex)
            {
                O.LogError("Exporting settings to " + path + " failed: " + ex.Message);
                Dialog(S.ErrorTitle, UiStrings.F(S.ErrorBody, ex.Message), new[] { S.Ok }, 0, Orchid.DialogTimeout);
            }
        }

        // The host's save-dialog path, forced to .json and a local disk, so whatever the host sends
        // an export can't overwrite a program or a non-JSON config file.
        internal static string ExportPath(string path)
        {
            var p = path.Trim();
            if (!Path.IsPathRooted(p) || IsShareOrDevice(p)) throw new IOException("Exports go to a full path on a local disk: " + p);
            if (!string.Equals(Path.GetExtension(p), ".json", StringComparison.OrdinalIgnoreCase)) p += ".json";
            p = Path.GetFullPath(p);
            if (IsShareOrDevice(p) || IsUnixSystemPath(p)) throw new IOException("Exports go to a full path on a local disk: " + p);
            return p;
        }

        // Settings from an exported file: each key this window has, as the field normalises it.
        // Nothing is stored until Save, or until auto-save runs.
        internal const long MaxImportBytes = 4 * 1024 * 1024;

        private void Import(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            Dictionary<string, object> data;
            try
            {
                string problem;
                var full = UserFilePath(path, out problem);
                if (full == null) throw new IOException("The file " + (problem == "not found" ? "wasn't found" : problem) + ": " + path);
                var bytes = ReadCapped(full, MaxImportBytes);
                if (bytes == null) throw new InvalidDataException("The file is larger than 4 MB.");
                data = Json.ParseObject(new StreamReader(new MemoryStream(bytes), true).ReadToEnd());
            }
            catch (Exception ex)
            {
                O.LogError("Importing settings from " + path + " failed: " + ex.Message);
                Dialog(S.ErrorTitle, UiStrings.F(S.ErrorBody, ex.Message), new[] { S.Ok }, 0, Orchid.DialogTimeout);
                return;
            }
            var keys = new List<string>();
            var changed = new List<Field>();
            foreach (var f in O.Fields.Where(x => x.Built && x.Keys.Any()))
            {
                bool any = false;
                foreach (var k in f.Keys)
                {
                    object v;
                    if (!data.TryGetValue(k, out v)) continue;
                    O.ImportValue(f, k, v);
                    keys.Add(k);
                    any = true;
                }
                if (any) changed.Add(f);
            }
            foreach (var f in changed) O.RunChangeHandlers(f);
            Refresh(keys);
            O.Log("Imported " + keys.Count + " settings from " + path + ".");
            // (Auto-save stores nothing while a value has an error.)
            bool saves = L.AutoSave && O.ErrorCount == 0;
            Dialog(S.ImportDoneTitle, UiStrings.F(saves ? S.ImportDoneBodyAutoSave : S.ImportDoneBody, keys.Count), new[] { S.Ok }, 0, Orchid.DialogTimeout);
            KickAutoSave();
        }

        private void About()
        {
            var body = new StringBuilder();
            body.Append("**").Append(O.ExtensionName).Append("**");
            if (!string.IsNullOrEmpty(O.Version)) body.Append(" ").Append(O.Version);
            body.Append('\n');
            if (!string.IsNullOrEmpty(O.Subtitle)) body.Append(O.Subtitle).Append('\n');
            foreach (var link in O.Links) body.Append('\n').Append(link.Key).Append(": ").Append(link.Value);
            body.Append("\n\nOrchidUI ").Append(Orchid.FrameworkVersion).Append(" (native) · ").Append(OrchidPlatform.Describe());
            int choice = Dialog(S.About, body.ToString(), new[] { S.CopyDiagnostics, S.Close }, 1, Orchid.DialogTimeout);
            if (choice == 0 && _link != null)
                _link.Send(MessageTypes.Clipboard, new Dictionary<string, object>(StringComparer.Ordinal) { { "text", O.Diagnostics() } });
        }

        // A refreshable dropdown or multi-select's refresh button: the loader's new list (as
        // UpdateDropdown), or the onRefresh callback, which calls UpdateDropdown itself.
        private void RefreshOptions(Field f)
        {
            Func<IEnumerable<string>> loader = null;
            Action onRefresh = null;
            var dd = f as DropdownField;
            var ms = f as MultiSelectField;
            if (dd != null) { loader = dd.Loader; onRefresh = dd.OnRefresh; }
            else if (ms != null) { loader = ms.Loader; onRefresh = ms.OnRefresh; }
            else return;
            if (loader != null) UpdateOptions(f, O.SafeLoad(loader, f.Label).ToList(), null);
            else if (onRefresh != null)
            {
                O.Safe(onRefresh, "refreshing '" + (f.Label ?? f.Key) + "'");
                Refresh();
            }
        }

        private void KickAutoSave()
        {
            if (!L.AutoSave) return;
            var delay = Math.Max(100, L.AutoSaveDelayMs);
            if (_autoSave == null) _autoSave = new Timer(_ => Post(AutoSaveNow), null, delay, Timeout.Infinite);
            else _autoSave.Change(delay, Timeout.Infinite);
        }

        private void AutoSaveNow()
        {
            if (_autoSave != null) _autoSave.Change(Timeout.Infinite, Timeout.Infinite);
            if (O.ErrorCount > 0 || !O.IsDirty) return;
            O.SaveInternal(true);
        }

        private bool TrySave()
        {
            if (O.ErrorCount > 0)
            {
                ShowToast(S.FixBeforeSaving, InfoKind.Danger);
                JumpToFirstError();
                return false;
            }
            return O.SaveInternal(false);
        }

        private void JumpToFirstError()
        {
            var f = O.Fields.FirstOrDefault(x => x.CurrentError != null && x.Built && x.Id != null);
            if (f != null && _link != null) _link.Send(MessageTypes.Jump, new Dictionary<string, object>(StringComparer.Ordinal) { { "field", f.Id } });
        }

        internal void MarkSaved(bool quiet)
        {
            _savedAt = DateTime.Now;
            Refresh();
            if (!quiet) ShowToast(S.SettingsSaved, InfoKind.Success);
        }

        // API calls routed onto the callback thread

        internal void PushValues(IEnumerable<string> keys) { Refresh(keys); }

        internal void UpdateOptions(Field f, List<string> options, int? selectIndex)
        {
            var dd = f as DropdownField;
            if (dd != null)
            {
                var before = O.GetPendingRaw(f.Key);
                var value = dd.SetOptions(options, selectIndex, before as string);
                O.ApplyChange(f, f.Key, value);
                // Like a ComboBox: OnChange only when the selection really changed. An OnChange
                // that calls UpdateDropdown would otherwise call itself until the stack ran out.
                if (!ValueConverter.LooseEquals(before, O.GetPendingRaw(f.Key))) O.RunChangeHandlers(f);
                Refresh(new[] { f.Key }, new[] { f });
                ShowToast(UiStrings.F(S.Refreshed, f.Label, dd.Options.Count), InfoKind.Info);
                KickAutoSave();
                return;
            }
            var ms = f as MultiSelectField;
            if (ms == null) return;
            ms.ReplaceOptions(options);
            Refresh(null, new[] { f });
            ShowToast(UiStrings.F(S.Refreshed, f.Label, options.Count), InfoKind.Info);
        }

        public void ShowToast(string message, InfoKind kind)
        {
            var l = _link;
            if (l == null || !l.IsOpen) return;
            l.Send(MessageTypes.Toast, new Dictionary<string, object>(StringComparer.Ordinal) { { "message", message ?? "" }, { "kind", kind.ToString().ToLowerInvariant() } });
        }

        public void BringToFront()
        {
            var l = _link;
            if (l != null && l.IsOpen) l.Send(MessageTypes.Focus, new Dictionary<string, object>());
        }

        public int Dialog(string title, string message, string[] buttons, int primary, TimeSpan timeout)
        {
            var l = _link;
            return l == null ? -1 : l.Dialog(title, message, buttons, primary, timeout);
        }

        // Closing

        internal void RequestClose() { CloseChecks(); }

        private void CloseChecks()
        {
            if (_closeSent) return;
            if (L.AutoSave) AutoSaveNow();
            if (!_forceClose && O.IsDirty && W.ConfirmClose)
            {
                int choice = Dialog(S.SaveChangesTitle, UiStrings.F(S.SaveChangesBody, O.ExtensionName), new[] { S.Cancel, S.DontSave, S.Save }, 2, Orchid.DialogTimeout);
                if (choice == 2)
                {
                    if (!TrySave()) return;
                }
                else if (choice != 1)
                {
                    return;
                }
            }
            SendClose();
        }

        private void SendClose()
        {
            if (_closeSent) return;
            SaveUiState();
            _closeSent = true;
            if (_autoSave != null) _autoSave.Change(Timeout.Infinite, Timeout.Infinite);
            if (_link != null) _link.Send(MessageTypes.Close, new Dictionary<string, object>());
        }

        private void SaveUiState()
        {
            var h = _hostState;
            if (h == null || _dialogOnly) return;
            try
            {
                var st = new UiState
                {
                    W = Json.Num(h, "w"),
                    H = Json.Num(h, "h"),
                    Zoom = Json.Num(h, "zoom"),
                    Maximized = Json.Bool(h, "max"),
                };
                string tabName;
                var tabId = Json.Str(h, "tab");
                st.Tab = tabId != null && _tabNameById.TryGetValue(tabId, out tabName) ? tabName : null;
                if (h.ContainsKey("x") && h.ContainsKey("y"))
                {
                    st.X = Json.Num(h, "x");
                    st.Y = Json.Num(h, "y");
                    st.HasPosition = true;
                    st.Scale = Json.Num(h, "scale");
                }
                O.SaveUiState(st);
            }
            catch (Exception ex) { O.LogDebug("Could not store window state: " + ex.Message); }
        }

        public void OnClosed(string reason, bool unexpected)
        {
            if (_autoSave != null) _autoSave.Dispose();
            if (unexpected && !_dialogOnly)
                O.LogError("The settings window closed unexpectedly: " + reason + ". Streamer.bot keeps running and saved settings are kept; run the action again to reopen it.");
            else if (unexpected)
                O.LogWarn("Dialog window failed: " + reason + ".");
            else
                O.LogDebug("Settings window closed (" + reason + ").");
            if (!_dialogOnly) O.OnWindowClosed(this);
            _closedEvent.Set();
        }

        // A dialog with no settings window open: a small host window just for it.
        internal static int StandaloneDialog(Orchid owner, string title, string message, string[] buttons, int primary, TimeSpan timeout)
        {
            var c = new WindowController(owner, true);
            try
            {
                if (!c.Start()) return -1;
                c._link.EndsAfterDialog = true;
                int result = c.Dialog(title, message, buttons, primary, timeout);
                c.Post(c.SendClose);
                c._closedEvent.WaitOne(TimeSpan.FromSeconds(5));
                return result;
            }
            catch (Exception ex)
            {
                owner.LogError("Dialog failed: " + ex.Message);
                return -1;
            }
        }
    }
}
