// The smallest useful settings window: one tab, no sidebar, saves as you type.
using System;
using OrchidUI;

public class CPHInline
{
    public bool Execute()
    {
        if (Orchid.AlreadyOpened("SHOUTOUTS")) return true;

        var ui = new Orchid(CPH, "SHOUTOUTS", "1.0.0")
            .SetWindowSize(620, 520)
            .SetAutoSave(true);

        var tab = ui.Tab("Shoutouts", Icons.Megaphone);
        tab.AddToggleSwitch("Enabled", "Shout out raiders automatically.", "enabled", true);
        tab.AddIntegerStepper("Minimum Raiders", "Smaller raids are ignored.", "minRaiders", 1, 1000, 5);
        tab.AddResponseBox("Message", "Variables: %user%, %game%", "message", "Go follow @%user%, last seen playing %game%!");

        ui.ShowUI();
        return true;
    }
}
