using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using OrchidUI.Host.Session;
using OrchidUI.Host.Views;
using OrchidUI.Protocol;

namespace OrchidUI.Host;

// One run of orchid-host: a live session with Streamer.bot, or a spec file shown on its own.
internal sealed class HostRun
{
    public enum Mode { Session, Spec }

    private readonly Mode _mode;
    private readonly string _path;
    private IClassicDesktopStyleApplicationLifetime _desktop = null!;
    private SessionClient? _client;
    private string _token = "";

    public HostRun(Mode mode, string path)
    {
        _mode = mode;
        _path = path;
    }

    public static HostRun? Current { get; set; }

    public void Begin(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop;
        if (_mode == Mode.Spec) ShowSpecFile();
        else _ = ConnectAsync();
    }

    private void ShowSpecFile()
    {
        try
        {
            var root = Json.Parse(File.ReadAllText(_path)) as Dictionary<string, object> ?? throw new InvalidDataException("not a JSON object");
            var specJson = Json.Obj(root, "spec") ?? root;
            var spec = WindowSpec.FromJson(specJson);
            var sink = new LocalSink(spec);
            var window = new MainWindow(spec, sink);
            sink.Window = window;
            window.Closed += (_, _) => _desktop.Shutdown(0);
            _desktop.MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            HostLog.Error("--spec " + _path + ": " + ex.Message);
            Console.Error.WriteLine("Couldn't show " + _path + ": " + ex.Message);
            _desktop.Shutdown(65);
        }
    }

    private async Task ConnectAsync()
    {
        try
        {
            var info = SessionInfo.Read(_path);
            _token = info.Token;
            HostLog.Info("session " + info.Id + " from file; connecting over " + info.Transport);
            _client = await SessionClient.ConnectAsync(info);
            _ = new SessionDriver(_client, w => { _desktop.MainWindow = w; w.Show(); }, Finish);
            _client.Hello(_token);
            _client.StartReading();
            HostLog.Info("hello sent, protocol " + ProtocolInfo.Major);
        }
        catch (Exception ex)
        {
            HostLog.Error("couldn't connect to Streamer.bot: " + ex.Message);
            Dispatcher.UIThread.Post(() => _desktop.Shutdown(69));
        }
    }

    private bool _finished;

    private void Finish(int code)
    {
        if (_finished) return;
        _finished = true;
        _client?.Dispose();
        _desktop.Shutdown(code);
    }
}
