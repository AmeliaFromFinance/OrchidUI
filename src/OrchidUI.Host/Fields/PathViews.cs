using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OrchidUI.Host.Views;
using OrchidUI.Protocol;

namespace OrchidUI.Host.Fields;

// AddFilePicker, AddFolderPicker and AddImageInput: a path box, a Browse button that opens the
// system's file dialog here (so on Linux it's the desktop's own dialog, not Wine's), and for a file
// picker with onAction a second button. A picked path goes to the shim marked "native", and the shim
// stores it as Streamer.bot sees it (Z:\home\… under Wine). The image input also shows a preview the
// shim prepared.
internal sealed class PathView(FieldSpec spec, WindowContext ctx) : FieldView(spec, ctx)
{
    private TextBox _box = null!;
    private Button _browse = null!;
    private Button? _action;
    private Image? _preview;
    private TextBlock? _note;
    private string? _previewKey;

    private bool IsImage => Spec.Kind == FieldKinds.Image;
    private bool IsFolder => Spec.PropBool("folder");

    public override IEnumerable<Control> Inputs => _action is null ? [_box, _browse] : [_box, _browse, _action];

    internal TextBox Box => _box;
    internal Button Browse => _browse;
    internal Button? Action => _action;
    internal Image? Preview => _preview;

    // For tests: what Browse would pick, instead of a dialog.
    internal static Func<PathView, Task<string?>>? PickOverride;

    public override Control BuildControl()
    {
        _box = new TextBox
        {
            PlaceholderText = Spec.Placeholder ?? (IsImage ? Ctx.S("ImageHint", "Image URL or file path") : null),
            FontSize = P.FontSize,
        };
        Name(_box);
        _box.TextChanged += (_, _) => Changed(Spec.Key!, _box.Text ?? "");

        string browseText = Ctx.S("Browse", "Browse");
        _browse = new Button { Content = browseText, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
        Ui.Named(_browse, Spec.Label + ": " + (IsFolder ? Ctx.S("ChooseFolder", "Choose a folder") : Ctx.S("ChooseFile", "Choose a file")));
        _browse.Click += async (_, _) => await BrowseAsync();

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6 };
        row.Children.Add(_box);
        Grid.SetColumn(_browse, 1);
        row.Children.Add(_browse);
        var actionText = Spec.PropStr("actionText");
        if (actionText is not null)
        {
            _action = new Button { Content = actionText, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
            _action.Classes.Add("accent");
            Ui.Named(_action, Spec.Label + ": " + actionText, Spec.Description);
            _action.Click += (_, _) => Ctx.Sink.Send(MessageTypes.Click, new Dictionary<string, object>(StringComparer.Ordinal) { { "field", Spec.Id } });
            Grid.SetColumn(_action, 2);
            row.Children.Add(_action);
        }
        if (!IsImage) return row;

        _preview = new Image { MaxHeight = 120, MaxWidth = 240, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };
        Ui.Named(_preview, Spec.Label + " preview");
        _note = new TextBlock { Foreground = P["TextMuted"], FontSize = P.SmallFontSize, IsVisible = false };
        var previewFrame = new Border
        {
            Child = new StackPanel { Children = { _preview, _note } },
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        return new StackPanel { Children = { row, previewFrame } };
    }

    private async Task BrowseAsync()
    {
        string? path;
        try { path = PickOverride is not null ? await PickOverride(this) : await PickAsync(); }
        catch (Exception ex)
        {
            HostLog.Warn("file dialog: " + ex.Message);
            path = null;
            Ctx.Toast?.Invoke(Ctx.S("NoFileDialog", "The file dialog couldn't open here. Type the path instead."), "warning");
        }
        if (string.IsNullOrEmpty(path)) return;
        Picked(path);
    }

    // A path from the dialog: shown at once, sent as native for the shim to translate.
    internal void Picked(string path)
    {
        // The text box reports the new text later; it's this same change.
        Agree(Spec.Key!, path);
        using (Quiet()) _box.Text = path;
        Ctx.Sink.Send(MessageTypes.Change, new Dictionary<string, object>(StringComparer.Ordinal)
        {
            { "field", Spec.Id }, { "key", Spec.Key! }, { "value", path }, { "native", true },
        });
    }

    private async Task<string?> PickAsync()
    {
        var top = TopLevel.GetTopLevel(_box);
        var sp = top?.StorageProvider;
        if (sp is null || (IsFolder ? !sp.CanPickFolder : !sp.CanOpen))
        {
            Ctx.Toast?.Invoke(Ctx.S("NoFileDialog", "The file dialog couldn't open here. Type the path instead."), "warning");
            return null;
        }
        IStorageFolder? start = null;
        var startPath = Spec.PropStr("start");
        if (!string.IsNullOrEmpty(startPath))
        {
            try { start = await sp.TryGetFolderFromPathAsync(startPath); } catch (Exception) { start = null; }
        }
        if (IsFolder)
        {
            var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Ctx.S("ChooseFolder", "Choose a folder"), SuggestedStartLocation = start, AllowMultiple = false });
            return folders.FirstOrDefault()?.TryGetLocalPath();
        }
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Ctx.S("ChooseFile", "Choose a file"),
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter = Filters(Spec.PropStr("filter")),
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    // A WinForms-style filter: "Backup files (*.json)|*.json|All files (*.*)|*.*".
    internal static List<FilePickerFileType>? Filters(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return null;
        var parts = filter.Split('|');
        var list = new List<FilePickerFileType>();
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            var patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => p == "*.*" ? "*" : p).ToArray();
            if (patterns.Length == 0) continue;
            list.Add(new FilePickerFileType(parts[i].Trim().Length > 0 ? parts[i].Trim() : parts[i + 1]) { Patterns = patterns });
        }
        return list.Count > 0 ? list : null;
    }

    public override void SetValue(string key, object? value)
    {
        var s = value as string ?? "";
        if (_box.Text == s) return;
        using (Quiet()) _box.Text = s;
    }

    // A URL is fetched once typing pauses, not for every letter.
    internal static TimeSpan UrlDelay = TimeSpan.FromMilliseconds(500);
    private bool _previewFailed;
    private string? _display;

    public override void ApplyState(FieldState state)
    {
        base.ApplyState(state);
        if (!IsImage || _preview is null || _note is null) return;
        _display = state.Display;
        // No "image" in the state: the preview the host has is still right.
        if (state.ImageSource is not null) ShowPreview(state.Image);
        ShowNote();
    }

    private void ShowNote()
    {
        var text = !string.IsNullOrEmpty(_display) ? _display : _previewFailed ? Ctx.S("NoPreview", "Can't be previewed") : "";
        _note!.Text = text;
        _note.IsVisible = text.Length > 0;
    }

    private void ShowPreview(ImageData? image)
    {
        var key = image is null ? "" : image.Url ?? (image.Base64?.Length.ToString() + ":" + image.Base64?.GetHashCode());
        if (key == _previewKey) return;
        _previewKey = key;
        _previewFailed = false;
        _preview!.Source = null;
        _preview.IsVisible = false;
        if (image is null) return;
        void Load() => _ = Images.LoadAsync(image, bmp =>
        {
            if (_previewKey != key) return;
            _preview.Source = bmp;
            _preview.IsVisible = true;
        }, () =>
        {
            if (_previewKey != key) return;
            _previewFailed = true;
            ShowNote();
        }, 480, "Image preview");
        if (image.Url is null) { Load(); return; }
        _ = Task.Delay(UrlDelay).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (_previewKey == key) Load();
        }), TaskScheduler.Default);
    }
}
