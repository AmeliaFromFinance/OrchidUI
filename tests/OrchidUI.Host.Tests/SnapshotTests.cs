using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using SkiaSharp;
using Xunit;

namespace OrchidUI.Host.Tests;

// One picture per theme of the settings example, compared with a small per-pixel tolerance so
// anti-aliasing differences between Skia builds don't fail the run.
public class SnapshotTests
{
    public static TheoryData<string> Themes => new(Repo.ThemeNames);

    // Avalonia.Fonts.Inter's copy (Program.cs: WithInterFont), which every machine has.
    internal const string BundledInter = "fonts:Inter#Inter";

    [AvaloniaTheory]
    [MemberData(nameof(Themes))]
    public void Settings_window_in_each_theme(string theme)
    {
        // The pictures were taken on Linux; Skia's text rasterising differs a little per OS.
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Snapshots are compared on Linux only.");
        var spec = Repo.Spec("settings");
        spec.Theme = Repo.Theme(theme);
        // System fonts differ between machines, so use the bundled Inter by its resource name. Plain
        // "Inter" picks an installed copy first when there is one (Debian's fonts-inter, say).
        spec.Theme.FontFamily = spec.Theme.HeadingFontFamily = spec.Theme.MonoFontFamily = BundledInter;
        spec.Theme.FontFileData = null;
        spec.Theme.FontFileName = null;
        var (w, _) = Wins.Open(spec);
        w.Width = 1180;
        w.Height = 800;
        Wins.Pump();
        using var frame = w.CaptureRenderedFrame()!;
        using var stream = new MemoryStream();
        frame.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        Snapshots.Match("settings-" + theme, stream.ToArray());
    }
}

// tests/OrchidUI.Host.Tests/snapshots/<name>.verified.png is the accepted picture. A mismatch
// writes <name>.received.png beside it; ORCHIDUI_UPDATE_GOLDEN=1 accepts the new picture.
internal static class Snapshots
{
    public static string Directory => Path.Combine(Repo.Root, "tests", "OrchidUI.Host.Tests", "snapshots");

    public static void Match(string name, byte[] png)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var verified = Path.Combine(Directory, name + ".verified.png");
        var received = Path.Combine(Directory, name + ".received.png");
        if (Environment.GetEnvironmentVariable("ORCHIDUI_UPDATE_GOLDEN") == "1" || !File.Exists(verified))
        {
            File.WriteAllBytes(verified, png);
            File.Delete(received);
            if (Environment.GetEnvironmentVariable("CI") == "true")
                Assert.Fail(name + ".verified.png was missing; it has been written, commit it.");
            return;
        }
        var problem = TolerantPng.Compare(png, File.ReadAllBytes(verified));
        if (problem is null)
        {
            File.Delete(received);
            return;
        }
        File.WriteAllBytes(received, png);
        Assert.Fail(name + ": " + problem + ". See " + received);
    }
}

internal static class TolerantPng
{
    // A pixel may differ by up to 24 per channel; no more than 0.5 % of pixels may differ at all.
    // Returns null when the pictures match.
    public static string? Compare(byte[] received, byte[] verified)
    {
        using var a = SKBitmap.Decode(received);
        using var b = SKBitmap.Decode(verified);
        if (a is null || b is null) return "not a PNG";
        if (a.Width != b.Width || a.Height != b.Height) return $"size {a.Width}x{a.Height} vs {b.Width}x{b.Height}";
        long differing = 0;
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
            {
                var p = a.GetPixel(x, y);
                var q = b.GetPixel(x, y);
                var d = Math.Max(Math.Max(Math.Abs(p.Red - q.Red), Math.Abs(p.Green - q.Green)), Math.Max(Math.Abs(p.Blue - q.Blue), Math.Abs(p.Alpha - q.Alpha)));
                if (d > 24) return $"pixel {x},{y} differs by {d}";
                if (d > 0) differing++;
            }
        var share = (double)differing / (a.Width * a.Height);
        return share <= 0.005 ? null : $"{share:P2} of pixels differ";
    }
}
