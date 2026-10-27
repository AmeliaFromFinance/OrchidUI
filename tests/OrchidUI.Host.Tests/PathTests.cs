using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using OrchidUI.Host.Fields;
using OrchidUI.Host.Views;
using OrchidUI.Protocol;
using Xunit;

namespace OrchidUI.Host.Tests;

// File, folder and image pickers: a path from the dialog goes to the shim marked native (the shim
// translates it for Wine); typing sends the text; the image input shows the shim's preview.
public class PathTests
{
    private const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [AvaloniaFact]
    public void A_picked_file_is_sent_as_a_native_path_and_the_action_button_clicks()
    {
        var (w, sink) = Wins.Open(Repo.Spec("settings"));
        var view = Wins.View<PathView>(w, "backupFile");
        w.SelectTab(view.Spec.Tab);
        Wins.Pump();
        PathView.PickOverride = _ => Task.FromResult<string?>("/home/quietfern/HYPE TIMER Backups/2026-10-27.json");
        try
        {
            view.Browse.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Wins.Pump();
        }
        finally { PathView.PickOverride = null; }
        var change = sink.LastChange("backupFile")!;
        Assert.Equal("/home/quietfern/HYPE TIMER Backups/2026-10-27.json", Json.Str(change, "value"));
        Assert.True(Json.Bool(change, "native"));
        Assert.Equal("/home/quietfern/HYPE TIMER Backups/2026-10-27.json", view.Box.Text);

        // The shim sends back the Wine form; it's shown, not echoed.
        sink.Clear();
        w.ApplyPatch(new Dictionary<string, object> { { "values", new Dictionary<string, object> { { "backupFile", @"Z:\home\quietfern\HYPE TIMER Backups\2026-10-27.json" } } } });
        Wins.Pump();
        Assert.Equal(@"Z:\home\quietfern\HYPE TIMER Backups\2026-10-27.json", view.Box.Text);
        Assert.Empty(sink.Of(MessageTypes.Change));

        // Typing is an ordinary change; Load is a click.
        view.Box.Text = @"C:\backups\old.json";
        Wins.Pump();
        Assert.False(Json.Bool(sink.LastChange("backupFile")!, "native"));
        view.Action!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wins.Pump();
        Assert.Equal(view.Spec.Id, Json.Str(sink.Of(MessageTypes.Click).Last(), "field"));
    }

    [AvaloniaFact]
    public void Filters_are_read_like_WPF_filters()
    {
        var filters = PathView.Filters("Backup files (*.json)|*.json|Images|*.png; *.jpg|All files (*.*)|*.*")!;
        Assert.Equal(new[] { "Backup files (*.json)", "Images", "All files (*.*)" }, filters.Select(f => f.Name));
        Assert.Equal(new[] { "*.png", "*.jpg" }, filters[1].Patterns);
        Assert.Equal(new[] { "*" }, filters[2].Patterns);
        Assert.Null(PathView.Filters(null));
        Assert.Null(PathView.Filters("no pipe here"));
    }

    [AvaloniaFact]
    public void The_image_input_shows_the_preview_from_the_shim_and_its_note()
    {
        var (w, _) = Wins.Open(Repo.Spec("settings"));
        var view = Wins.View<PathView>(w, "logo");
        w.SelectTab(view.Spec.Tab);
        Wins.Pump();
        Assert.False(view.Preview!.IsVisible);
        w.ApplyPatch(new Dictionary<string, object>
        {
            { "states", new object[] { new Dictionary<string, object> { { "id", view.Spec.Id }, { "image", new Dictionary<string, object> { { "mime", "image/png" }, { "data", OnePixelPng } } } } } },
        });
        Assert.True(SpinUntil(() => view.Preview.IsVisible));
        Assert.NotNull(view.Preview.Source);

        w.ApplyPatch(new Dictionary<string, object>
        {
            { "states", new object[] { new Dictionary<string, object> { { "id", view.Spec.Id }, { "image", null! }, { "display", "File not found" } } } },
        });
        Wins.Pump();
        Assert.False(view.Preview.IsVisible);
        Assert.Contains(Wins.All<TextBlock>(view.Row), t => t.Text == "File not found" && t.IsVisible);
    }

    [AvaloniaFact]
    public void A_state_without_an_image_keeps_the_preview_and_a_failed_preview_keeps_its_note()
    {
        var (w, _) = Wins.Open(Repo.Spec("settings"));
        var view = Wins.View<PathView>(w, "logo");
        w.SelectTab(view.Spec.Tab);
        Wins.Pump();
        Dictionary<string, object> State(params (string, object?)[] extra)
        {
            var d = new Dictionary<string, object> { { "id", view.Spec.Id } };
            foreach (var (k, v) in extra) d[k] = v!;
            return d;
        }
        void Apply(Dictionary<string, object> state) => w.ApplyPatch(new Dictionary<string, object> { { "states", new object[] { state } } });

        Apply(State(("image", new Dictionary<string, object> { { "mime", "image/png" }, { "data", OnePixelPng } })));
        Assert.True(SpinUntil(() => view.Preview!.IsVisible));
        // Only dirty changed, so the shim leaves the preview out.
        Apply(State(("dirty", true)));
        Wins.Pump();
        Assert.True(view.Preview!.IsVisible);
        Assert.NotNull(view.Preview.Source);

        // Bytes that aren't an image: the note stays through later patches.
        Apply(State(("image", new Dictionary<string, object> { { "mime", "image/png" }, { "data", Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }) } })));
        var note = () => Wins.All<TextBlock>(view.Row).FirstOrDefault(t => t.Text == "Can't be previewed" && t.IsVisible);
        Assert.True(SpinUntil(() => note() is not null));
        Apply(State(("dirty", false)));
        Wins.Pump();
        Assert.NotNull(note());
        Assert.False(view.Preview.IsVisible);
    }

    [AvaloniaFact]
    public void A_url_is_fetched_once_typing_stops()
    {
        var (w, _) = Wins.Open(Repo.Spec("settings"));
        var view = Wins.View<PathView>(w, "logo");
        w.SelectTab(view.Spec.Tab);
        Wins.Pump();
        var fetched = new List<string>();
        Images.FetchOverride = url => { lock (fetched) fetched.Add(url); return Task.FromResult<byte[]?>(Convert.FromBase64String(OnePixelPng)); };
        PathView.UrlDelay = TimeSpan.FromMilliseconds(150);
        try
        {
            foreach (var url in new[] { "https://e", "https://ex", "https://example.com/l", "https://example.com/logo.png" })
            {
                w.ApplyPatch(new Dictionary<string, object> { { "states", new object[] { new Dictionary<string, object> { { "id", view.Spec.Id }, { "image", new Dictionary<string, object> { { "url", url } } } } } } });
                Wins.Pump();
            }
            Assert.True(SpinUntil(() => view.Preview!.IsVisible));
            lock (fetched) Assert.Equal(new[] { "https://example.com/logo.png" }, fetched);
        }
        finally
        {
            Images.FetchOverride = null;
            PathView.UrlDelay = TimeSpan.FromMilliseconds(500);
        }
    }

    [AvaloniaFact]
    public void A_huge_picture_in_a_small_file_isnt_decoded()
    {
        // 8000x8000 grey: 64 MB of pixels in a ~60 KB PNG. Refused from its header alone (the
        // IDAT here holds one row, so decoding it would fail differently).
        var bomb = Png(8000, 8000, fake: true);
        Assert.Equal((8000L, 8000L), Images.PixelSize(bomb));
        Assert.Null(Images.Decode(bomb, 480));
        Assert.Null(Images.Decode(Png(100, 600_000, fake: true), 480));
        // Big but reasonable: scaled to where it's shown.
        Assert.Equal(480, Images.Decode(Png(2000, 1500), 480)!.PixelSize.Width);
        // Small ones stay as they are, an icon too.
        Assert.Equal(1, Images.Decode(Convert.FromBase64String(OnePixelPng), 480)!.PixelSize.Width);
        var ico = Ico(Convert.FromBase64String(OnePixelPng));
        Assert.Equal((1L, 1L), Images.PixelSize(ico));
        Assert.Equal(1, Images.Decode(ico, 480)!.PixelSize.Width);
        Assert.Equal((8000L, 8000L), Images.PixelSize(Ico(bomb)));
        // A size that can't be read: not drawn.
        Assert.Null(Images.Decode(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3 }, 480));
    }

    // An ICO holding one PNG.
    private static byte[] Ico(byte[] png)
    {
        var b = new byte[6 + 16];
        b[2] = 1; b[4] = 1;
        b[6] = 1; b[7] = 1;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(6 + 8), png.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(6 + 12), b.Length);
        return b.Concat(png).ToArray();
    }

    private static byte[] Png(int width, int height, bool fake = false)
    {
        static byte[] Chunk(string type, byte[] data)
        {
            var ms = new MemoryStream();
            var len = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
            ms.Write(len);
            var td = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            ms.Write(td);
            var crc = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(td));
            ms.Write(crc);
            return ms.ToArray();
        }
        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; // bit depth, grey
        var raw = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(raw, System.IO.Compression.CompressionLevel.SmallestSize, true))
        {
            var line = new byte[width + 1];
            for (int y = 0; y < (fake ? 1 : height); y++) z.Write(line);
        }
        return new byte[] { 0x89, 0x50, 0x4E, 0x47, 13, 10, 26, 10 }
            .Concat(Chunk("IHDR", ihdr)).Concat(Chunk("IDAT", raw.ToArray())).Concat(Chunk("IEND", [])).ToArray();
    }

    private static uint Crc(byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (var b in data)
        {
            c ^= b;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFF;
    }

    private static bool SpinUntil(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until)
        {
            Wins.Pump();
            if (done()) return true;
            Thread.Sleep(20);
        }
        return false;
    }
}

// The More menu: Import and Export pick the file here and send it native; Reset all and About are
// commands the shim answers.
public class MoreMenuTests
{
    [AvaloniaFact]
    public void The_more_menu_offers_what_the_layout_allows()
    {
        var spec = Repo.Spec("settings");
        var (w, sink) = Wins.Open(spec);
        Assert.NotNull(w.MoreButton);
        var items = ((Avalonia.Controls.MenuFlyout)w.MoreButton!.Flyout!).ItemsSource!.Cast<Avalonia.Controls.MenuItem>().ToList();
        Assert.Equal(new[] { "Import settings", "Export settings", "Reset to defaults", "About" }, items.Select(i => (string)i.Header!));

        MainWindow.FileDialogOverride = kind => Task.FromResult<string?>(kind == "import" ? "/home/quietfern/in.json" : "/home/quietfern/out.json");
        try
        {
            foreach (var i in items) i.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
            Wins.Pump();
        }
        finally { MainWindow.FileDialogOverride = null; }
        var commands = sink.Of(MessageTypes.Command).ToList();
        Assert.Equal(new[] { Commands.Import, Commands.Export, Commands.ResetAll, Commands.About }, commands.Select(c => Json.Str(c, "name")));
        Assert.Equal("/home/quietfern/in.json", Json.Str(commands[0], "path"));
        Assert.True(Json.Bool(commands[0], "native"));
        Assert.Equal("/home/quietfern/out.json", Json.Str(commands[1], "path"));

        foreach (var flag in new[] { "showImportExport", "showResetAll", "showAbout" }) spec.Layout[flag] = false;
        var (hidden, _) = Wins.Open(spec);
        Assert.Null(hidden.MoreButton);
    }

    [AvaloniaFact]
    public void A_long_footer_is_cut_before_the_buttons()
    {
        var spec = Repo.Spec("settings");
        spec.Footer = "Saved values are written to " + string.Concat(Enumerable.Repeat("/home/quietfern/.local/share/Streamer.bot", 6));
        var (w, _) = Wins.Open(spec);
        Wins.Pump();
        var footer = w.FooterText!;
        static double Left(Avalonia.Visual v)
        {
            double x = 0;
            for (Avalonia.Visual? c = v; c is not null && c is not Window; c = c.GetVisualParent()) x += c.Bounds.X;
            return x;
        }
        var footerRight = Left(footer) + footer.Bounds.Width;
        var buttonsLeft = Left(w.MoreButton!);
        Assert.True(footerRight <= buttonsLeft, footerRight + " > " + buttonsLeft);
        Assert.True(footer.Bounds.Width > 50);
    }
}
