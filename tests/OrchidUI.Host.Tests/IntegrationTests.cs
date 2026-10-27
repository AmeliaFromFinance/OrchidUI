extern alias shim;
extern alias sandbox;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using OrchidUI.Host.Fields;
using OrchidUI.Host.Session;
using Xunit;
using FakeCph = sandbox::OrchidUI.Sandbox.FakeCph;
using Icons = shim::OrchidUI.Icons;
using Orchid = shim::OrchidUI.Orchid;

namespace OrchidUI.Host.Tests;

[CollectionDefinition("environment", DisableParallelization = true)]
public sealed class EnvironmentCollection;

// The real shim (with a fake CPH) and the real host window, over a real pipe and real loopback TCP.
[Collection("environment")]
public class IntegrationTests
{
    public static TheoryData<string> Transports => OperatingSystem.IsWindows() ? new() { "pipe", "tcp" } : new() { "tcp", "pipe" };

    [AvaloniaTheory]
    [MemberData(nameof(Transports))]
    public async Task Shim_and_host_open_edit_save_and_close(string transport)
    {
        var dir = Directory.CreateTempSubdirectory("orchid-it-").FullName;
        var vars = new Dictionary<string, string?>
        {
            ["ORCHIDUI_NO_LAUNCH"] = "1",
            ["ORCHIDUI_SESSION_DIR"] = dir,
            ["ORCHIDUI_TRANSPORT"] = transport,
        };
        var old = vars.Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
        foreach (var kv in vars) Environment.SetEnvironmentVariable(kv.Key, kv.Value);
        SessionClient? client = null;
        try
        {
            var cph = new FakeCph();
            var name = "ITEST" + transport.ToUpperInvariant();
            var ui = new Orchid(cph, name, "1.0.0").SetWindowSize(620, 520);
            var tab = ui.Tab("Shoutouts", Icons.Megaphone);
            tab.AddToggleSwitch("Enabled", "Shout out raiders automatically.", "enabled", true);
            tab.AddIntegerStepper("Minimum Raiders", "Smaller raids are ignored.", "minRaiders", 1, 1000, 5);
            tab.AddResponseBox("Message", "Variables: %user%", "message", "Go follow @%user%!").Required();

            var shown = Task.Run(() => ui.ShowUI());
            string? file = null;
            await Until(() => (file = Directory.GetFiles(dir, "session-*.json").FirstOrDefault()) is not null, "session file");

            var info = SessionInfo.Read(file!);
            Assert.False(File.Exists(file), "The host deletes the session file as it reads it.");
            Assert.Equal(transport, info.Transport);
            client = await SessionClient.ConnectAsync(info);
            int? finished = null;
            var driver = new SessionDriver(client, w => w.Show(), code => finished = code);
            client.Hello(info.Token);
            client.StartReading();
            await Until(() => driver.Window is not null && shown.IsCompleted, "window");
            Assert.True(ui.IsOpen);
            var w = driver.Window!;
            Assert.Contains("ITEST", w.Title);

            // Edit two settings with the keyboard, then fail validation by clearing the required one.
            var toggle = (ToggleSwitch)Wins.View<ToggleView>(w, "enabled").Inputs.Single();
            toggle.Focus();
            w.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            w.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            var box = (TextBox)Wins.View<TextView>(w, "message").Inputs.Single();
            box.Text = "";
            var message = Wins.View<TextView>(w, "message");
            await Until(() => message.ErrorText is { IsVisible: true }, "required error");
            Assert.Equal("1 setting(s) need attention", w.StatusText);

            box.Text = "Thanks @%user%";
            await Until(() => message.ErrorText is not { IsVisible: true } && w.StatusText.Contains("Unsaved"), "dirty status");

            // Ctrl+S saves through Streamer.bot's global variable, in the stored format.
            var cmd = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            w.KeyPressQwerty(PhysicalKey.S, cmd);
            w.KeyReleaseQwerty(PhysicalKey.S, cmd);
            await Until(() => (cph.Raw("orchid_" + name) ?? "").Contains("Thanks"), "saved settings");
            Assert.Equal("{\"enabled\":false,\"minRaiders\":5,\"message\":\"Thanks @%user%\"}", cph.Raw("orchid_" + name));
            await Until(() => w.ToastVisible, "saved toast");

            // Closing asks the shim, which saves the window state and tells the host to close.
            w.Close();
            await Until(() => finished is not null && !ui.IsOpen, "close");
            Assert.Equal(0, finished);
            Assert.False(w.IsVisible);
            var state = cph.Raw("orchid_" + name + ".window") ?? "";
            Assert.Contains("\"w\":620", state);
            Assert.Empty(cph.Logged("ERROR"));
        }
        finally
        {
            client?.Dispose();
            foreach (var kv in old) Environment.SetEnvironmentVariable(kv.Key, kv.Value);
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    private static async Task Until(Func<bool> condition, string what, int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Timed out waiting for " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }
}
