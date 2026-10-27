using System;
using System.Collections.Generic;
using System.Linq;
using OrchidUI.Protocol;
using Xunit;

namespace OrchidUI.Tests
{
    // The fields and window buttons OrchidUI draws (lists, tables, multi-selects,
    // refreshable dropdowns, pickers, Import/Export/Reset all/About), end to end through the shim.
    [Collection("environment")]
    public class EndToEndTests
    {
        private static Dictionary<string, object> Values(Frame patch) { return Json.Obj(patch.Payload, "values"); }

        [Fact]
        public void Lists_and_tables_are_drawn_natively_and_store_the_saved_format()
        {
            using (var host = FakeHost.Start("tcp", ui =>
            {
                var t = ui.Tab("Lists");
                t.AddTextList("Messages", "", "messages", new[] { "one", "two" }, "Type a message…");
                t.AddSelectionAmountTextList("Goals", "", "goals", new[] { "Subs", "Bits" }, new[] { "Subs|50|Cooking stream" }, "Counts", "Target", "Reward");
            }).Open())
            {
                var list = host.Spec.Fields.Single(f => f.Key == "messages");
                Assert.Equal(FieldKinds.List, list.Kind);
                Assert.True(list.PropBool("flat"));
                Assert.Equal("Type a message…", list.Placeholder);
                Assert.Equal("expander", list.Layout);
                var table = host.Spec.Fields.Single(f => f.Key == "goals");
                Assert.Equal(FieldKinds.List, table.Kind);
                var cols = ((object[])table.Props["columns"]).Cast<Dictionary<string, object>>().ToArray();
                Assert.Equal(new[] { "choice", "number", "text" }, cols.Select(c => Json.Str(c, "kind")).ToArray());
                Assert.Equal(new object[] { "Subs", "Bits" }, Json.Arr(cols[0], "options"));
                Assert.Equal("2 item(s)", host.Spec.States.Single(s => s.Id == list.Id).Summary);

                // A row with an empty choice and missing cells: the shim fills them and
                // sends the stored form back.
                host.Change("goals", new object[] { new object[] { "Subs", "50", "Cooking stream" }, new object[] { "" } });
                var values = Values(host.Next(MessageTypes.Patch));
                Assert.Equal(3, ((object[])((object[])values["goals"])[1]).Length);
                Assert.Equal("Subs", ((object[])((object[])values["goals"])[1])[0]);
                Assert.Equal("2 row(s)", host.NextState("goals").Summary);

                host.Change("messages", new object[] { "one", "two", "three" });
                Assert.Equal("3 item(s)", host.NextState("messages").Summary);
                host.Command(Commands.Save);
                host.NextStatus(s => !s.Dirty);
                Assert.Contains("\"messages\":[\"one\",\"two\",\"three\"]", host.Cph.Raw("orchid_TEST EXT"));
                Assert.Contains("\"goals\":[[\"Subs\",\"50\",\"Cooking stream\"],[\"Subs\",\"\",\"\"]]", host.Cph.Raw("orchid_TEST EXT"));
            }
        }

        [Fact]
        public void Refreshing_a_multi_select_or_dropdown_runs_its_loader_or_callback_in_streamerbot()
        {
            int loads = 0;
            Orchid ui = null;
            using (var host = FakeHost.Start("tcp", o =>
            {
                ui = o;
                var t = o.Tab("Rewards");
                t.AddMultiSelect("Rewards", "", "rewards", () => { loads++; return loads == 1 ? new[] { "Hydrate" } : new[] { "Hydrate", "Stretch" }; }, new[] { "Hydrate" });
                t.AddRefreshableDropdown("Scene", "", "scene", new[] { "Main" }, () => ui.UpdateDropdown("scene", new[] { "Main", "Starting Soon" }));
                t.AddRefreshableDropdown("Game", "", "game", () => { throw new InvalidOperationException("offline"); });
            }).Open())
            {
                var ms = host.Spec.Fields.Single(f => f.Key == "rewards");
                Assert.Equal(FieldKinds.MultiSelect, ms.Kind);
                Assert.True(ms.PropBool("refreshable"));
                Assert.Equal("1 selected", host.Spec.States.Single(s => s.Id == ms.Id).Summary);
                Assert.Equal(FieldKinds.Dropdown, host.Spec.Fields.Single(f => f.Key == "scene").Kind);
                Assert.True(host.Spec.Fields.Single(f => f.Key == "scene").PropBool("refreshable"));
                Assert.DoesNotContain(host.Spec.Fields, f => f.Kind == FieldKinds.Unsupported);

                host.Command(Commands.Refresh, ms.Id);
                var fields = Json.Arr(host.Next(MessageTypes.Patch).Payload, "fields");
                Assert.Equal(new object[] { "Hydrate", "Stretch" }, Json.Arr(Json.Obj((Dictionary<string, object>)fields[0], "props"), "options"));
                Assert.Equal(2, loads);
                Assert.Contains("Rewards", Json.Str(host.Next(MessageTypes.Toast).Payload, "message"));

                host.Command(Commands.Refresh, host.Spec.Fields.Single(f => f.Key == "scene").Id);
                Assert.True(FakeHost.WaitUntil(() => ui.GetPendingValue<string>("scene") == "Main"));
                fields = Json.Arr(host.Next(MessageTypes.Patch).Payload, "fields");
                Assert.Equal(new object[] { "Main", "Starting Soon" }, Json.Arr(Json.Obj((Dictionary<string, object>)fields[0], "props"), "options"));

                // A loader that throws is logged; the list stays.
                host.Command(Commands.Refresh, host.Spec.Fields.Single(f => f.Key == "game").Id);
                Assert.True(FakeHost.WaitUntil(() => host.Cph.Logged("WARN").Any(l => l.Contains("offline"))));

                host.Change("rewards", new object[] { "Hydrate", "Stretch" });
                Assert.Equal("2 selected", host.NextState("rewards").Summary);
            }
        }

        [Fact]
        public void Pickers_store_paths_as_streamerbot_sees_them_and_run_their_action()
        {
            string loaded = null;
            Orchid ui = null;
            var png = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "orchid-preview-" + Guid.NewGuid().ToString("N") + ".png");
            System.IO.File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
            try
            {
                using (var host = FakeHost.Start("tcp", o =>
                {
                    ui = o;
                    var t = o.Tab("Files");
                    t.AddFilePicker("Backup", "", "backupFile", "", "Backup files (*.json)|*.json", System.IO.Path.GetTempPath(), () => loaded = ui.GetPendingValue<string>("backupFile"), "Load");
                    t.AddFolderPicker("Clips", "", "clipFolder", "");
                    t.AddImageInput("Logo", "", "logo", "");
                }).Open())
                {
                    var file = host.Spec.Fields.Single(f => f.Key == "backupFile");
                    Assert.Equal(FieldKinds.Path, file.Kind);
                    Assert.False(file.PropBool("folder"));
                    Assert.Equal("Backup files (*.json)|*.json", file.PropStr("filter"));
                    Assert.Equal("Load", file.PropStr("actionText"));
                    Assert.Equal(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()), file.PropStr("start"));
                    Assert.True(host.Spec.Fields.Single(f => f.Key == "clipFolder").PropBool("folder"));
                    Assert.Null(host.Spec.Fields.Single(f => f.Key == "clipFolder").PropStr("actionText"));
                    Assert.Equal(FieldKinds.Image, host.Spec.Fields.Single(f => f.Key == "logo").Kind);

                    // Under Wine a path from the host's dialog is stored with Z:, and sent back.
                    OrchidPlatform.OverrideWine(true);
                    try
                    {
                        host.Send(MessageTypes.Change, new Dictionary<string, object> { { "field", file.Id }, { "key", "backupFile" }, { "value", "/home/quietfern/backup.json" }, { "native", true } });
                        Assert.Equal(@"Z:\home\quietfern\backup.json", Values(host.Next(MessageTypes.Patch))["backupFile"]);
                        // Typed text isn't a dialog path: kept as typed.
                        host.Change("clipFolder", "/home/quietfern/clips");
                        Assert.True(FakeHost.WaitUntil(() => ui.GetPendingValue<string>("clipFolder") == "/home/quietfern/clips"));
                    }
                    finally { OrchidPlatform.OverrideWine(null); }

                    host.Send(MessageTypes.Click, new Dictionary<string, object> { { "field", file.Id } });
                    Assert.True(FakeHost.WaitUntil(() => loaded != null));
                    Assert.Equal(@"Z:\home\quietfern\backup.json", loaded);

                    // The image input's preview: the file's bytes, a URL as it is, or why not.
                    host.Change("logo", png);
                    var state = host.NextState("logo");
                    Assert.Equal("image/png", state.Image.Mime);
                    Assert.NotNull(state.Image.Base64);
                    host.Change("logo", "https://example.com/logo.png");
                    Assert.Equal("https://example.com/logo.png", host.NextState("logo").Image.Url);
                    host.Change("logo", png + ".missing");
                    state = host.NextState("logo");
                    Assert.Null(state.Image);
                    Assert.Equal("File not found", state.Display);
                }
            }
            finally { System.IO.File.Delete(png); }
        }

        private static void Answer(FakeHost host, int index, string titleContains)
        {
            var dialog = host.Next(MessageTypes.Dialog);
            Assert.Contains(titleContains, Json.Str(dialog.Payload, "title"));
            host.Send(MessageTypes.DialogResult, new Dictionary<string, object> { { "request", Json.Int(dialog.Payload, "request") }, { "index", index } });
        }

        private static void Build(Orchid ui)
        {
            ui.SetSubtitle("by PixelPancake").AddLink("Docs", "https://example.com/docs");
            var t = ui.Tab("General");
            t.AddToggleSwitch("Enabled", "", "enabled", true);
            t.AddIntegerStepper("Minimum", "", "minimum", 1, 100, 5);
            t.AddTextList("Messages", "", "messages", new[] { "hi" });
        }

        [Fact]
        public void Reset_all_asks_then_restores_every_default_without_saving()
        {
            using (var host = FakeHost.Start("tcp", Build).Open())
            {
                Assert.True(Json.Bool(host.Spec.Layout, "showResetAll"));
                Assert.True(Json.Bool(host.Spec.Layout, "showImportExport"));
                Assert.True(Json.Bool(host.Spec.Layout, "showAbout"));
                host.Change("minimum", 42);
                host.NextStatus(s => s.Dirty);

                // Cancel keeps everything.
                host.Command(Commands.ResetAll);
                Answer(host, 0, "Reset");
                host.Command(Commands.ResetAll);
                Answer(host, 1, "Reset");
                var values = Values(host.Next(MessageTypes.Patch));
                Assert.Equal(5, Convert.ToInt32(values["minimum"]));
                Assert.Equal(true, values["enabled"]);
                Assert.Contains("Click Save", Json.Str(host.Next(MessageTypes.Toast).Payload, "message"));
                Assert.Contains("\"minimum\":5", host.Cph.Raw("orchid_TEST EXT"));
            }
        }

        [Fact]
        public void Export_then_import_round_trips_the_window_values()
        {
            var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "orchid-export-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                using (var host = FakeHost.Start("tcp", Build).Open())
                {
                    host.Change("minimum", 42);
                    host.NextStatus(s => s.Dirty);
                    host.Send(MessageTypes.Command, new Dictionary<string, object> { { "name", Commands.Export }, { "path", file }, { "native", true } });
                    Assert.Contains("Exported to", Json.Str(host.Next(MessageTypes.Toast).Payload, "message"));
                    var exported = Json.ParseObject(System.IO.File.ReadAllText(file));
                    Assert.Equal(42, Convert.ToInt32(exported["minimum"]));
                    Assert.Equal(new object[] { "hi" }, (object[])exported["messages"]);

                    // An edited file: a value out of range is normalised, an unknown key is ignored.
                    System.IO.File.WriteAllText(file, "{\"minimum\": 500, \"enabled\": false, \"messages\": [\"one\", \"two\"], \"someoneElses\": 1}");
                    host.Send(MessageTypes.Command, new Dictionary<string, object> { { "name", Commands.Import }, { "path", file } });
                    var values = Values(host.Next(MessageTypes.Patch));
                    Assert.Equal(100, Convert.ToInt32(values["minimum"]));
                    Assert.Equal(false, values["enabled"]);
                    Assert.False(values.ContainsKey("someoneElses"));
                    var dialog = host.Next(MessageTypes.Dialog);
                    Assert.Contains("**3**", Json.Str(dialog.Payload, "message"));
                    host.Send(MessageTypes.DialogResult, new Dictionary<string, object> { { "request", Json.Int(dialog.Payload, "request") }, { "index", 0 } });
                    // Not stored until Save.
                    Assert.DoesNotContain("\"minimum\":100", host.Cph.Raw("orchid_TEST EXT"));
                    host.Command(Commands.Save);
                    host.NextStatus(s => !s.Dirty);
                    Assert.Contains("\"minimum\":100", host.Cph.Raw("orchid_TEST EXT"));

                    // A file that isn't JSON: an error dialog, nothing changed.
                    System.IO.File.WriteAllText(file, "not json");
                    host.Send(MessageTypes.Command, new Dictionary<string, object> { { "name", Commands.Import }, { "path", file } });
                    Answer(host, 0, "Something went wrong");
                    Assert.Equal(100, ui(host).GetPendingValue<int>("minimum"));
                }
            }
            finally { System.IO.File.Delete(file); }
        }

        private static Orchid ui(FakeHost host) { return host.Orchid; }

        [Fact]
        public void About_shows_the_extension_and_copies_diagnostics()
        {
            using (var host = FakeHost.Start("tcp", Build).Open())
            {
                host.Command(Commands.About);
                var dialog = host.Next(MessageTypes.Dialog);
                var message = Json.Str(dialog.Payload, "message");
                Assert.Contains("**TEST EXT** 1.0.0", message);
                Assert.Contains("by PixelPancake", message);
                Assert.Contains("Docs: https://example.com/docs", message);
                Assert.Equal(new object[] { "Copy diagnostics", "Close" }, Json.Arr(dialog.Payload, "buttons"));
                host.Send(MessageTypes.DialogResult, new Dictionary<string, object> { { "request", Json.Int(dialog.Payload, "request") }, { "index", 0 } });
                var copy = host.Next(MessageTypes.Clipboard);
                Assert.Contains("Extension: TEST EXT 1.0.0", Json.Str(copy.Payload, "text"));
                Assert.Contains("Storage key: orchid_TEST EXT", Json.Str(copy.Payload, "text"));
            }
        }

        [Fact]
        public void The_more_menu_can_be_hidden()
        {
            using (var host = FakeHost.Start("tcp", o =>
            {
                Build(o);
                o.ConfigureLayout(l => { l.ShowImportExport = false; l.ShowResetAll = false; l.ShowAbout = false; });
            }).Open())
            {
                Assert.False(Json.Bool(host.Spec.Layout, "showImportExport", true));
                Assert.False(Json.Bool(host.Spec.Layout, "showResetAll", true));
                Assert.False(Json.Bool(host.Spec.Layout, "showAbout", true));
            }
        }
    }
}
