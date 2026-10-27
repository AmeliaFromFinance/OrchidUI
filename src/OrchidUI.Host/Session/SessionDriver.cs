using Avalonia.Controls;
using OrchidUI.Host.Views;
using OrchidUI.Protocol;

namespace OrchidUI.Host.Session;

// Turns frames from the shim into window actions (UI thread). Used by orchid-host and by the
// integration tests, which run the same code against a headless window.
internal sealed class SessionDriver
{
    private readonly SessionClient _client;
    private readonly Action<Window> _show;
    private readonly Action<int> _finish;
    private bool _finished;

    public SessionDriver(SessionClient client, Action<Window> show, Action<int> finish)
    {
        _client = client;
        _show = show;
        _finish = finish;
        client.FrameReceived += OnFrame;
        client.Lost += OnLost;
    }

    public MainWindow? Window { get; private set; }
    public DialogWindow? DialogWindow { get; private set; }

    private void OnFrame(Frame f)
    {
        var p = f.Payload;
        try
        {
            switch (f.Type)
            {
                case MessageTypes.Open:
                    Open(Json.Obj(p, "spec"));
                    break;
                case MessageTypes.Patch:
                    Window?.ApplyPatch(p);
                    break;
                case MessageTypes.Toast:
                    Window?.ShowToast(Json.Str(p, "message", ""), Json.Str(p, "kind", "info"));
                    break;
                case MessageTypes.Dialog:
                {
                    var buttons = (Json.Arr(p, "buttons") ?? Array.Empty<object>()).Select(b => b as string ?? "").ToArray();
                    int request = Json.Int(p, "request"), primary = Json.Int(p, "primary", buttons.Length - 1);
                    if (Window is not null) Window.ShowDialog(request, Json.Str(p, "title", ""), Json.Str(p, "message", ""), buttons, primary);
                    else DialogWindow?.Show(request, Json.Str(p, "title", ""), Json.Str(p, "message", ""), buttons, primary);
                    break;
                }
                case MessageTypes.DialogCancel:
                    Window?.CancelDialog(Json.Int(p, "request"));
                    break;
                case MessageTypes.Focus:
                    Window?.BringToFront();
                    break;
                case MessageTypes.Jump:
                    Window?.JumpTo(Json.Str(p, "field", ""));
                    break;
                case MessageTypes.Close:
                    HostLog.Info("closing on request");
                    if (Window is not null) Window.CloseNow();
                    else if (DialogWindow is not null) DialogWindow.CloseNow();
                    else Finish(0);
                    break;
                case MessageTypes.Clipboard:
                    if (Window is not null) _ = Window.CopyText(Json.Str(p, "text", ""));
                    break;
                case MessageTypes.Refused:
                    HostLog.Error("Streamer.bot refused this host: " + Json.Str(p, "reason", "?"));
                    Finish(77);
                    break;
            }
        }
        catch (Exception ex)
        {
            HostLog.Error("handling " + f.Type + " failed: " + ex);
            _client.Send(MessageTypes.Log, new Dictionary<string, object> { { "level", "error" }, { "message", "orchid-host couldn't handle '" + f.Type + "': " + ex.Message } });
        }
    }

    private void Open(Dictionary<string, object>? specJson)
    {
        if (Window is not null || DialogWindow is not null) return;
        WindowSpec spec;
        try { spec = WindowSpec.FromJson(specJson); }
        catch (FormatException ex)
        {
            HostLog.Error(ex.Message);
            _client.Send(MessageTypes.Log, new Dictionary<string, object> { { "level", "error" }, { "message", ex.Message + " Update orchid-host to match OrchidUI.dll." } });
            Finish(76);
            return;
        }
        HostLog.Info("spec " + spec.Extension + ", " + spec.Tabs.Count + " tabs; theme " + (spec.Theme.IsDark ? "dark" : "light"));
        Window shown;
        if (Json.Str(spec.Window, "mode") == "dialog")
        {
            DialogWindow = new DialogWindow(spec, _client);
            shown = DialogWindow;
        }
        else
        {
            Window = new MainWindow(spec, _client);
            shown = Window;
        }
        shown.Closed += (_, _) => Finish(0);
        _show(shown);
        _client.Send(MessageTypes.Ready, new Dictionary<string, object>());
    }

    private void OnLost(string reason)
    {
        HostLog.Warn("link ended: " + reason);
        bool hadWindow = Window is not null || DialogWindow is not null;
        if (Window is not null) Window.CloseNow();
        else if (DialogWindow is not null) DialogWindow.CloseNow();
        Finish(hadWindow ? 0 : 75);
    }

    private void Finish(int code)
    {
        if (_finished) return;
        _finished = true;
        _client.Dispose();
        _finish(code);
    }
}
