using System;
using System.Globalization;
using System.IO;

namespace OrchidUI
{
    // What Streamer.bot is running on. Wine is recognised by the registry key
    // every Wine prefix has, or by Wine's environment variables.
    public static class OrchidPlatform
    {
        private static readonly object ProbeLock = new object();
        private static bool _probed;
        private static bool _wine;
        private static int _build = -1;

        public static bool IsWine { get { Probe(); return _wine; } }

        // "unknown" under Wine (its version string isn't read), else null.
        public static string WineVersion { get { Probe(); return _wine ? "unknown" : null; } }

        // Linux or macOS when running under Wine (from the host's environment), else null.
        public static string HostSystem
        {
            get
            {
                if (!IsWine) return null;
                var home = Env("HOME") ?? "";
                return home.StartsWith("/Users/", StringComparison.Ordinal) ? "macOS" : "Linux";
            }
        }

        public static bool IsProton
        {
            get { return IsWine && (Env("STEAM_COMPAT_DATA_PATH") != null || Env("PROTON_VERSION") != null); }
        }

        public static int WindowsBuild
        {
            get
            {
                if (_build >= 0) return _build;
                _build = 0;
                if (!IsWindowsOs) return _build;
                try
                {
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    {
                        int b;
                        var v = key == null ? null : key.GetValue("CurrentBuildNumber") as string;
                        if (v != null && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out b)) _build = b;
                    }
                }
                catch { }
                return _build;
            }
        }

        public static bool IsWindows11 { get { return !IsWine && WindowsBuild >= 22000; } }
        public static bool SupportsBackdrop { get { return !IsWine && WindowsBuild >= 22621; } }

        // There is no compatibility rendering; this only reports Wine.
        public static bool CompatibilityDefault { get { return IsWine; } }

        public static string Describe()
        {
            if (IsWine)
            {
                string s = "Wine";
                if (!string.IsNullOrEmpty(HostSystem)) s += " on " + HostSystem;
                if (IsProton) s += " (Proton)";
                return s;
            }
            if (!IsWindowsOs) return Environment.OSVersion.Platform == PlatformID.Unix ? "Unix (.NET " + Environment.Version + ")" : Environment.OSVersion.ToString();
            int b = WindowsBuild;
            return (b >= 22000 ? "Windows 11" : b >= 10240 ? "Windows 10" : "Windows") + (b > 0 ? " (build " + b + ")" : "");
        }

        internal static bool IsWindowsOs
        {
            get { return Path.DirectorySeparatorChar == '\\'; }
        }

        internal static string Env(string name)
        {
            try
            {
                var v = Environment.GetEnvironmentVariable(name);
                return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            }
            catch { return null; }
        }

        internal static bool? EnvBool(string name)
        {
            var v = (Env(name) ?? "").ToLowerInvariant();
            if (v == "1" || v == "true" || v == "yes" || v == "on") return true;
            if (v == "0" || v == "false" || v == "no" || v == "off") return false;
            return null;
        }

        internal static double? EnvDouble(string name)
        {
            double d;
            var v = Env(name);
            return v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : (double?)null;
        }

        // For tests: pretend to run under Wine (or not) without a prefix.
        internal static void OverrideWine(bool? wine)
        {
            lock (ProbeLock)
            {
                _probed = wine.HasValue;
                _wine = wine ?? false;
            }
        }

        private static void Probe()
        {
            if (_probed) return;
            lock (ProbeLock)
            {
                if (_probed) return;
                _wine = DetectWine();
                _probed = true;
            }
        }

        private static bool DetectWine()
        {
            if (!IsWindowsOs) return false;
            if (Env("WINEPREFIX") != null || Env("WINELOADER") != null || Env("WINEDLLPATH") != null) return true;
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Wine"))
                {
                    if (key != null) return true;
                }
            }
            catch { }
            return false;
        }
    }
}
