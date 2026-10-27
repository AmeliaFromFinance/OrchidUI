using Avalonia;
using OrchidUI.Host.Session;
using OrchidUI.Host.Views;
using OrchidUI.Protocol;
using Xunit;

namespace OrchidUI.Host.Tests;

public class RegressionTests
{
    private static string SessionJson() => Json.Serialize(new Dictionary<string, object>
    {
        { "protocol", ProtocolInfo.Major }, { "id", "abc123" }, { "transport", "tcp" }, { "pipe", null! }, { "port", 40000 },
        { "token", new string('a', 64) }, { "expiresAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 30000 },
    }, false);

    [Fact]
    public void A_readable_session_file_in_a_folder_others_can_enter_is_refused_and_deleted()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix permissions only.");
        // /tmp is world-traversable, so nothing above this folder is private.
        var dir = Path.Combine("/tmp", "orchid-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.SetUnixFileMode(dir, (UnixFileMode)0b111_101_101);
        var file = Path.Combine(dir, "session-abc123.json");
        File.WriteAllText(file, SessionJson());
        File.SetUnixFileMode(file, (UnixFileMode)0b110_100_100);
        try
        {
            var ex = Assert.Throws<InvalidDataException>(() => SessionInfo.Read(file));
            Assert.Contains("isn't private", ex.Message);
            Assert.False(File.Exists(file), "A refused token must still be burned.");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_readable_session_file_inside_a_private_folder_is_accepted()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix permissions only.");
        var dir = Path.Combine("/tmp", "orchid-private-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var inner = Path.Combine(dir, "orchidui");
        Directory.CreateDirectory(inner);
        File.SetUnixFileMode(inner, (UnixFileMode)0b111_101_101); // what Wine makes: 0755 inside a 0700 parent
        var file = Path.Combine(inner, "session-abc123.json");
        File.WriteAllText(file, SessionJson());
        File.SetUnixFileMode(file, (UnixFileMode)0b110_100_100);
        try
        {
            var info = SessionInfo.Read(file);
            Assert.Equal(40000, info.Port);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Watch_mode_only_counts_a_per_user_TMPDIR_as_private()
    {
        Assert.True(WatchMode.IsPerUserTemp("/var/folders/ab/xyz/T/"));
        Assert.False(WatchMode.IsPerUserTemp("/tmp/"));
        Assert.False(WatchMode.IsPerUserTemp(null));
    }

    [Fact]
    public void A_window_saved_on_a_200_percent_second_screen_reopens_there()
    {
        // Primary 2560x1440 at 100 %, secondary 3840x2160 at 200 % to its right. In DIP the
        // secondary spans x 1280..3200, overlapping the primary's 0..2560.
        var screens = new List<(PixelRect, double)>
        {
            (new PixelRect(0, 0, 2560, 1440), 1.0),
            (new PixelRect(2560, 0, 3840, 2160), 2.0),
        };
        // Saved from physical x=3000 on the secondary: DIP 1500.
        Assert.Equal(new PixelPoint(3000, 200), MainWindow.ChoosePosition(screens, 1500, 100, 800, 600, 2.0));
        // With no saved scale, the first screen that fits wins.
        Assert.Equal(new PixelPoint(1500, 100), MainWindow.ChoosePosition(screens, 1500, 100, 800, 600, 0));
        // Off every screen: centre instead.
        Assert.Null(MainWindow.ChoosePosition(screens, -5000, -5000, 800, 600, 1.0));
    }
}
