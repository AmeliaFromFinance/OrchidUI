using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using OrchidUI.Host.Fields;
using OrchidUI.Protocol;
using Xunit;

namespace OrchidUI.Host.Tests;

public class RenderTests
{
    [AvaloniaFact]
    public void Every_field_of_the_settings_example_renders()
    {
        var spec = Repo.Spec("settings");
        var (w, _) = Wins.Open(spec);
        Assert.Equal(spec.Fields.Count, w.Views.Count);
        Assert.DoesNotContain(Wins.All<TextBlock>(w), t => (t.Text ?? "").StartsWith("Could not show", StringComparison.Ordinal));
        var kinds = w.Views.Select(v => v.GetType().Name).Distinct().ToArray();
        foreach (var expected in new[] { "TitleView", "DescriptionView", "SeparatorView", "InfoView", "ToggleView", "DurationView", "SegmentedView", "DropdownView", "StepperView", "SliderView", "ColorView", "CopyView", "TextView", "ButtonView", "ListView", "MultiSelectView", "PathView" })
            Assert.Contains(expected, kinds);
        Assert.DoesNotContain("PlaceholderView", kinds);
    }

    [AvaloniaFact]
    public void Layouts_sidebar_tabs_and_none_render()
    {
        var (sidebar, _) = Wins.Open(Repo.Spec("settings"));
        Assert.Contains(Wins.All<ToggleButton>(sidebar), b => b.Classes.Contains("nav"));
        var (tabs, _) = Wins.Open(Repo.Spec("customised"));
        Assert.Contains(Wins.All<ToggleButton>(tabs), b => b.Classes.Contains("tab"));
        var (none, _) = Wins.Open(Repo.Spec("minimal"));
        Assert.DoesNotContain(Wins.All<ToggleButton>(none), b => b.Classes.Contains("nav") || b.Classes.Contains("tab"));
        Assert.Null(none.SearchBox);
    }

    [AvaloniaFact]
    public void Values_from_the_spec_are_shown()
    {
        var (w, _) = Wins.Open(Repo.Spec("settings"));
        var duration = Wins.View<DurationView>(w, "startingTime");
        Assert.Equal(new[] { "0", "6", "0", "0" }, duration.Inputs.Cast<TextBox>().Select(t => t.Text).ToArray());
        var combo = (ComboBox)Wins.View<DropdownView>(w, "currency").Inputs.Single();
        Assert.Equal("USD", combo.SelectedItem);
        var colour = (ColorPicker)Wins.View<Fields.ColorView>(w, "fontColor").Inputs.Single();
        Assert.Equal(Avalonia.Media.Colors.White, colour.Color);
    }

    [AvaloniaFact]
    public void Placeholders_name_the_api_and_the_key()
    {
        // No real kind falls back to a placeholder, so fake one, as an unknown kind from a newer shim would.
        var spec = Repo.Spec("settings");
        spec.Fields.First(f => f.Key == "rotator").Kind = FieldKinds.Unsupported;
        var (w, _) = Wins.Open(spec);
        w.SelectTab("t4");
        Wins.Pump();
        var text = Wins.All<TextBlock>(w).Select(t => t.Text ?? "").ToArray();
        Assert.Contains(text, t => t.Contains("AddTextList") && t.Contains("rotator"));
    }

    [AvaloniaFact]
    public void Field_state_patches_hide_disable_and_flag_errors()
    {
        var spec = Repo.Spec("settings");
        var (w, _) = Wins.Open(spec);
        var view = Wins.View<TextView>(w, "chat_timeAdded");
        w.ApplyPatch(new Dictionary<string, object> { { "states", new object[] { new FieldState { Id = view.Spec.Id, Error = "This setting is required.", Dirty = true }.ToJson() } } });
        Assert.True(view.ErrorText!.IsVisible);
        Assert.Equal("This setting is required.", view.ErrorText.Text);
        w.ApplyPatch(new Dictionary<string, object> { { "states", new object[] { new FieldState { Id = view.Spec.Id, Enabled = false }.ToJson() } } });
        Assert.False(view.Row.IsEnabled);
        w.ApplyPatch(new Dictionary<string, object> { { "states", new object[] { new FieldState { Id = view.Spec.Id, Visible = false }.ToJson() } } });
        Assert.False(view.Row.IsVisible);
    }

    [AvaloniaFact]
    public void Status_patches_drive_the_buttons_and_text()
    {
        var (w, _) = Wins.Open(Repo.Spec("settings"));
        w.ApplyPatch(new Dictionary<string, object> { { "status", new StatusSpec { Dirty = true, Errors = 1, Text = "1 setting(s) need attention", Tone = "danger", CanSave = false, CanDiscard = true }.ToJson() } });
        Assert.Equal("1 setting(s) need attention", w.StatusText);
        var save = Wins.All<Button>(w).First(b => (b.Content as string) == "Save");
        Assert.False(save.IsEnabled);
    }

    [AvaloniaFact]
    public void Dropdown_options_can_be_replaced_while_open()
    {
        var spec = Repo.Spec("settings");
        var (w, _) = Wins.Open(spec);
        var field = spec.Fields.First(f => f.Key == "currency");
        field.Props["options"] = new object[] { "USD", "SEK" };
        w.ApplyPatch(new Dictionary<string, object> { { "fields", new object[] { field.ToJson() } } });
        var combo = (ComboBox)Wins.View<DropdownView>(w, "currency").Inputs.Single();
        Assert.Equal(new[] { "USD", "SEK" }, combo.ItemsSource!.Cast<string>().ToArray());
    }

    [AvaloniaFact]
    public void Toasts_and_dialogs_show_and_answer()
    {
        var (w, sink) = Wins.Open(Repo.Spec("minimal"));
        w.ShowToast("Settings saved", "success");
        Assert.True(w.ToastVisible);
        w.ShowDialog(4, "Load backup?", "Load **this**?", new[] { "Cancel", "Load" }, 1);
        Wins.Pump();
        Assert.Equal(1, w.OpenDialogs);
        w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Wins.Pump();
        var result = sink.Of(MessageTypes.DialogResult).Single();
        Assert.Equal(4, Json.Int(result, "request"));
        Assert.Equal(1, Json.Int(result, "index"));
        Assert.Equal(0, w.OpenDialogs);
    }

    [AvaloniaFact]
    public void Escape_dismisses_a_dialog_with_minus_one()
    {
        var (w, sink) = Wins.Open(Repo.Spec("minimal"));
        w.ShowDialog(9, "Sure?", "Really?", new[] { "No", "Yes" }, 1);
        Wins.Pump();
        w.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Wins.Pump();
        Assert.Equal(-1, Json.Int(sink.Of(MessageTypes.DialogResult).Single(), "index"));
    }

    [AvaloniaFact]
    public void Closing_asks_the_shim_with_the_window_state_in_dip()
    {
        var (w, sink) = Wins.Open(Repo.Spec("settings"));
        w.Close();
        Wins.Pump();
        Assert.True(w.IsVisible, "The shim decides whether the window closes.");
        var state = Json.Obj(sink.Of(MessageTypes.CloseRequest).Single(), "state")!;
        Assert.Equal(1180, Json.Num(state, "w"));
        Assert.Equal(800, Json.Num(state, "h"));
        Assert.Equal("t1", Json.Str(state, "tab"));
        w.CloseNow();
        Wins.Pump();
        Assert.False(w.IsVisible);
    }

    [AvaloniaFact]
    public void Jump_selects_the_tab_holding_the_field()
    {
        var spec = Repo.Spec("settings");
        var (w, _) = Wins.Open(spec);
        var target = spec.Fields.First(f => f.Key == "chat_timeAdded");
        w.JumpTo(target.Id);
        Wins.Pump();
        Assert.Equal(target.Tab, w.CurrentTab);
    }

    // A linked text hidden when its page first shows (by the search or ShowWhen) can be drawn
    // before Avalonia has measured it, and building its layout mid-draw measures the link.
    [AvaloniaFact]
    public void Hidden_text_with_a_link_on_a_page_not_yet_shown_doesnt_crash_the_host()
    {
        var spec = Repo.Spec("settings");
        spec.Fields.First(f => f.Id == "f16").Props["text"] = "Seconds per event, see https://example.com/docs for more.";
        spec.Fields.First(f => f.Id == "f41").Props["text"] = "Help: https://example.com/chat";
        var hidden = spec.States.FirstOrDefault(s => s.Id == "f41");
        if (hidden is null) spec.States.Add(hidden = new FieldState { Id = "f41" });
        hidden.Visible = false;
        var (w, _) = Wins.Open(spec);
        Assert.Equal("t1", w.CurrentTab);

        // The search moves to Events with the description hidden, then shows it again.
        w.SearchBox!.Focus();
        Wins.Pump();
        foreach (var ch in "raid")
        {
            w.KeyTextInput(ch.ToString());
            Wins.Pump();
        }
        Assert.Equal("t2", w.CurrentTab);
        for (int i = 0; i < 4; i++)
        {
            w.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
            Wins.Pump();
        }
        Assert.True(w.Views.First(v => v.Spec.Id == "f16").Row.IsVisible);

        // A page whose linked text ShowWhen hides.
        w.SelectTab("t5");
        Wins.Pump();
        Assert.False(w.Views.First(v => v.Spec.Id == "f41").Row.IsVisible);
    }

    [AvaloniaFact]
    public void Links_only_open_http_and_https()
    {
        var opened = new List<string>();
        var old = Views.Ui.UrlOpener;
        Views.Ui.UrlOpener = u => { opened.Add(u); return true; };
        try
        {
            Assert.True(Views.Ui.OpenUrl("https://example.com/docs"));
            Assert.False(Views.Ui.OpenUrl("file:///etc/passwd"));
            Assert.False(Views.Ui.OpenUrl("javascript:alert(1)"));
            Assert.False(Views.Ui.OpenUrl("C:\\Windows\\system32\\calc.exe"));
            Assert.Equal(new[] { "https://example.com/docs" }, opened);
        }
        finally { Views.Ui.UrlOpener = old; }
    }
}
