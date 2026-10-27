using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using OrchidUI.Host.Fields;
using OrchidUI.Protocol;
using Xunit;

namespace OrchidUI.Host.Tests;

// Keyboard input on each control becomes a change message with the value the shim stores.
public class InputTests
{
    private static void Focus(Control c)
    {
        c.Focus();
        Wins.Pump();
    }

    private static void Key(Avalonia.Controls.Window w, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPressQwerty(key, mods);
        w.KeyReleaseQwerty(key, mods);
        Wins.Pump();
    }

    [AvaloniaFact]
    public void Typing_in_a_text_box_sends_the_text()
    {
        var (w, sink) = Wins.Open(Repo.Spec("settings"));
        w.SelectTab(Repo.Spec("settings").Fields.First(f => f.Key == "apiToken").Tab);
        Wins.Pump();
        var box = (TextBox)Wins.View<TextView>(w, "apiToken").Inputs.Single();
        Focus(box);
        w.KeyTextInput("s3cret");
        Wins.Pump();
        Assert.Equal("s3cret", Json.Str(sink.LastChange("apiToken")!, "value"));
        Assert.Equal('•', box.PasswordChar);
    }

    [AvaloniaFact]
    public void Space_flips_a_toggle()
    {
        var (w, sink) = Wins.Open(Repo.Spec("settings"));
        var toggle = (ToggleSwitch)Wins.View<ToggleView>(w, "enabled").Inputs.Single();
        Focus(toggle);
        Key(w, PhysicalKey.Space);
        Assert.Equal(false, Json.Get(sink.LastChange("enabled")!, "value"));
    }

    [AvaloniaFact]
    public void Arrow_keys_move_a_slider_by_one()
    {
        var spec = Repo.Spec("settings");
        var (w, sink) = Wins.Open(spec);
        w.SelectTab(spec.Fields.First(f => f.Key == "time_t1").Tab);
        Wins.Pump();
        var slider = Wins.View<SliderView>(w, "time_t1").Inputs.OfType<Slider>().Single();
        Focus(slider);
        Key(w, PhysicalKey.ArrowRight);
        Assert.Equal(181, Json.Int(sink.LastChange("time_t1")!, "value"));
    }

    [AvaloniaFact]
    public void The_switch_beside_a_slider_sends_its_own_key()
    {
        var spec = Repo.Spec("settings");
        var (w, sink) = Wins.Open(spec);
        w.SelectTab(spec.Fields.First(f => f.Key == "time_raid").Tab);
        Wins.Pump();
        var toggle = Wins.View<SliderView>(w, "time_raid").Inputs.OfType<ToggleSwitch>().Single();
        Focus(toggle);
        Key(w, PhysicalKey.Space);
        Assert.Equal(true, Json.Get(sink.LastChange("time_raid_enabled")!, "value"));
    }

    [AvaloniaFact]
    public void Up_steps_a_number_box()
    {
        var (w, sink) = Wins.Open(Repo.Spec("minimal"));
        var box = (NumericUpDown)Wins.View<StepperView>(w, "minRaiders").Inputs.Single();
        Focus(box);
        Key(w, PhysicalKey.ArrowUp);
        Assert.Equal(6, Json.Int(sink.LastChange("minRaiders")!, "value"));
    }

    [AvaloniaFact]
    public void Up_on_a_duration_segment_adds_one_unit_and_carries()
    {
        var (w, sink) = Wins.Open(Repo.Spec("settings"));
        var hours = Wins.View<DurationView>(w, "startingTime").Inputs.Cast<TextBox>().ElementAt(1);
        Focus(hours);
        Key(w, PhysicalKey.ArrowUp);
        Assert.Equal("00:07:00:00", Json.Str(sink.LastChange("startingTime")!, "value"));
    }

    [AvaloniaFact]
    public void Segments_send_the_chosen_option()
    {
        var (w, sink) = Wins.Open(Repo.Spec("settings"));
        var first = Wins.View<SegmentedView>(w, "timeFormat").Inputs.Cast<ToggleButton>().First();
        Focus(first);
        Key(w, PhysicalKey.Space);
        Assert.Equal("dd:hh:mm:ss", Json.Str(sink.LastChange("timeFormat")!, "value"));
    }

    [AvaloniaFact]
    public void Choosing_one_segment_after_another_highlights_only_the_last()
    {
        // The highlight has to follow the value on the first click, with no earlier option left lit.
        var (w, sink) = Wins.Open(Repo.Spec("settings"));
        var buttons = Wins.View<SegmentedView>(w, "timeFormat").Inputs.Cast<ToggleButton>().ToList();
        Assert.Equal(new bool?[] { false, true, false }, buttons.Select(b => b.IsChecked));
        foreach (var (index, value) in new[] { (0, "dd:hh:mm:ss"), (2, "hh:mm"), (1, "hh:mm:ss"), (1, "hh:mm:ss") })
        {
            Focus(buttons[index]);
            Key(w, PhysicalKey.Space);
            Assert.Equal(buttons.Select((_, i) => (bool?)(i == index)), buttons.Select(b => b.IsChecked));
            Assert.Equal(value, Json.Str(sink.LastChange("timeFormat")!, "value"));
        }
        // Clicking the chosen one again keeps it, and sends nothing new.
        int sent = sink.Of(MessageTypes.Change).Count();
        Key(w, PhysicalKey.Space);
        Assert.True(buttons[1].IsChecked);
        Assert.Equal(sent, sink.Of(MessageTypes.Change).Count());
    }

    [AvaloniaFact]
    public void A_segment_set_by_the_shim_highlights_only_that_option()
    {
        var (w, sink) = Wins.Open(Repo.Spec("settings"));
        w.ApplyPatch(new Dictionary<string, object> { { "values", new Dictionary<string, object> { { "timeFormat", "hh:mm" } } } });
        Wins.Pump();
        var buttons = Wins.View<SegmentedView>(w, "timeFormat").Inputs.Cast<ToggleButton>().ToList();
        Assert.Equal(new bool?[] { false, false, true }, buttons.Select(b => b.IsChecked));
        Assert.Empty(sink.Of(MessageTypes.Change));
    }

    [AvaloniaFact]
    public void A_value_pushed_by_the_shim_is_not_echoed_back()
    {
        var (w, sink) = Wins.Open(Repo.Spec("settings"));
        w.ApplyPatch(new Dictionary<string, object> { { "values", new Dictionary<string, object> { { "enabled", false }, { "currency", "EUR" }, { "startingTime", "00:00:01:30" } } } });
        Wins.Pump();
        Assert.Empty(sink.Of(MessageTypes.Change));
        Assert.Equal(false, ((ToggleSwitch)Wins.View<ToggleView>(w, "enabled").Inputs.Single()).IsChecked);
    }
}
