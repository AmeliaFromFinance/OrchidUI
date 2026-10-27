// HYPE TIMER: runtime action (e.g. triggered by Twitch Subscription)
// Reads what the user configured in the settings window. Needs the OrchidUI.dll reference too.
using System;
using OrchidUI;

public class CPHInline
{
    public bool Execute()
    {
        var cfg = OrchidSettings.Load(CPH, "HYPE TIMER");

        if (!cfg.GetBool("enabled", true)) return true;
        if (cfg.GetBool("onlyWhenLive", true) && !CPH.ObsIsStreaming()) return true;

        // Slider + switch fields store "{key}" and "{key}_enabled".
        string tier = "tier 1";
        CPH.TryGetArg("tier", out tier);
        string key = tier == "prime" ? "time_prime"
                   : tier == "tier 2" ? "time_t2"
                   : tier == "tier 3" ? "time_t3"
                   : "time_t1";
        if (!cfg.IsEnabled(key)) return true;

        int seconds = cfg.GetInt(key, 180);
        TimeSpan start = cfg.GetDuration("startingTime");   // "00:06:00:00" -> 6 hours
        string[][] goals = cfg.GetRows("goals");            // [ ["Subs","50","Cooking stream"], ... ]
        string[] rewards = cfg.GetList("rewards");

        string user;
        CPH.TryGetArg("user", out user);
        string message = cfg.GetString("chat_timeAdded")
            .Replace("%user%", user ?? "someone")
            .Replace("%addedSeconds%", seconds.ToString())
            .Replace("%addedTime%", TimeSpan.FromSeconds(seconds).ToString(@"m\:ss"));
        if (message.Trim() != "-") CPH.SendMessage(message);

        CPH.LogInfo("Added " + seconds + "s. Start time " + start + ", " + goals.Length + " goals, " + rewards.Length + " rewards.");
        return true;
    }
}

// No DLL reference? The settings are plain JSON in a global variable:
//
//   using Newtonsoft.Json.Linq;
//   var cfg  = JObject.Parse(CPH.GetGlobalVar<string>("orchid_HYPE TIMER", true));
//   int secs = cfg["time_t1"]?.Value<int>() ?? 180;
//   bool on  = cfg["time_t1_enabled"]?.Value<bool>() ?? true;
