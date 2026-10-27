using System.Diagnostics;
using OrchidUI.Protocol;

namespace OrchidUI.Host.Session;

// orchid-host --watch: for when Wine can't start a native program (start /unix is broken in
// Wine 9.5 to 10.0-rc2). Watches the session folder and opens a window for each new session.
internal static class WatchMode
{
    public static string DefaultDirectory()
    {
        var overridden = Environment.GetEnvironmentVariable("ORCHIDUI_SESSION_DIR");
        if (!string.IsNullOrWhiteSpace(overridden)) return overridden;
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrchidUI", "sessions");
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(runtime) && runtime.StartsWith('/')) return runtime.TrimEnd('/') + "/orchidui";
        var tmp = Environment.GetEnvironmentVariable("TMPDIR");
        if (IsPerUserTemp(tmp)) return tmp!.TrimEnd('/') + "/orchidui";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".cache", "orchidui", "sessions");
    }

    // macOS gives each user a private TMPDIR; Linux's /tmp is shared and doesn't count.
    internal static bool IsPerUserTemp(string? tmp)
    {
        if (string.IsNullOrEmpty(tmp) || !tmp.StartsWith('/')) return false;
        var t = tmp.TrimEnd('/');
        return t.Length > 0 && t != "/tmp" && t != "/var/tmp" && t != "/private/tmp";
    }

    public static int Run(string? dir, CancellationToken ct)
    {
        dir ??= DefaultDirectory();
        Directory.CreateDirectory(dir);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Console.WriteLine("orchid-host " + HostInfo.Version + " is watching " + dir + " for OrchidUI windows. Leave this running; Ctrl+C stops it.");
        HostLog.Info("watching " + dir);
        var started = new HashSet<string>(StringComparer.Ordinal);
        var self = Environment.ProcessPath ?? "orchid-host";
        while (!ct.IsCancellationRequested)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "session-*.json"))
                {
                    if (!started.Add(file)) continue;
                    var psi = new ProcessStartInfo(self) { UseShellExecute = false };
                    psi.ArgumentList.Add("--session");
                    psi.ArgumentList.Add(file);
                    using var p = Process.Start(psi);
                    HostLog.Info("opened a window for " + Path.GetFileName(file));
                }
                started.RemoveWhere(f => !File.Exists(f));
            }
            catch (IOException ex) { HostLog.Warn("watch: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { HostLog.Warn("watch: " + ex.Message); }
            ct.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(250));
        }
        return 0;
    }
}
