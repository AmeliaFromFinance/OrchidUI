using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace OrchidUI.Host.Views;

// The show/hide button of a list's card: a screen reader hears it as expanded or collapsed (and can
// expand or collapse it), not just its name.
internal sealed class DisclosureButton : Button
{
    private bool _expanded;

    protected override Type StyleKeyOverride => typeof(Button);

    // Shows or hides what the button controls.
    public Action<bool>? Apply { get; set; }

    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value) return;
            var old = _expanded;
            _expanded = value;
            Apply?.Invoke(value);
            (ControlAutomationPeer.FromElement(this) as Peer)?.Changed(old, value);
        }
    }

    public DisclosureButton()
    {
        Click += (_, _) => IsExpanded = !IsExpanded;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    internal sealed class Peer(DisclosureButton owner) : ButtonAutomationPeer(owner), IExpandCollapseProvider
    {
        public ExpandCollapseState ExpandCollapseState => owner.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        public bool ShowsMenu => false;
        public void Expand() => owner.IsExpanded = true;
        public void Collapse() => owner.IsExpanded = false;

        internal void Changed(bool old, bool now) => RaisePropertyChangedEvent(
            ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
            old ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed,
            now ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
    }
}
