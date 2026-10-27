using Avalonia;
using OrchidUI.Host.Session;

namespace OrchidUI.Host;

internal static class Program
{
    private const string Usage = """
        orchid-host: draws OrchidUI settings windows natively.

          orchid-host --session <file>   open the window a Streamer.bot action asked for (started by OrchidUI.dll)
          orchid-host --watch [--dir D]  keep running and open windows as they're requested (Wine fallback)
          orchid-host --spec <file>      show a window from a WindowSpec JSON file, without Streamer.bot
          orchid-host --version

        Logs go to
        """;

    [STAThread]
    public static int Main(string[] args)
    {
        string? session = null, spec = null, dir = null;
        bool watch = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--session" when i + 1 < args.Length: session = args[++i]; break;
                case "--spec" when i + 1 < args.Length: spec = args[++i]; break;
                case "--dir" when i + 1 < args.Length: dir = args[++i]; break;
                case "--watch": watch = true; break;
                case "--log-console": HostLog.Console = true; break;
                case "--version":
                    Console.WriteLine("orchid-host " + HostInfo.Version + " (protocol " + Protocol.ProtocolInfo.Major + ")");
                    return 0;
                default:
                    Console.WriteLine(Usage + " " + HostLog.Directory);
                    return args[i] is "--help" or "-h" ? 0 : 64;
            }
        }
        HostLog.Init();
        if (watch)
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            return WatchMode.Run(dir, cts.Token);
        }
        if (session is null && spec is null)
        {
            Console.WriteLine(Usage + " " + HostLog.Directory);
            return 64;
        }
        HostRun.Current = session is not null ? new HostRun(HostRun.Mode.Session, session) : new HostRun(HostRun.Mode.Spec, spec!);
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            HostLog.Error("orchid-host crashed: " + ex);
            return 70;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
