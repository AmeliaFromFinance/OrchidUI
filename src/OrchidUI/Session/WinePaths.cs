using System;
using System.Diagnostics;
using System.IO;

namespace OrchidUI
{
    // Paths between the Wine prefix and the Linux/macOS side. Z: is Wine's root-filesystem drive;
    // anything else goes through winepath.
    internal static class WinePaths
    {
        public static string FromUnix(string unixPath)
        {
            if (string.IsNullOrEmpty(unixPath) || unixPath[0] != '/') throw new ArgumentException("Not an absolute Unix path.", "unixPath");
            return "Z:" + unixPath.Replace('/', '\\');
        }

        public static string ToUnix(string windowsPath)
        {
            if (string.IsNullOrEmpty(windowsPath)) return null;
            var full = windowsPath.Replace('/', '\\');
            if (full.StartsWith(@"Z:\", StringComparison.OrdinalIgnoreCase)) return "/" + full.Substring(3).Replace('\\', '/');
            return RunWinepath(full);
        }

        private static string RunWinepath(string path)
        {
            try
            {
                var psi = new ProcessStartInfo("winepath.exe", "-u \"" + path + "\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return null;
                    string output = p.StandardOutput.ReadToEnd().Trim();
                    if (!p.WaitForExit(5000)) { try { p.Kill(); } catch { } return null; }
                    return p.ExitCode == 0 && output.StartsWith("/", StringComparison.Ordinal) ? output : null;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
                return null;
            }
        }
    }
}
