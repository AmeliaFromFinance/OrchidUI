using System.Globalization;
using OrchidUI.Protocol;

namespace OrchidUI.Host.Views;

// Stands in for Streamer.bot with --spec: keeps values, marks them dirty and "saves" them, so a
// spec file can be clicked through. Callbacks such as validation and ShowWhen only run in the real shim.
internal sealed class LocalSink : ILinkSink
{
    private readonly WindowSpec _spec;
    private readonly Dictionary<string, object?> _saved;
    private readonly Dictionary<string, object?> _values;

    public LocalSink(WindowSpec spec)
    {
        _spec = spec;
        _values = spec.Values.ToDictionary(k => k.Key, k => (object?)k.Value, StringComparer.Ordinal);
        _saved = new Dictionary<string, object?>(_values, StringComparer.Ordinal);
    }

    public MainWindow? Window { get; set; }

    public void Send(string type, Dictionary<string, object> payload)
    {
        if (Window is null) return;
        switch (type)
        {
            case MessageTypes.Change:
            {
                var key = Json.Str(payload, "key", "");
                _values[key] = Json.Get(payload, "value");
                var field = _spec.Fields.FirstOrDefault(f => f.Id == Json.Str(payload, "field"));
                var state = new FieldState { Id = field?.Id ?? "", Dirty = field?.Keys.Any(IsDirty) ?? false };
                if (field is { HasFormat: true } && field.Key == key && _values[key] is not null and not string and not bool)
                    state.Display = Humanize(Convert.ToDouble(_values[key], CultureInfo.InvariantCulture));
                Window.ApplyPatch(new Dictionary<string, object> { { "states", new object[] { state.ToJson() } }, { "status", Status().ToJson() } });
                break;
            }
            case MessageTypes.Command:
                switch (Json.Str(payload, "name"))
                {
                    case Commands.Save:
                    case Commands.SaveAndClose:
                    case Commands.Done:
                        foreach (var (k, v) in _values) _saved[k] = v;
                        Window.ApplyPatch(new Dictionary<string, object> { { "states", AllStates() }, { "status", Status(saved: true).ToJson() } });
                        Window.ShowToast(_spec.String("SettingsSaved", "Settings saved"), "success");
                        if (Json.Str(payload, "name") != Commands.Save) Window.CloseNow();
                        break;
                    case Commands.Refresh:
                    case Commands.ResetAll:
                    case Commands.Import:
                    case Commands.Export:
                    case Commands.About:
                        Window.ShowToast("This runs in Streamer.bot; nothing happens with --spec.", "info");
                        break;
                    case Commands.Discard:
                        foreach (var (k, v) in _saved) _values[k] = v;
                        Window.ApplyPatch(new Dictionary<string, object> { { "values", _saved.ToDictionary(k => k.Key, k => k.Value!) }, { "states", AllStates() }, { "status", Status().ToJson() } });
                        Window.ShowToast(_spec.String("ChangesDiscarded", "Changes discarded"), "info");
                        break;
                }
                break;
            case MessageTypes.Click:
                Window.ShowToast("Buttons run in Streamer.bot; nothing happens with --spec.", "info");
                break;
            case MessageTypes.CloseRequest:
                Window.CloseNow();
                break;
        }
    }

    private bool IsDirty(string key) =>
        !_saved.TryGetValue(key, out var a) || Json.Serialize(a, false) != Json.Serialize(_values.GetValueOrDefault(key), false);

    private object[] AllStates() => _spec.Fields.Select(f => (object)new FieldState { Id = f.Id, Dirty = f.Keys.Any(IsDirty) }.ToJson()).ToArray();

    private StatusSpec Status(bool saved = false)
    {
        bool dirty = _values.Keys.Any(IsDirty);
        return new StatusSpec
        {
            Dirty = dirty,
            CanDiscard = dirty,
            Text = dirty ? _spec.String("UnsavedChanges", "Unsaved changes")
                : saved ? string.Format(CultureInfo.InvariantCulture, _spec.String("SavedAt", "Saved at {0}"), DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture))
                : _spec.String("AllSaved", "All changes saved"),
            Tone = dirty ? "normal" : "muted",
        };
    }

    internal static string Humanize(double secondsValue)
    {
        long total = (long)Math.Max(0, Math.Round(secondsValue));
        long d = total / 86400, h = total % 86400 / 3600, m = total % 3600 / 60, s = total % 60;
        if (d > 0) return h > 0 ? d + "d " + h + "h" : d + "d";
        if (h > 0) return m > 0 ? h + "h " + m + "m" : h + "h";
        if (m > 0) return s > 0 ? m + "m " + s + "s" : m + "m";
        return s + "s";
    }
}
