using System;

namespace OrchidUI
{
    public enum InfoKind { Info, Success, Warning, Danger }

    // Custom extends the window into the title bar on Windows; everywhere else, and
    // for Auto, the OS draws the title bar.
    public enum ChromeMode { Auto, Custom, Native }

    // Ignored: the host uses the GPU when it can and falls back by itself.
    public enum RenderingMode { Auto, Hardware, Software }

    // Mica/Acrylic need Windows 11 22H2+. Auto = Mica there, Solid everywhere else (Linux, macOS).
    public enum BackdropMode { Auto, Mica, MicaAlt, Acrylic, Solid }

    // Auto = Sidebar with 2+ tabs, None with a single tab.
    public enum NavigationStyle { Auto, Sidebar, Tabs, None }

    public enum PaneMode { Auto, Expanded, Compact }

    public enum Density { Compact, Normal, Comfortable }

    public sealed class WindowOptions
    {
        public string Title { get; set; }
        public string IconPath { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double MinWidth { get; set; }
        public double MinHeight { get; set; }
        public double MaxWidth { get; set; }
        public double MaxHeight { get; set; }
        public double? Left { get; set; }
        public double? Top { get; set; }
        public bool Resizable { get; set; }
        public bool CanMinimize { get; set; }
        public bool CanMaximize { get; set; }
        public bool StartMaximized { get; set; }
        public bool Topmost { get; set; }
        public bool ShowInTaskbar { get; set; }
        public bool RememberBounds { get; set; }
        public bool ConfirmClose { get; set; }
        public bool Rounded { get; set; }
        public double Scale { get; set; }
        public bool AllowZoom { get; set; }
        public ChromeMode Chrome { get; set; }
        public RenderingMode Rendering { get; set; }
        public BackdropMode Backdrop { get; set; }
        // Ignored; there is no separate Wine rendering path.
        public bool? Compatibility { get; set; }

        public WindowOptions()
        {
            Width = 1180;
            Height = 800;
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
            Resizable = true;
            CanMinimize = true;
            CanMaximize = true;
            ShowInTaskbar = true;
            RememberBounds = true;
            ConfirmClose = true;
            Rounded = true;
            Scale = 1.0;
            AllowZoom = true;
        }

        public WindowOptions Clone() { return (WindowOptions)MemberwiseClone(); }
    }

    public sealed class LayoutOptions
    {
        public NavigationStyle Navigation { get; set; }
        public PaneMode Pane { get; set; }
        public double PaneWidth { get; set; }
        public double CompactPaneBelow { get; set; }
        public double ContentMaxWidth { get; set; }
        public double ContentPadding { get; set; }
        public Density Density { get; set; }
        public double InputWidth { get; set; }
        public double DropdownWidth { get; set; }
        public double SliderWidth { get; set; }
        public string StartTab { get; set; }
        public bool RememberTab { get; set; }
        public bool ShowPageTitle { get; set; }
        public bool ShowSearch { get; set; }
        public bool ShowImportExport { get; set; }
        public bool ShowResetAll { get; set; }
        public bool ShowAbout { get; set; }
        public bool ShowStatus { get; set; }
        public bool ShowDiscard { get; set; }
        public bool ShowSave { get; set; }
        public bool ShowSaveAndClose { get; set; }
        public bool ShowDirtyMarkers { get; set; }
        public bool ShowTitleBarName { get; set; }
        public bool Animations { get; set; }
        public bool AutoSave { get; set; }
        public int AutoSaveDelayMs { get; set; }
        public double ToastSeconds { get; set; }

        public LayoutOptions()
        {
            PaneWidth = 280;
            CompactPaneBelow = 1000;
            ContentMaxWidth = 1000;
            ContentPadding = 36;
            Density = Density.Normal;
            InputWidth = 280;
            DropdownWidth = 240;
            SliderWidth = 220;
            RememberTab = true;
            ShowPageTitle = true;
            ShowSearch = true;
            ShowImportExport = true;
            ShowResetAll = true;
            ShowAbout = true;
            ShowStatus = true;
            ShowDiscard = true;
            ShowSave = true;
            ShowSaveAndClose = true;
            ShowDirtyMarkers = true;
            ShowTitleBarName = true;
            Animations = true;
            AutoSaveDelayMs = 600;
            ToastSeconds = 3.2;
        }

        public LayoutOptions Clone() { return (LayoutOptions)MemberwiseClone(); }
    }

    // Every built-in piece of text, so the UI can be translated. {0} is a placeholder.
    public sealed class UiStrings
    {
        public string WindowTitle = "{0} — Settings";
        public string Save = "Save";
        public string SaveAndClose = "Save & close";
        public string Discard = "Discard";
        public string Done = "Done";
        public string UnsavedChanges = "Unsaved changes";
        public string AllSaved = "All changes saved";
        public string SavedAt = "Saved at {0}";
        public string AutoSaved = "Changes save automatically";
        public string NeedsAttention = "{0} setting(s) need attention";
        public string FixBeforeSaving = "Fix the highlighted settings before saving.";
        public string SettingsSaved = "Settings saved";
        public string ChangesDiscarded = "Changes discarded";
        public string Search = "Search settings";
        public string SearchTooltip = "Search all settings (Ctrl+F)";
        public string NoResults = "No settings match “{0}”";
        public string More = "More";
        public string Import = "Import settings";
        public string Export = "Export settings";
        public string ResetAll = "Reset to defaults";
        public string About = "About";
        public string CollapseNav = "Collapse / expand navigation";
        public string ResetAllTitle = "Reset to defaults?";
        public string ResetAllBody = "Every setting of **{0}** goes back to its default value.\n\nNothing is stored until you click Save.";
        public string ResetAllDone = "Defaults restored. Click Save to keep them.";
        public string Reset = "Reset";
        public string ImportDoneTitle = "Import complete";
        public string ImportDoneBody = "Loaded **{0}** settings from the file.\n\nReview them and click Save to keep them.";
        // With auto-save on, the reset and the import are saved straight away.
        public string ResetAllBodyAutoSave = "Every setting of **{0}** goes back to its default value, and is saved.";
        public string ResetAllDoneAutoSave = "Defaults restored and saved.";
        public string ImportDoneBodyAutoSave = "Loaded and saved **{0}** settings from the file.";
        public string ExportDone = "Exported to {0}";
        public string JsonFiles = "JSON (*.json)|*.json";
        public string AllFiles = "All files (*.*)|*.*";
        public string SaveChangesTitle = "Save your changes?";
        public string SaveChangesBody = "You have unsaved changes in **{0}**.";
        public string DontSave = "Don't save";
        public string Cancel = "Cancel";
        public string Ok = "OK";
        public string Yes = "Yes";
        public string No = "No";
        public string Close = "Close";
        public string Minimize = "Minimize";
        public string Maximize = "Maximize";
        public string Restore = "Restore";
        public string ErrorTitle = "Something went wrong";
        public string ErrorBody = "{0}\n\nDetails were written to the Streamer.bot log.";
        public string ResetToDefault = "Reset to default";
        public string CopyKey = "Copy key ({0})";
        public string Copied = "Copied to the clipboard";
        public string Copy = "Copy";
        public string On = "On";
        public string Off = "Off";
        public string Refresh = "Refresh list";
        public string Refreshed = "Refreshed “{0}”: {1} items";
        public string Remove = "Remove";
        public string MoveUp = "Move up";
        public string MoveDown = "Move down";
        public string Increase = "Increase";
        public string Decrease = "Decrease";
        public string EnableDisable = "Enable / disable";
        public string PickColor = "Pick a colour";
        public string Browse = "Browse";
        public string ChooseFile = "Choose a file";
        public string ChooseFolder = "Choose a folder";
        public string ImageHint = "Image URL or file path";
        public string NoFileDialog = "The file dialog couldn't open here. Type the path instead.";
        public string NoPreview = "Can't be previewed";
        public string FileNotFound = "File not found";
        public string TooLargeToPreview = "Too large to preview (over 512 KB)";
        public string LoadingPreview = "Loading preview…";
        public string AddItem = "Add item";
        public string AddRow = "Add row";
        public string NoEntries = "No entries yet.";
        public string AddPlaceholder = "Add…";
        public string NothingSelected = "Nothing selected yet.";
        public string NoneSelected = "None selected";
        public string CountSelected = "{0} selected";
        public string Empty = "Empty";
        public string ItemCount = "{0} item(s)";
        public string Item = "item";
        public string Row = "row";
        public string Expand = "Show";
        public string Collapse = "Hide";
        public string RowCount = "{0} row(s)";
        public string Required = "This setting is required.";
        public string Days = "Days";
        public string Hours = "Hours";
        public string Minutes = "Minutes";
        public string Seconds = "Seconds";
        public string DayUnit = "d";
        public string HourUnit = "h";
        public string MinuteUnit = "m";
        public string SecondUnit = "s";
        public string Unsaved = "Unsaved change";
        public string CopyDiagnostics = "Copy diagnostics";
        public string Zoom = "Zoom {0}%";

        public UiStrings Clone() { return (UiStrings)MemberwiseClone(); }

        internal static string F(string format, params object[] args)
        {
            try { return string.Format(format ?? "", args); }
            catch { return format ?? ""; }
        }
    }
}
