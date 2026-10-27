using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OrchidUI.Host.Views;
using OrchidUI.Protocol;

namespace OrchidUI.Host.Tests;

internal sealed class RecordingSink : ILinkSink
{
    public List<(string Type, Dictionary<string, object> Payload)> Sent { get; } = new();

    public void Send(string type, Dictionary<string, object> payload) => Sent.Add((type, payload));

    public IEnumerable<Dictionary<string, object>> Of(string type) => Sent.Where(s => s.Type == type).Select(s => s.Payload);

    public Dictionary<string, object>? LastChange(string key) => Of(MessageTypes.Change).LastOrDefault(p => Json.Str(p, "key") == key);

    public void Clear() => Sent.Clear();
}

internal static class Repo
{
    public static string Root
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "OrchidUI.slnx"))) return dir.FullName;
            throw new InvalidOperationException("OrchidUI.slnx not found");
        }
    }

    public static WindowSpec Spec(string name) =>
        WindowSpec.FromJson((Dictionary<string, object>)Json.Parse(File.ReadAllText(Path.Combine(Root, "tests", "fixtures", "specs", name + ".json"))));

    public static ThemeSpec Theme(string name) =>
        ThemeSpec.FromJson((Dictionary<string, object>)Json.Parse(File.ReadAllText(Path.Combine(Root, "tests", "fixtures", "themes", name + ".spec.json"))));

    public static string[] ThemeNames => Directory.GetFiles(Path.Combine(Root, "themes"), "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;
}

internal static class Wins
{
    public static (MainWindow Window, RecordingSink Sink) Open(WindowSpec spec)
    {
        var sink = new RecordingSink();
        var w = new MainWindow(spec, sink);
        w.Show();
        Pump();
        return (w, sink);
    }

    public static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    public static T View<T>(MainWindow w, string key) where T : Fields.FieldView =>
        (T)w.Views.First(v => v.Spec.Keys.Contains(key) || v.Spec.Label == key);

    public static IEnumerable<T> All<T>(Control root) where T : Control => root.GetVisualDescendants().OfType<T>();
}
