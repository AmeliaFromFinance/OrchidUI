using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using OrchidUI.Protocol;
using Xunit;

namespace OrchidUI.Host.Tests;

// Ctrl+S, Ctrl+F, Ctrl+1 to 9, Ctrl+Tab, Esc and Ctrl +/-/0 (Cmd on macOS).
public class ShortcutTests
{
    private static readonly RawInputModifiers Cmd = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static void Key(Window w, PhysicalKey key, RawInputModifiers mods)
    {
        w.KeyPressQwerty(key, mods);
        w.KeyReleaseQwerty(key, mods);
        Wins.Pump();
    }

    [AvaloniaFact]
    public void Ctrl_S_saves()
    {
        var (w, sink) = Wins.Open(Repo.Spec("settings"));
        Key(w, PhysicalKey.S, Cmd);
        Assert.Equal(Commands.Save, Json.Str(sink.Of(MessageTypes.Command).Single(), "name"));
    }

    [AvaloniaFact]
    public void Ctrl_number_and_Ctrl_Tab_switch_tabs()
    {
        var (w, _) = Wins.Open(Repo.Spec("settings"));
        Key(w, PhysicalKey.Digit3, Cmd);
        Assert.Equal("t3", w.CurrentTab);
        Key(w, PhysicalKey.Tab, Cmd);
        Assert.Equal("t4", w.CurrentTab);
        Key(w, PhysicalKey.Tab, Cmd | RawInputModifiers.Shift);
        Assert.Equal("t3", w.CurrentTab);
        Key(w, PhysicalKey.Digit1, Cmd);
        Key(w, PhysicalKey.Tab, Cmd | RawInputModifiers.Shift);
        Assert.Equal("t6", w.CurrentTab);
    }

    [AvaloniaFact]
    public void Tab_changes_are_reported_so_copy_boxes_refresh()
    {
        var (w, sink) = Wins.Open(Repo.Spec("settings"));
        Key(w, PhysicalKey.Digit2, Cmd);
        Assert.Equal("t2", Json.Str(sink.Of(MessageTypes.TabShown).Last(), "tab"));
    }

    [AvaloniaFact]
    public void Ctrl_F_focuses_search_and_Esc_clears_it()
    {
        var (w, _) = Wins.Open(Repo.Spec("settings"));
        Key(w, PhysicalKey.F, Cmd);
        Assert.True(w.SearchBox!.IsFocused);
        w.KeyTextInput("currency");
        Wins.Pump();
        var visible = w.Views.Where(v => v.Row.IsVisible && v.IsCard).Select(v => v.Spec.Key).ToArray();
        Assert.Equal(new[] { "currency" }, visible);
        Key(w, PhysicalKey.Escape, RawInputModifiers.None);
        Assert.Equal("", w.SearchBox.Text);
        Assert.Contains(w.Views, v => v.Spec.Key == "enabled" && v.Row.IsVisible);
    }

    [AvaloniaFact]
    public void Search_with_no_match_says_so()
    {
        var (w, _) = Wins.Open(Repo.Spec("settings"));
        w.SearchBox!.Text = "zzzz-nothing";
        Wins.Pump();
        Assert.Contains(Wins.All<TextBlock>(w), t => t.IsEffectivelyVisible && (t.Text ?? "").Contains("No settings match"));
    }

    [AvaloniaFact]
    public void Ctrl_plus_minus_and_zero_zoom()
    {
        var (w, _) = Wins.Open(Repo.Spec("settings"));
        Key(w, PhysicalKey.Equal, Cmd);
        Assert.Equal(1.1, w.Zoom, 3);
        Key(w, PhysicalKey.Minus, Cmd);
        Key(w, PhysicalKey.Minus, Cmd);
        Assert.Equal(0.9, w.Zoom, 3);
        Key(w, PhysicalKey.Digit0, Cmd);
        Assert.Equal(1.0, w.Zoom, 3);
        Assert.Contains("Zoom 100%", w.ToastText);
    }
}
