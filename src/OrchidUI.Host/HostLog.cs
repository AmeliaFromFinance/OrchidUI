using System.Globalization;

namespace OrchidUI.Host;

// orchid-host.log in the user's OrchidUI folder. Never contains setting values.
internal static class HostLog
{
    private static readonly object Lock = new();
    private static string? _path;

    public static string Directory
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrchidUI");
            var xdg = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsMacOS()) return System.IO.Path.Combine(home, "Library", "Logs", "OrchidUI");
            return System.IO.Path.Combine(string.IsNullOrEmpty(xdg) ? System.IO.Path.Combine(home, ".local", "state") : xdg, "orchidui");
        }
    }

    public static bool Console { get; set; }

    public static void Init(string? path = null)
    {
        try
        {
            var dir = Directory;
            System.IO.Directory.CreateDirectory(dir);
            _path = path ?? System.IO.Path.Combine(dir, "orchid-host.log");
            var info = new FileInfo(_path);
            if (info.Exists && info.Length > 1024 * 1024) File.Move(_path, _path + ".1", true);
        }
        catch (Exception) { _path = null; }
    }

    public static void Info(string message) => Write("info", message);
    public static void Warn(string message) => Write("warn", message);
    public static void Error(string message) => Write("error", message);

    private static void Write(string level, string message)
    {
        var line = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + level + " " + message;
        lock (Lock)
        {
            if (Console) System.Console.Error.WriteLine(line);
            if (_path is null) return;
            try { File.AppendAllText(_path, line + Environment.NewLine); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
