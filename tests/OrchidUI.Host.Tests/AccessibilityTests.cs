using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;

namespace OrchidUI.Host.Tests;

// Fails the build if anything a keyboard user can reach has no accessible name.
public class AccessibilityTests
{
    public static TheoryData<string> Specs => new() { "settings", "minimal", "customised" };

    [AvaloniaTheory]
    [MemberData(nameof(Specs))]
    public void Every_focusable_control_on_every_tab_has_a_name(string name)
    {
        var spec = Repo.Spec(name);
        var (w, _) = Wins.Open(spec);
        var missing = new List<string>();
        var reachable = 0;
        foreach (var tab in spec.Tabs)
        {
            w.SelectTab(tab.Id);
            Wins.Pump();
            reachable += Focusable(w).Count();
            foreach (var c in Unnamed(w)) missing.Add(tab.Name + ": " + Describe(c));
        }
        Assert.True(reachable >= 4, "The walk only reached " + reachable + " controls.");
        Assert.True(missing.Count == 0, "Controls without an accessible name:\n" + string.Join("\n", missing.Distinct()));
    }

    [AvaloniaFact]
    public void Inputs_carry_the_field_description_as_help_text()
    {
        var (w, _) = Wins.Open(Repo.Spec("minimal"));
        var toggle = Wins.View<Fields.ToggleView>(w, "enabled").Inputs.Single();
        Assert.Equal("Enabled", AutomationProperties.GetName(toggle));
        Assert.Equal("Shout out raiders automatically.", AutomationProperties.GetHelpText(toggle));
    }

    [AvaloniaFact]
    public void A_screen_reader_sees_a_tab_as_a_named_tree()
    {
        // What Orca walks through AT-SPI: the automation tree under the window.
        var (w, _) = Wins.Open(Repo.Spec("minimal"));
        var names = new List<string>();
        void Walk(AutomationPeer p)
        {
            var n = p.GetName();
            if (!string.IsNullOrEmpty(n)) names.Add(n);
            foreach (var child in p.GetChildren()) Walk(child);
        }
        Walk(ControlAutomationPeer.CreatePeerForElement(w));
        Assert.Contains("Enabled", names);
        Assert.Contains("Minimum Raiders", names);
        Assert.Contains("Message", names);
        Assert.Contains("Done", names);
    }

    private static IEnumerable<Control> Focusable(Control root) =>
        root.GetVisualDescendants().OfType<Control>()
            .Where(c => c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled && c is not Window)
            .Where(c => !IsInnerPart(c));

    internal static IEnumerable<Control> Unnamed(Control root) =>
        Focusable(root).Where(c => string.IsNullOrWhiteSpace(ControlAutomationPeer.CreatePeerForElement(c).GetName()));

    // Parts inside a templated control (a NumericUpDown's TextBox, a ComboBox's popup items) take
    // their name from the owner; the owner is checked instead.
    private static bool IsInnerPart(Control c) => c.TemplatedParent is Control owner && owner != c && owner.Focusable;

    private static string Describe(Control c)
    {
        var path = c.GetVisualAncestors().OfType<Control>().Take(3).Select(a => a.GetType().Name);
        return c.GetType().Name + (c.Name is null ? "" : "#" + c.Name) + " in " + string.Join(" < ", path);
    }
}
