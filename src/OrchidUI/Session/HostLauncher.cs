using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OrchidUI
{
    internal sealed class LaunchResult
    {
        public bool Started;
        public string Error;

        // The binary was found but isn't a build this DLL trusts.
        public bool Untrusted;
        public Process Process;

        // Under Wine, start.exe returns at once; whether the host really runs shows up as a connection.
        public bool Detached;
    }

    // Finds orchid-host next to Streamer.bot, checks it's a build this DLL trusts, and starts it.
    internal static class HostLauncher
    {
        public static string FolderName = "OrchidUI";

        internal static string[] TrustedHashes = HostHashes.Known;

        // Where orchid-host writes its log; it mirrors HostLog.Directory in the host.
        public static string LogLocation()
        {
            if (OrchidPlatform.IsWindowsOs && !OrchidPlatform.IsWine)
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrchidUI", "orchid-host.log");
            var xdg = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
            return (string.IsNullOrEmpty(xdg) ? "~/.local/state" : xdg) + "/orchidui/orchid-host.log (~/Library/Logs/OrchidUI on macOS)";
        }

        public static string FindHost()
        {
            var overridden = OrchidPlatform.Env("ORCHIDUI_HOST");
            if (overridden != null) return overridden;
            string dir = Path.Combine(Orchid.StreamerBotDirectory, FolderName);
            // Under Wine the native Linux/macOS binary has no extension; on Windows it's the .exe.
            string name = OrchidPlatform.IsWindowsOs && !OrchidPlatform.IsWine ? "orchid-host.exe" : "orchid-host";
            return Path.Combine(dir, name);
        }

        public static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var hash = sha.ComputeHash(stream);
                var sb = new StringBuilder(64);
                foreach (var b in hash) sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        // null when the binary may be started, else why not.
        public static string Verify(string path, Action<string> warn)
        {
            bool untrusted;
            return Verify(path, warn, out untrusted);
        }

        public static string Verify(string path, Action<string> warn, out bool untrusted)
        {
            untrusted = false;
            if (!File.Exists(path)) return "orchid-host wasn't found at " + path + ". Unzip orchid-host for your OS into the OrchidUI folder next to Streamer.bot.exe.";
            if (TrustedHashes.Length == 0)
            {
                warn("Development build of OrchidUI: orchid-host at " + path + " is started without a checksum check.");
                return null;
            }
            string actual;
            try { actual = Sha256(path); }
            catch (Exception ex) { return "orchid-host couldn't be read for its checksum: " + ex.Message; }
            untrusted = !TrustedHashes.Contains(actual, StringComparer.OrdinalIgnoreCase);
            if (untrusted)
                return "orchid-host at " + path + " isn't a build this OrchidUI.dll trusts (SHA-256 " + actual + "). Download the matching release.";
            return null;
        }

        public static LaunchResult Launch(string hostPath, SessionFile session, Action<string> warn)
        {
            bool untrusted;
            var problem = Verify(hostPath, warn, out untrusted);
            if (problem != null) return new LaunchResult { Error = problem, Untrusted = untrusted };
            try
            {
                if (OrchidPlatform.IsWine)
                {
                    string unixHost = WinePaths.ToUnix(Path.GetFullPath(hostPath));
                    if (unixHost == null) return new LaunchResult { Error = "The Unix path of " + hostPath + " couldn't be worked out (winepath failed)." };
                    var psi = new ProcessStartInfo("start.exe", "/unix " + Quote(unixHost) + " --session " + Quote(session.HostPath))
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    var p = Process.Start(psi);
                    return new LaunchResult { Started = p != null, Process = p, Detached = true, Error = p == null ? "start /unix didn't start." : null };
                }

                var direct = new ProcessStartInfo(hostPath, "--session " + Quote(session.HostPath))
                {
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(hostPath)),
                };
                var process = Process.Start(direct);
                return new LaunchResult { Started = process != null, Process = process, Error = process == null ? "orchid-host didn't start." : null };
            }
            catch (Exception ex)
            {
                return new LaunchResult { Error = "orchid-host couldn't be started: " + ex.Message };
            }
        }

        private static string Quote(string s)
        {
            return "\"" + (s ?? "").Replace("\"", "\\\"") + "\"";
        }
    }
}
