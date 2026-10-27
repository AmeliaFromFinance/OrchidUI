using Avalonia.Controls;
using OrchidUI.Host.Theming;
using OrchidUI.Protocol;

namespace OrchidUI.Host.Views;

// Where the window's messages go: the session link, or a local stand-in for --spec and tests.
internal interface ILinkSink
{
    void Send(string type, Dictionary<string, object> payload);
}

// What every field view needs from its window.
internal sealed class WindowContext
{
    public WindowContext(WindowSpec spec, Palette palette, ILinkSink sink)
    {
        Spec = spec;
        P = palette;
        Sink = sink;
    }

    public WindowSpec Spec { get; }
    public Palette P { get; }
    public ILinkSink Sink { get; }
    public Func<string, Task>? CopyToClipboard { get; set; }
    public Action<string, string>? Toast { get; set; }
    public Func<Dictionary<string, object>>? CurrentState { get; set; }

    public string S(string key, string fallback) => Spec.String(key, fallback);
    public double Layout(string key, double fallback) => Json.Num(Spec.Layout, key, fallback);
    public bool LayoutFlag(string key, bool fallback) => Json.Bool(Spec.Layout, key, fallback);
    public string LayoutText(string key, string fallback) => Json.Str(Spec.Layout, key, fallback);
    public bool AutoSave => LayoutFlag("autoSave", false);

    public void SendChange(string fieldId, string key, object? value) =>
        Sink.Send(MessageTypes.Change, new Dictionary<string, object>(StringComparer.Ordinal) { { "field", fieldId }, { "key", key }, { "value", value! } });

    public void SendCommand(string name, string? field = null)
    {
        var payload = new Dictionary<string, object>(StringComparer.Ordinal) { { "name", name } };
        if (field is not null) payload["field"] = field;
        var state = CurrentState?.Invoke();
        if (state is not null) payload["state"] = state;
        Sink.Send(MessageTypes.Command, payload);
    }

    public async Task Copy(string text)
    {
        if (CopyToClipboard is null) return;
        try
        {
            await CopyToClipboard(text);
            Toast?.Invoke(S("Copied", "Copied to the clipboard"), "success");
        }
        catch (Exception ex) { HostLog.Warn("Clipboard: " + ex.Message); }
    }
}

// dd:hh:mm:ss, as the shim's DurationUtil.
internal static class Duration
{
    public static long Total(long d, long h, long m, long s) => d * 86400 + h * 3600 + m * 60 + s;

    public static string Format(long total)
    {
        if (total < 0) total = 0;
        long d = total / 86400; total %= 86400;
        long h = total / 3600; total %= 3600;
        long m = total / 60;
        long s = total % 60;
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}:{3:00}", d, h, m, s);
    }

    public static bool TryParseSeconds(string? text, out long seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split(':');
        var nums = new long[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) return false;
            nums[i] = (long)Math.Max(0, Math.Round(v));
        }
        seconds = nums.Length switch
        {
            1 => nums[0],
            2 => Total(0, 0, nums[0], nums[1]),
            3 => Total(0, nums[0], nums[1], nums[2]),
            4 => Total(nums[0], nums[1], nums[2], nums[3]),
            _ => -1,
        };
        return seconds >= 0;
    }
}
