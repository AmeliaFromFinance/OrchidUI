using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using OrchidUI.Host.Fields;
using OrchidUI.Host.Views;
using OrchidUI.Protocol;
using Xunit;

namespace OrchidUI.Host.Tests;

// AddTextList and AddTable (and the selection lists, which are tables): every edit sends the whole
// list in its stored form; values from the shim redraw it without being sent back.
public class ListTests
{
    private static (MainWindow W, RecordingSink Sink, ListView View) Open(string key)
    {
        var spec = Repo.Spec("settings");
        foreach (var f in spec.Fields) f.Collapsed = false;
        var (w, sink) = Wins.Open(spec);
        var view = Wins.View<ListView>(w, key);
        w.SelectTab(view.Spec.Tab);
        Wins.Pump();
        return (w, sink, view);
    }

    private static string[] Flat(RecordingSink sink, string key) =>
        ((object[])Json.Get(sink.LastChange(key)!, "value")!).Cast<string>().ToArray();

    private static string[][] Rows(RecordingSink sink, string key) =>
        ((object[])Json.Get(sink.LastChange(key)!, "value")!).Cast<object[]>().Select(r => r.Cast<string>().ToArray()).ToArray();

    [AvaloniaFact]
    public void A_text_list_adds_with_enter_edits_moves_and_removes()
    {
        var (w, sink, view) = Open("rotator");
        Assert.Equal(new[] { "A sub adds 3 minutes!", "100 bits add 1 minute!" }, view.Rows.Select(r => r[0]));

        view.AddBox!.Focus();
        w.KeyTextInput("Raids add 2 minutes!");
        w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Wins.Pump();
        Assert.Equal(new[] { "A sub adds 3 minutes!", "100 bits add 1 minute!", "Raids add 2 minutes!" }, Flat(sink, "rotator"));
        Assert.Equal("", view.AddBox.Text);

        // Edit the first item, move the last one up, remove the middle one.
        var first = (TextBox)view.Inputs.First();
        first.Text = "A sub adds 4 minutes!";
        Wins.Pump();
        Assert.Equal("A sub adds 4 minutes!", Flat(sink, "rotator")[0]);
        Click(view, "Move up (Rotator Messages item 3)");
        Assert.Equal(new[] { "A sub adds 4 minutes!", "Raids add 2 minutes!", "100 bits add 1 minute!" }, Flat(sink, "rotator"));
        Click(view, "Remove (Rotator Messages item 2)");
        Assert.Equal(new[] { "A sub adds 4 minutes!", "100 bits add 1 minute!" }, Flat(sink, "rotator"));

        // An empty add box adds nothing.
        int before = sink.Of(MessageTypes.Change).Count();
        view.AddButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wins.Pump();
        Assert.Equal(before, sink.Of(MessageTypes.Change).Count());
    }

    [AvaloniaFact]
    public void A_table_adds_rows_with_defaults_and_sends_cells_as_strings()
    {
        var (_, sink, view) = Open("goals");
        Assert.Equal(new[] { "Subs", "50", "Cooking stream" }, view.Rows[0]);

        view.AddButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wins.Pump();
        var rows = Rows(sink, "goals");
        Assert.Equal(4, rows.Length);
        Assert.Equal(new[] { "Subs", "0", "" }, rows[3]);

        // The new row's cells: choice, number, text.
        var cells = view.Inputs.Skip(3 * 6).Take(3).ToArray();
        ((ComboBox)cells[0]).SelectedItem = "Raids";
        ((NumericUpDown)cells[1]).Value = 12.5m;
        ((TextBox)cells[2]).Text = "Charity stream";
        Wins.Pump();
        Assert.Equal(new[] { "Raids", "12.5", "Charity stream" }, Rows(sink, "goals")[3]);
        ((NumericUpDown)cells[1]).Value = null;
        Wins.Pump();
        Assert.Equal("", Rows(sink, "goals")[3][1]);
    }

    [AvaloniaFact]
    public void A_list_from_the_shim_is_drawn_and_not_sent_back()
    {
        var (w, sink, view) = Open("goals");
        sink.Clear();
        w.ApplyPatch(new Dictionary<string, object>
        {
            { "values", new Dictionary<string, object> { { "goals", new object[] { new object[] { "Bits", "500" }, new object[] { "Mystery", "1", "x" } } } } },
        });
        Wins.Pump();
        Assert.Empty(sink.Of(MessageTypes.Change));
        Assert.Equal(new[] { "Bits", "500", "" }, view.Rows[0]);
        // A choice that isn't an option still shows.
        Assert.Equal("Mystery", ((ComboBox)view.Inputs.Skip(6).First()).SelectedItem);
        Assert.Equal(2, view.RowsPanel.Children.Count);
    }

    [AvaloniaFact]
    public void A_collapsed_list_starts_hidden_shows_its_summary_and_opens_for_a_jump()
    {
        var spec = Repo.Spec("settings");
        var (w, _) = Wins.Open(spec);
        var view = Wins.View<ListView>(w, "rotator");
        w.SelectTab(view.Spec.Tab);
        Wins.Pump();
        Assert.True(view.Spec.Collapsed);
        Assert.False(view.RowsPanel.Parent is Control body && body.IsVisible);
        Assert.Equal("2 item(s)", view.SummaryText!.Text);
        w.JumpTo(view.Spec.Id);
        Wins.Pump();
        Assert.True(((Control)view.RowsPanel.Parent!).IsVisible);
    }

    [AvaloniaFact]
    public void A_multi_select_adds_from_the_list_removes_chips_and_refreshes()
    {
        var spec = Repo.Spec("settings");
        foreach (var f in spec.Fields) f.Collapsed = false;
        var (w, sink) = Wins.Open(spec);
        var view = Wins.View<MultiSelectView>(w, "rewards");
        w.SelectTab(view.Spec.Tab);
        Wins.Pump();
        Assert.Empty(view.Selected);
        Assert.Equal(new[] { "Add 5 minutes", "Hydrate", "Pet the cat", "Song request" }, view.Picker.ItemsSource!.Cast<string>());

        Pick(w, view, "Hydrate");
        Pick(w, view, "Song request");
        Assert.Equal(new object[] { "Hydrate", "Song request" }, (object[])Json.Get(sink.LastChange("rewards")!, "value")!);
        // What's chosen leaves the list to add from.
        Assert.Equal(new[] { "Add 5 minutes", "Pet the cat" }, view.Picker.ItemsSource!.Cast<string>());

        var remove = view.Inputs.OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Remove Hydrate (Contributing Rewards)");
        remove.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wins.Pump();
        Assert.Equal(new object[] { "Song request" }, (object[])Json.Get(sink.LastChange("rewards")!, "value")!);

        view.RefreshButton!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wins.Pump();
        var command = sink.Of(MessageTypes.Command).Last();
        Assert.Equal(Commands.Refresh, Json.Str(command, "name"));
        Assert.Equal(view.Spec.Id, Json.Str(command, "field"));

        // New options from the shim; a chosen one that's gone stays chosen.
        var respec = FieldSpec.FromJson(view.Spec.ToJson());
        respec.Props["options"] = new object[] { "Hydrate", "Stretch" };
        sink.Clear();
        w.ApplyPatch(new Dictionary<string, object> { { "fields", new object[] { respec.ToJson() } }, { "values", new Dictionary<string, object> { { "rewards", new object[] { "Song request", "Stretch" } } } } });
        Wins.Pump();
        Assert.Equal(new[] { "Song request", "Stretch" }, view.Selected);
        Assert.Equal(new[] { "Hydrate" }, view.Picker.ItemsSource!.Cast<string>());
        Assert.Empty(sink.Of(MessageTypes.Change));
    }

    [AvaloniaFact]
    public void A_refreshable_dropdown_has_a_refresh_button()
    {
        var spec = Repo.Spec("settings");
        spec.Fields.First(f => f.Key == "currency").Props["refreshable"] = true;
        var (w, sink) = Wins.Open(spec);
        var view = Wins.View<DropdownView>(w, "currency");
        Assert.NotNull(view.RefreshButtonControl);
        Assert.Contains(view.RefreshButtonControl, view.Inputs);
        view.RefreshButtonControl!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wins.Pump();
        Assert.Equal(Commands.Refresh, Json.Str(sink.Of(MessageTypes.Command).Last(), "name"));
        Assert.Null(Wins.View<DropdownView>(w, "font").RefreshButtonControl);
    }

    // Highlight an option, then Enter on the closed box adds it.
    private static void Pick(MainWindow w, MultiSelectView view, string item)
    {
        view.Picker.SelectedItem = item;
        view.Picker.Focus();
        w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Wins.Pump();
    }

    [AvaloniaFact]
    public void Moving_through_a_multi_selects_options_adds_nothing_until_one_is_chosen()
    {
        var spec = Repo.Spec("settings");
        foreach (var f in spec.Fields) f.Collapsed = false;
        var (w, sink) = Wins.Open(spec);
        var view = Wins.View<MultiSelectView>(w, "rewards");
        w.SelectTab(view.Spec.Tab);
        Wins.Pump();
        view.Picker.Focus();
        Wins.Pump();
        foreach (var key in new[] { PhysicalKey.ArrowDown, PhysicalKey.ArrowDown, PhysicalKey.ArrowUp })
        {
            w.KeyPressQwerty(key, RawInputModifiers.None);
            Wins.Pump();
        }
        Assert.Empty(view.Selected);
        Assert.Null(sink.LastChange("rewards"));

        // Opening the list and closing it with Escape adds nothing either.
        view.Picker.IsDropDownOpen = true;
        Wins.Pump();
        w.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        w.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Wins.Pump();
        Assert.False(view.Picker.IsDropDownOpen);
        Assert.Empty(view.Selected);

        // Arrowing on the closed box, then opening and closing the list without choosing: nothing.
        view.Picker.Focus();
        w.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Wins.Pump();
        view.Picker.IsDropDownOpen = true;
        Wins.Pump();
        view.Picker.IsDropDownOpen = false;
        Wins.Pump();
        Assert.Empty(view.Selected);

        // Choosing from the open list adds it.
        view.Picker.IsDropDownOpen = true;
        Wins.Pump();
        view.Picker.SelectedItem = "Pet the cat";
        view.Picker.IsDropDownOpen = false;
        Wins.Pump();
        Assert.Equal(new[] { "Pet the cat" }, view.Selected);
        Assert.Equal(new object[] { "Pet the cat" }, (object[])Json.Get(sink.LastChange("rewards")!, "value")!);
    }

    [AvaloniaFact]
    public void A_number_cell_takes_a_decimal_comma_and_stores_a_dot()
    {
        var (w, sink, view) = Open("goals");
        var cell = (NumericUpDown)view.Inputs.Skip(1).First();
        Assert.Equal("50", cell.Text);
        cell.Focus();
        Wins.Pump();
        w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        w.KeyTextInput("12,5");
        w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Wins.Pump();
        Assert.Equal(12.5m, cell.Value);
        Assert.Equal("12.5", Rows(sink, "goals")[0][1]);
        Assert.Equal(1234.5m, (decimal)ListView.CellNumber.Instance.Convert("1.234,5", typeof(decimal?), null, System.Globalization.CultureInfo.InvariantCulture)!);
        Assert.Equal(1234.5m, (decimal)ListView.CellNumber.Instance.Convert("1,234.5", typeof(decimal?), null, System.Globalization.CultureInfo.InvariantCulture)!);
        Assert.Null(ListView.CellNumber.Instance.Convert(" ", typeof(decimal?), null, System.Globalization.CultureInfo.InvariantCulture));
        // "1,000": a thousand in en-US, one in de-DE.
        Assert.Equal(1000m, (decimal)ListView.CellNumber.Instance.Convert("1,000", typeof(decimal?), null, new System.Globalization.CultureInfo("en-US"))!);
        Assert.Equal(1m, (decimal)ListView.CellNumber.Instance.Convert("1,000", typeof(decimal?), null, new System.Globalization.CultureInfo("de-DE"))!);
        Assert.Equal(1.5m, (decimal)ListView.CellNumber.Instance.Convert("1,5", typeof(decimal?), null, new System.Globalization.CultureInfo("en-US"))!);
    }

    [AvaloniaFact]
    public void A_value_from_the_shim_doesnt_take_the_keyboard_from_the_cell_being_typed_in()
    {
        var (w, sink, view) = Open("goals");
        var typing = (TextBox)view.Inputs.Skip(2).First();
        typing.Focus();
        Wins.Pump();
        w.ApplyPatch(new Dictionary<string, object>
        {
            { "values", new Dictionary<string, object> { { "goals", view.Rows.Select((r, i) => (object)r.Select((c, j) => (object)(i == 1 && j == 2 ? "Renamed by a handler" : c)).ToArray()).ToArray() } } },
        });
        Wins.Pump();
        Assert.Same(typing, view.Inputs.Skip(2).First());
        Assert.True(typing.IsFocused);
        Assert.Equal("Renamed by a handler", ((TextBox)view.Inputs.Skip(6 + 2).First()).Text);
        Assert.Equal("Renamed by a handler", view.Rows[1][2]);

        // Rows longer than the columns (kept as they are) never reach the next row's cells.
        var before = view.Rows[1].ToArray();
        var wide = view.Rows.Select(r => r.ToArray()).ToArray();
        wide[0] = [.. wide[0], "x", "y", "z", "w", "v", "u", "t"];
        view.SetValue("goals", wide.Select(r => (object)r.Cast<object>().ToArray()).ToArray());
        Wins.Pump();
        wide[0][8] = "Not row 2's reward";
        view.SetValue("goals", wide.Select(r => (object)r.Cast<object>().ToArray()).ToArray());
        Wins.Pump();
        Assert.Equal(before, view.Rows[1]);
        Assert.Equal(before[2], ((TextBox)view.Inputs.Skip(6 + 2).First()).Text);
    }

    [AvaloniaFact]
    public void Unavailable_moves_are_left_out_and_the_toggle_says_whether_its_open()
    {
        var spec = Repo.Spec("settings");
        var (w, _) = Wins.Open(spec);
        var view = Wins.View<ListView>(w, "rotator");
        w.SelectTab(view.Spec.Tab);
        Wins.Pump();
        var buttons = view.Inputs.OfType<Button>().Where(b => (Avalonia.Automation.AutomationProperties.GetName(b) ?? "").StartsWith("Move", StringComparison.Ordinal)).ToList();
        var up1 = buttons.First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Move up (Rotator Messages item 1)");
        var down2 = buttons.First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Move down (Rotator Messages item 2)");
        Assert.False(up1.IsVisible);
        Assert.False(down2.IsVisible);
        Assert.True(buttons.First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Move down (Rotator Messages item 1)").IsVisible);

        var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(view.Disclosure!);
        var expander = Assert.IsAssignableFrom<Avalonia.Automation.Provider.IExpandCollapseProvider>(peer);
        Assert.Equal(Avalonia.Automation.ExpandCollapseState.Collapsed, expander.ExpandCollapseState);
        expander.Expand();
        Wins.Pump();
        Assert.Equal(Avalonia.Automation.ExpandCollapseState.Expanded, expander.ExpandCollapseState);
        Assert.True(((Control)view.RowsPanel.Parent!).IsVisible);
        view.Disclosure!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wins.Pump();
        Assert.Equal(Avalonia.Automation.ExpandCollapseState.Collapsed, expander.ExpandCollapseState);
        Assert.False(((Control)view.RowsPanel.Parent!).IsVisible);
    }

    private static void Click(ListView view, string name)
    {
        var button = view.Inputs.OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == name);
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wins.Pump();
    }
}
