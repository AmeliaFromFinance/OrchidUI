// HYPE TIMER: settings action
// Paste into an "Execute C# Code" sub-action, add OrchidUI.dll under References,
// then run the action (e.g. with a Test trigger) to open the settings window.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OrchidUI;

public class CPHInline
{
    const string Extension = "HYPE TIMER";
    const string Version = "1.0.0";

    private Orchid ui;

    public bool Execute()
    {
        if (Orchid.AlreadyOpened(Extension)) return true;

        ui = new Orchid(CPH, Extension, Version)
            .SetTheme("Orchid")
            .SetSubtitle("by you")
            .AddLink("Docs", "https://example.com/docs")
            .AddHeaderText("Hype Timer", "Every sub, cheer and raid adds time. Settings live in `orchid_HYPE TIMER`.")
            .SetFooterText("Need help? https://example.com/discord");

        var general = ui.Tab("General", Icons.Settings);
        general.AddTitle("General Settings");
        general.AddToggleSwitch("Enabled", "Turn the whole timer on or off.", "enabled", true).WithIcon(Icons.Play);
        general.AddDurationPicker("Starting Time", "Time on the clock when the timer starts.", "startingTime", 0, 6, 0, 0).WithIcon(Icons.Clock);
        general.AddSegmented("Time Format", "How the overlay shows the remaining time.", "timeFormat",
            new[] { "dd:hh:mm:ss", "hh:mm:ss", "hh:mm" }, 1).WithIcon(Icons.Stopwatch);
        general.AddDropdown("Currency", "Donations are converted into this currency.", "currency",
            new[] { "USD", "EUR", "GBP", "CAD", "AUD", "JPY", "SEK", "NOK", "DKK", "PLN", "BRL" }).WithIcon(Icons.Money);

        general.AddSeparator();
        general.AddTitle("Time Limit");
        general.AddToggleSwitch("Enable Time Limit", "Stop adding time once the limit is reached.", "timeLimit_enabled", false).WithIcon(Icons.Timer);
        general.AddDurationPicker("Maximum Added Time", "The most time contributions can add.", "timeLimit_maxTime", 1, 0, 0, 0)
               .WithIcon(Icons.Clock)
               .ShowWhen("timeLimit_enabled", true);

        general.AddSeparator();
        general.AddTitle("Happy Hour");
        general.AddInfoBox("Start a happy hour with **!happyhour** to multiply all added time for a while.");
        general.AddDecimalStepper("Multiplier", "1 = +100% (double time).", "happyHour_multiplier", 0.05, 10, 0.05, 0.5).WithIcon(Icons.Lightning);
        general.AddSlider("Duration", "How long a happy hour lasts, in seconds.", "happyHour_duration", 10, 86400, 3600)
               .WithIcon(Icons.Timer)
               .AsDuration();

        var events = ui.Tab("Events", Icons.Lightning);
        events.AddTitle("Subscriptions");
        events.AddDescription("Seconds added per event. Use the switch to turn an event type off.");
        events.AddSliderWithToggleSwitch("Prime", "Seconds per Prime sub.", "time_prime", 1, 3600, 180, true).AsDuration().WithIcon(Icons.Star);
        events.AddSliderWithToggleSwitch("Tier 1", "Seconds per Tier 1 sub.", "time_t1", 1, 3600, 180, true).AsDuration().WithIcon(Icons.Star);
        events.AddSliderWithToggleSwitch("Tier 2", "Seconds per Tier 2 sub.", "time_t2", 1, 3600, 300, true).AsDuration().WithIcon(Icons.Star);
        events.AddSliderWithToggleSwitch("Tier 3", "Seconds per Tier 3 sub.", "time_t3", 1, 3600, 600, true).AsDuration().WithIcon(Icons.Star);

        events.AddSeparator();
        events.AddTitle("Bits & Raids");
        events.AddSliderWithToggleSwitch("Bits", "Seconds per 100 bits.", "time_bits", 1, 3600, 60, true).AsDuration().WithIcon(Icons.Gift);
        events.AddIntegerStepper("Minimum Cheer", "Cheers below this amount add nothing.", "bits_minimum", 1, 100000, 100, 50)
              .WithIcon(Icons.Filter)
              .EnableWhen("time_bits_enabled", true);
        events.AddSliderWithToggleSwitch("Raids", "Seconds per 10 raiders.", "time_raid", 1, 3600, 120, false).AsDuration().WithIcon(Icons.People);

        events.AddSeparator();
        events.AddTitle("Channel Point Rewards");
        events.AddMultiSelect("Contributing Rewards", "Redemptions of these rewards add time. Click refresh after creating new rewards.",
            "rewards", () => CPH.TwitchGetRewards().Select(r => r.Title).OrderBy(t => t))
              .WithPlaceholder("Add a reward…")
              .WithIcon(Icons.Heart);

        var look = ui.Tab("Appearance", Icons.Palette);
        look.AddTitle("Overlay Style");
        look.AddDropdown("Font", "Font used by the overlay.", "font",
            new[] { "Poppins", "Inter", "Montserrat", "Nunito", "Bebas Neue", "Quicksand", "Roboto" }).WithIcon(Icons.Font);
        look.AddColorPicker("Font Color", "Main text colour.", "fontColor", "#FFFFFF").WithIcon(Icons.Palette);
        look.AddColorPicker("Background", "Card background, with transparency.", "backgroundColor", "#CC1A1324", allowAlpha: true).WithIcon(Icons.Brightness);
        look.AddSlider("Corner Radius", "Roundness of the cards in pixels.", "cornerRadius", 0, 40, 12).WithFormat(v => v + " px").WithIcon(Icons.Edit);
        look.AddDecimalStepper("Zoom", "1 = 100%.", "zoom", 0.5, 3, 0.1, 1).WithIcon(Icons.Zoom);
        look.AddImageInput("Logo", "Image URL or file path shown above the timer.", "logo", "").WithIcon(Icons.Photo);
        look.AddCopyBox("Overlay URL", "Add this as a Browser Source in OBS.", "http://localhost:7474/hype-timer").WithIcon(Icons.Link);

        var goals = ui.Tab("Goals", Icons.Trophy);
        goals.AddTitle("Goals");
        goals.AddSelectionAmountTextList("Goal List", "Each row is one goal: what counts, how many, and the reward text.", "goals",
            new[] { "Subs", "Bits", "Donations", "Raids", "Combined" },
            new[] { "Subs|50|Cooking stream", "Bits|10000|Horror game night", "Combined|250|Dye hair pink" },
            "Counts", "Target", "Reward").WithIcon(Icons.Flag);
        goals.AddTextList("Rotator Messages", "Shown one after another under the timer.", "rotator",
            new[] { "A sub adds 3 minutes!", "100 bits add 1 minute!" }, "Type a message…")
             .WithIcon(Icons.List)
             .Collapsed();

        var chat = ui.Tab("Chat", Icons.Chat);
        chat.AddTitle("Chat Responses");
        chat.AddDescription("Leave a field as a single `-` to disable that message.");
        chat.AddResponseBox("Time Added", "Variables: %user%, %addedTime%, %addedSeconds%", "chat_timeAdded",
            "@%user% added %addedTime% to the timer!")
            .Required();
        chat.AddResponseBox("Timer Finished", "Variables: %totalTime%", "chat_finished",
            "The timer is over! We streamed for %totalTime% 💜")
            .Validate(v => ((string)v).Length <= 500, "Twitch messages are limited to 500 characters.");

        var adv = ui.Tab("Advanced", Icons.Wrench);
        adv.AddTitle("Advanced");
        adv.AddToggleSwitch("Only Add When Live", "Ignore events while you're offline.", "onlyWhenLive", true).WithIcon(Icons.Video);
        adv.AddPasswordBox("API Token", "Stored in plain text inside Streamer.bot's global variables.", "apiToken").WithIcon(Icons.Key);

        string backupDir = Path.Combine(Orchid.StreamerBotDirectory, "HYPE TIMER Backups");
        adv.AddFilePicker("Restore Backup", "Pick a backup file and click Load.", "backupFile", "",
            "Backup files (*.json)|*.json", backupDir, LoadBackup, "Load").WithIcon(Icons.Save);
        adv.AddButton("Log Settings", "Writes every value to the Streamer.bot log.", "Log now", () =>
        {
            ui.LogSettings();
            ui.ShowToast("Settings written to the log", InfoKind.Success);
        }).WithIcon(Icons.Document);

        ui.ShowUI();
        return true;
    }

    // Runs on the settings window's thread when "Load" is clicked.
    private void LoadBackup()
    {
        string path = ui.GetPendingValue<string>("backupFile");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            ui.ShowMessage("No backup selected", "Choose a backup file first.");
            return;
        }
        if (!ui.ShowConfirm("Load backup?", "Load **" + Path.GetFileName(path) + "**?\n\nThis replaces the current timer state.", "Load", "Cancel"))
            return;

        CPH.SetGlobalVar("hypeTimer_state", File.ReadAllText(path), true);
        ui.ShowMessage("Backup loaded", "Timer state restored. Happy streaming!");
    }
}
