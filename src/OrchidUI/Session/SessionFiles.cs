using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OrchidUI.Protocol;

namespace OrchidUI
{
    // Where session files go, as seen from Streamer.bot (WindowsPath) and from orchid-host (HostPath).
    internal sealed class SessionDirectory
    {
        public string WindowsPath;
        public string HostPath;

        // Set when the folder may not be private; logged once per window.
        public string Warning;

        internal static bool IsPerUserTemp(string tmp)
        {
            if (string.IsNullOrEmpty(tmp) || !tmp.StartsWith("/", StringComparison.Ordinal)) return false;
            var t = tmp.TrimEnd('/');
            return t.Length > 0 && t != "/tmp" && t != "/var/tmp" && t != "/private/tmp";
        }

        // Some Wine builds (Proton's, as Bottles runs them) don't pass XDG_RUNTIME_DIR on. It is
        // /run/user/<uid> wherever systemd-logind made it (private, 0700): the uid comes from
        // /proc/self/status, read through Wine's Z: drive. null when either can't be seen.
        internal static string RuntimeDirFromUid(Func<string, string> readFile, Func<string, bool> dirExists)
        {
            string status;
            try { status = readFile("/proc/self/status"); }
            catch (Exception) { return null; }
            if (string.IsNullOrEmpty(status)) return null;
            foreach (var line in status.Split('\n'))
            {
                if (!line.StartsWith("Uid:", StringComparison.Ordinal)) continue;
                var fields = line.Substring(4).Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                int uid;
                if (fields.Length == 0 || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out uid)) return null;
                var dir = "/run/user/" + uid.ToString(CultureInfo.InvariantCulture);
                try { return dirExists(dir) ? dir : null; }
                catch (Exception) { return null; }
            }
            return null;
        }

        // How the Unix side is seen from inside Wine (tests replace them).
        internal static Func<string, string> ReadUnixFile = unixPath => File.ReadAllText(WinePaths.FromUnix(unixPath));
        internal static Func<string, bool> UnixDirExists = unixPath => Directory.Exists(WinePaths.FromUnix(unixPath));

        public static SessionDirectory Resolve()
        {
            string warning = null;
            var overridden = OrchidPlatform.Env("ORCHIDUI_SESSION_DIR");
            if (OrchidPlatform.IsWine)
            {
                // A Unix folder the native host can read: XDG_RUNTIME_DIR is per-user and 0700.
                string unix = overridden;
                if (unix == null)
                {
                    // The DLL can't set Unix permissions from inside Wine, so the folder must sit in
                    // one other users can't enter: XDG_RUNTIME_DIR (Linux) or the per-user TMPDIR
                    // (macOS). orchid-host refuses a readable session file anywhere else.
                    var runtime = OrchidPlatform.Env("XDG_RUNTIME_DIR");
                    var tmp = OrchidPlatform.Env("TMPDIR");
                    var home = OrchidPlatform.Env("HOME");
                    if (runtime == null || !runtime.StartsWith("/", StringComparison.Ordinal)) runtime = RuntimeDirFromUid(ReadUnixFile, UnixDirExists);
                    if (runtime != null && runtime.StartsWith("/", StringComparison.Ordinal)) unix = runtime.TrimEnd('/') + "/orchidui";
                    else if (IsPerUserTemp(tmp)) unix = tmp.TrimEnd('/') + "/orchidui";
                    else if (home != null && home.StartsWith("/", StringComparison.Ordinal))
                    {
                        unix = home.TrimEnd('/') + "/.cache/orchidui/sessions";
                        warning = "Neither XDG_RUNTIME_DIR nor a per-user TMPDIR is visible inside Wine, so session files go to " + unix
                            + ", which may be readable by other users; orchid-host refuses them unless that folder (or one above it) is private. "
                            + "Run 'orchid-host --watch' once (it makes the folder private) or set ORCHIDUI_SESSION_DIR to a folder only you can open.";
                    }
                }
                if (unix != null && unix.StartsWith("/", StringComparison.Ordinal))
                    return new SessionDirectory { HostPath = unix, WindowsPath = WinePaths.FromUnix(unix), Warning = warning };

                // Last resort: the prefix's own AppData, translated by winepath.
                var win = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrchidUI", "sessions");
                Directory.CreateDirectory(win);
                return new SessionDirectory { WindowsPath = win, HostPath = WinePaths.ToUnix(win) };
            }

            if (OrchidPlatform.IsWindowsOs)
            {
                var dir = overridden ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrchidUI", "sessions");
                return new SessionDirectory { WindowsPath = dir, HostPath = dir };
            }

            // Native Unix (tests, development): same layout the Wine case uses.
            var xdg = OrchidPlatform.Env("XDG_RUNTIME_DIR");
            var path = overridden ?? (xdg != null ? xdg.TrimEnd('/') + "/orchidui" : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "orchidui", "sessions"));
            return new SessionDirectory { WindowsPath = path, HostPath = path };
        }
    }

    // The one-time file that tells orchid-host how to connect. It is 0600 where the OS allows it
    // and deleted after the handshake; the token expires after TokenLifetimeSeconds regardless.
    internal sealed class SessionFile
    {
        // How long the token is valid (tests shorten it).
        internal static TimeSpan Lifetime = TimeSpan.FromSeconds(ProtocolInfo.TokenLifetimeSeconds);

        public string Id;
        public string Transport;
        public string Pipe;
        public int Port;
        public string Token;
        public int ShimPid;
        public long ExpiresAt;
        public string WindowsPath;
        public string HostPath;

        public Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "protocol", ProtocolInfo.Major },
                { "id", Id },
                { "transport", Transport },
                { "pipe", Pipe },
                { "port", Port },
                { "token", Token },
                { "shimPid", ShimPid },
                { "expiresAt", ExpiresAt },
            };
        }

        public static SessionFile Write(SessionDirectory dir, string id, string transport, string pipe, int port, string token)
        {
            Directory.CreateDirectory(dir.WindowsPath);
            RestrictDirectory(dir.WindowsPath);
            var file = new SessionFile
            {
                Id = id,
                Transport = transport,
                Pipe = pipe,
                Port = port,
                Token = token,
                ShimPid = System.Diagnostics.Process.GetCurrentProcess().Id,
                ExpiresAt = DateTimeOffset.UtcNow.Add(Lifetime).ToUnixTimeMilliseconds(),
                WindowsPath = Path.Combine(dir.WindowsPath, "session-" + id + ".json"),
            };
            file.HostPath = dir.HostPath == dir.WindowsPath
                ? file.WindowsPath
                : dir.HostPath.TrimEnd('/') + "/session-" + id + ".json";
            var tmp = file.WindowsPath + ".tmp";
            using (var stream = CreatePrivate(tmp))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(Json.Serialize(file.ToJson(), false));
            }
            File.Move(tmp, file.WindowsPath);
            return file;
        }

        public void Delete()
        {
            try { if (File.Exists(WindowsPath)) File.Delete(WindowsPath); }
            catch { }
        }

        private static FileStream CreatePrivate(string path)
        {
#if NETFRAMEWORK
            return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
#else
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            return new FileStream(path, options);
#endif
        }

        private static void RestrictDirectory(string path)
        {
#if !NETFRAMEWORK
            if (!OperatingSystem.IsWindows())
            {
                try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
#endif
        }

        internal static string NewId()
        {
            return Tokens.New().Substring(0, 12);
        }

        internal static string Describe(long unixMs)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToString("u", CultureInfo.InvariantCulture);
        }
    }
}
