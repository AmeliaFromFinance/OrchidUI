using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OrchidUI.Protocol;
using OrchidUI.Sandbox;
using Xunit;

namespace OrchidUI.Tests
{
    // The four examples compile and run unchanged; the specs they produce are golden files.
    [Collection("environment")]
    public class ExampleTests
    {
        public static IEnumerable<object[]> WindowExamples()
        {
            yield return new object[] { "settings" };
            yield return new object[] { "minimal" };
            yield return new object[] { "customised" };
        }

        // Paths in Streamer.bot's folder (a file picker's start folder) are resolved to full paths,
        // which differ per machine and clone; the golden files have "<Streamer.bot>/" instead.
        internal static string MachineNeutral(string json)
        {
            var dir = Json.Serialize(Orchid.StreamerBotDirectory, false);
            dir = dir.Substring(1, dir.Length - 2);
            return json.Replace(dir, "<Streamer.bot>/");
        }

        [Theory]
        [MemberData(nameof(WindowExamples))]
        public void Example_spec_matches_its_golden_file(string name)
        {
            using (Env.Clear("ORCHIDUI_THEME", "ORCHIDUI_SCALE", "ORCHIDUI_ANIMATIONS"))
            {
                var spec = ExampleRunner.Dump(ExampleRunner.FileFor(Repo.Examples, name), new FakeCph());
                Assert.NotNull(spec);
                Golden.Verify("specs", name + ".json", MachineNeutral(Json.Serialize(spec.ToJson(), true) + "\n"));
            }
        }

        [Fact]
        public void Environment_overrides_scale_and_backdrop_and_ignores_the_Windows_only_switches()
        {
            Func<WindowSpec> dump = () => ExampleRunner.Dump(ExampleRunner.FileFor(Repo.Examples, "minimal"), new FakeCph());
            WindowSpec plain;
            using (Env.Clear("ORCHIDUI_THEME", "ORCHIDUI_SCALE", "ORCHIDUI_ANIMATIONS", "ORCHIDUI_BACKDROP", "ORCHIDUI_COMPAT", "ORCHIDUI_RENDER", "ORCHIDUI_CHROME"))
                plain = dump();
            Assert.Equal("mica", Json.Str(plain.Window, "backdrop"));
            Assert.Equal(1.0, plain.Zoom);

            using (Env.Set("ORCHIDUI_SCALE", "1.5", "ORCHIDUI_BACKDROP", "Solid"))
            {
                var spec = dump();
                Assert.Equal(1.5, spec.Zoom);
                Assert.Equal("solid", Json.Str(spec.Window, "backdrop"));
            }

            using (Env.Set("ORCHIDUI_COMPAT", "1", "ORCHIDUI_RENDER", "Software", "ORCHIDUI_CHROME", "Native"))
            {
                var spec = dump();
                Assert.Equal(Json.Serialize(plain.ToJson(), true), Json.Serialize(spec.ToJson(), true));
            }
        }

        [Fact]
        public void The_runtime_example_reads_what_the_settings_example_saved()
        {
            var cph = new FakeCph { Streaming = true };
            cph.Args["tier"] = "tier 2";
            cph.Args["user"] = "PixelPancake";
            ExampleRunner.Dump(ExampleRunner.FileFor(Repo.Examples, "settings"), cph);
            Assert.NotNull(cph.Raw("orchid_HYPE TIMER"));
            Assert.True(ExampleRunner.Run(ExampleRunner.Compile(ExampleRunner.FileFor(Repo.Examples, "runtime")), cph));
            Assert.Equal(new[] { "@PixelPancake added 5:00 to the timer!" }, cph.Messages.ToArray());
            Assert.Contains(cph.Logged("INFO"), l => l.Contains("Added 300s. Start time 06:00:00, 3 goals, 0 rewards."));
        }

        [Fact]
        public void Every_example_compiles_against_the_shim_unchanged()
        {
            foreach (var name in ExampleRunner.Names)
                Assert.NotNull(ExampleRunner.Compile(ExampleRunner.FileFor(Repo.Examples, name)));
        }

        [Fact]
        public void The_settings_example_marks_later_fields_as_not_yet_supported_and_keeps_their_values()
        {
            var cph = new FakeCph();
            var spec = ExampleRunner.Dump(ExampleRunner.FileFor(Repo.Examples, "settings"), cph);
            var unsupported = spec.Fields.Where(f => f.Kind == FieldKinds.Unsupported).Select(f => f.PropStr("api")).ToArray();
            Assert.Empty(unsupported);
            Assert.Contains("\"goals\":[[\"Subs\",\"50\",\"Cooking stream\"]", cph.Raw("orchid_HYPE TIMER"));
        }

        [Fact]
        public void AddCustom_compiles_and_logs_one_warning()
        {
            var cph = new FakeCph();
            var o = new Orchid(cph, "CUSTOM");
            o.AddCustom("General", () => "a WPF element");
            WindowSpec spec = null;
            Orchid.ShowHook = (x, s) => spec = s;
            try { o.ShowUI(); }
            finally { Orchid.ShowHook = null; }
            Assert.Equal(FieldKinds.Custom, spec.Fields.Single().Kind);
            Assert.Single(cph.Logged("WARN"), l => l.Contains("AddCustom") && l.Contains("placeholder"));
        }
    }

    // The JSON stored in orchid_{NAME} must match the golden files byte for byte.
    [Collection("environment")]
    public class SavedSettingsTests
    {
        private static string Run(string extension, string stored, Action<Orchid> build, bool save)
        {
            var cph = new FakeCph();
            if (stored != null) cph.SetGlobalVar(Orchid.StorageKeyFor(extension), stored, true);
            var o = new Orchid(cph, extension, "1.0.0");
            build(o);
            Orchid.ShowHook = (x, s) => { };
            try { o.ShowUI(); }
            finally { Orchid.ShowHook = null; }
            o.NormaliseForWindow();
            if (save) Assert.True(o.SaveInternal(true));
            return cph.Raw(Orchid.StorageKeyFor(extension));
        }

        private static void HypeTimer(Orchid ui)
        {
            var g = ui.Tab("General");
            g.AddToggleSwitch("Enabled", "", "enabled", true);
            g.AddDurationPicker("Starting Time", "", "startingTime", 0, 6, 0, 0);
            g.AddSegmented("Time Format", "", "timeFormat", new[] { "dd:hh:mm:ss", "hh:mm:ss", "hh:mm" }, 1);
            g.AddDropdown("Currency", "", "currency", new[] { "USD", "EUR", "GBP" });
            g.AddDecimalStepper("Multiplier", "", "happyHour_multiplier", 0.05, 10, 0.05, 0.5);
            g.AddSlider("Duration", "", "happyHour_duration", 10, 86400, 3600).AsDuration();
            var e = ui.Tab("Events");
            e.AddSliderWithToggleSwitch("Tier 1", "", "time_t1", 1, 3600, 180, true);
            e.AddIntegerStepper("Minimum Cheer", "", "bits_minimum", 1, 100000, 100, 50);
            var l = ui.Tab("Appearance");
            l.AddColorPicker("Font Color", "", "fontColor", "#FFFFFF");
            l.AddColorPicker("Background", "", "backgroundColor", "#CC1A1324", allowAlpha: true);
            l.AddTextbox("Font", "", "font", "Poppins");
            ui.Tab("Goals").AddSelectionAmountTextList("Goal List", "", "goals", new[] { "Subs", "Bits" }, new[] { "Subs|50|Cooking stream" });
        }

        [Fact]
        public void Fresh_install_stores_the_defaults()
        {
            Golden.Verify("settings", "fresh.json", Run("HYPE TIMER", null, HypeTimer, false) + "\n");
        }

        [Fact]
        public void Odd_stored_values_are_normalised_and_saved()
        {
            var stored = File.ReadAllText(Path.Combine(Repo.Fixtures, "settings", "odd-input.json")).Trim();
            Golden.Verify("settings", "odd-saved.json", Run("HYPE TIMER", stored, HypeTimer, true) + "\n");
        }

        [Fact]
        public void Keys_the_window_doesnt_know_survive_a_save()
        {
            var saved = Run("KEEP", "{\"other\":\"kept\",\"enabled\":false}", ui => ui.Tab("T").AddToggleSwitch("Enabled", "", "enabled", true), true);
            Assert.Equal("{\"other\":\"kept\",\"enabled\":false}", saved);
        }
    }
}
