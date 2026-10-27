using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using OrchidUI.Protocol;
using OrchidUI.Sandbox;
using Xunit;

namespace OrchidUI.Tests
{
    [Collection("environment")]
    public class RegressionTests
    {
        [Fact]
        public void A_header_image_too_big_for_one_frame_is_dropped_with_a_warning_and_the_window_still_opens()
        {
            var image = Path.Combine(Path.GetTempPath(), "orchid-big-" + Guid.NewGuid().ToString("N") + ".png");
            var bytes = new byte[900 * 1024];
            new Random(7).NextBytes(bytes);
            File.WriteAllBytes(image, bytes);
            try
            {
                using (var host = FakeHost.Start("tcp", o =>
                {
                    o.AddHeader(image);
                    o.Tab("General").AddToggleSwitch("Enabled", "", "enabled", true);
                }).Open())
                {
                    Assert.True(host.Orchid.IsOpen);
                    Assert.Null(host.Spec.HeaderImage);
                    Assert.Contains(host.Cph.Logged("WARN"), l => l.Contains("Header image is too big"));
                }
            }
            finally { File.Delete(image); }
        }

        [Fact]
        public void A_spec_that_fits_keeps_its_embedded_files()
        {
            var spec = new WindowSpec { HeaderImage = new ImageData { Mime = "image/png", Base64 = new string('A', 1000) } };
            var warnings = new List<string>();
            WindowController.FitInOneFrame(spec, warnings.Add);
            Assert.NotNull(spec.HeaderImage);
            Assert.Empty(warnings);
        }

        [Fact]
        public void The_window_counts_as_closed_as_soon_as_the_link_drops_and_a_late_dialog_does_not_wait_out_its_timeout()
        {
            var oldTimeout = Orchid.DialogTimeout;
            var oldConnect = SessionLink.ConnectTimeout;
            Orchid.DialogTimeout = TimeSpan.FromSeconds(20);
            SessionLink.ConnectTimeout = TimeSpan.FromSeconds(1);
            var gate = new ManualResetEventSlim(false);
            var done = new ManualResetEventSlim(false);
            long elapsed = -1;
            try
            {
                Orchid ui = null;
                using (var host = FakeHost.Start("tcp", o =>
                {
                    ui = o;
                    o.Tab("T").AddButton("Slow", "", "Go", () =>
                    {
                        gate.Wait(TimeSpan.FromSeconds(20));
                        var sw = Stopwatch.StartNew();
                        ui.ShowMessage("Done", "Finished.");
                        elapsed = sw.ElapsedMilliseconds;
                        done.Set();
                    });
                }).Open())
                {
                    host.Send(MessageTypes.Click, new Dictionary<string, object> { { "field", host.Spec.Fields.Single().Id } });
                    Thread.Sleep(200);
                    host.Kill();
                    // The callback thread is busy in the button, so OnClosed hasn't run yet.
                    Assert.True(FakeHost.WaitUntil(() => !ui.IsOpen, 3), "IsOpen stayed true after the host went away.");
                    gate.Set();
                    Assert.True(done.Wait(TimeSpan.FromSeconds(15)), "The dialog after the drop never returned.");
                    Assert.True(elapsed < 10000, "The dialog waited " + elapsed + " ms.");
                }
            }
            finally
            {
                gate.Set();
                Orchid.DialogTimeout = oldTimeout;
                SessionLink.ConnectTimeout = oldConnect;
            }
        }

        [Fact]
        public void UpdateDropdown_inside_OnChange_runs_the_handler_once_instead_of_recursing()
        {
            int calls = 0;
            Orchid ui = null;
            using (var host = FakeHost.Start("tcp", o =>
            {
                ui = o;
                o.Tab("T").AddDropdown("Scene", "", "scene", new[] { "Live", "BRB" }).OnChange(v =>
                {
                    if (Interlocked.Increment(ref calls) > 20) return;
                    ui.UpdateDropdown("scene", new[] { "Live", "BRB", "Ending" });
                });
            }).Open())
            {
                host.Change("scene", "BRB");
                Assert.True(FakeHost.WaitUntil(() => calls >= 1));
                Thread.Sleep(500);
                Assert.Equal(1, calls);
            }
        }

        [Fact]
        public void SetValue_while_open_with_a_value_the_control_normalises_does_not_mark_the_window_dirty()
        {
            Orchid ui = null;
            using (var host = FakeHost.Start("tcp", o =>
            {
                ui = o;
                o.Tab("T").AddColorPicker("Accent", "", "accent", "#000000");
            }).Open())
            {
                ui.SetValue("accent", "#fff");
                var patch = host.Next(MessageTypes.Patch);
                Assert.Equal("#FFFFFF", Json.Str(Json.Obj(patch.Payload, "values"), "accent"));
                Assert.False(ui.IsDirty);
                Assert.Contains("\"accent\":\"#fff\"", host.Cph.Raw("orchid_TEST EXT"));
            }
        }

        [Fact]
        public void A_replaced_window_closing_late_does_not_unregister_its_successor()
        {
            using (var host = FakeHost.Start("tcp", o => o.Tab("T").AddToggleSwitch("On", "", "on", true)).Open())
            {
                var stale = new WindowController(host.Orchid, true);
                host.Orchid.OnWindowClosed(stale);
                Assert.True(Orchid.AlreadyOpened("TEST EXT"));
                Assert.True(host.Orchid.IsOpen);
            }
        }

        [Fact]
        public void A_standalone_dialog_closed_by_the_host_right_after_answering_logs_no_warning()
        {
            bool? answer = null;
            using (var host = FakeHost.StartWith("tcp", o => { }, o => answer = o.ShowConfirm("Sure?", "Really?")).OpenNoWait())
            {
                Assert.Equal("dialog", Json.Str(host.Spec.Window, "mode"));
                var dialog = host.Next(MessageTypes.Dialog);
                // Like the real host: answer and close the connection straight away.
                host.Send(MessageTypes.DialogResult, new Dictionary<string, object> { { "request", Json.Int(dialog.Payload, "request") }, { "index", -1 } });
                host.Kill();
                Assert.True(host.ShowTask.Wait(TimeSpan.FromSeconds(10)));
                Assert.False(answer);
                Thread.Sleep(200);
                Assert.Empty(host.Cph.Logged("WARN"));
                Assert.Empty(host.Cph.Logged("ERROR"));
            }
        }

        [Fact]
        public void An_untrusted_host_binary_is_reported_as_untrusted_so_Wine_does_not_wait_for_watch_mode()
        {
            var fake = Path.Combine(Path.GetTempPath(), "orchid-host-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(fake, "not the real host");
            var old = HostLauncher.TrustedHashes;
            HostLauncher.TrustedHashes = new[] { new string('0', 64) };
            try
            {
                var result = HostLauncher.Launch(fake, null, s => { });
                Assert.NotNull(result.Error);
                Assert.True(result.Untrusted);
                var missing = HostLauncher.Launch(fake + ".missing", null, s => { });
                Assert.False(missing.Untrusted);
            }
            finally
            {
                HostLauncher.TrustedHashes = old;
                File.Delete(fake);
            }
        }

        [Fact]
        public void Session_folders_under_Wine_prefer_private_locations()
        {
            Assert.True(SessionDirectory.IsPerUserTemp("/var/folders/ab/xyz123/T/"));
            Assert.False(SessionDirectory.IsPerUserTemp("/tmp"));
            Assert.False(SessionDirectory.IsPerUserTemp("/tmp/"));
            Assert.False(SessionDirectory.IsPerUserTemp("/var/tmp"));
            Assert.False(SessionDirectory.IsPerUserTemp(null));
            Assert.False(SessionDirectory.IsPerUserTemp("C:\\Temp"));
        }

        [Fact]
        public void Window_state_keeps_the_screen_scale_and_stays_readable_without_it()
        {
            var st = new UiState { W = 900, H = 600, X = 1500, Y = 100, HasPosition = true, Scale = 2 };
            var d = st.ToDictionary();
            Assert.Equal(2.0, d["scale"]);
            var back = UiState.From(Json.ParseObject(Json.Serialize(d, false)));
            Assert.Equal(2.0, back.Scale);
            var old = UiState.From(Json.ParseObject("{\"w\":900,\"h\":600,\"x\":10,\"y\":20,\"max\":false,\"zoom\":1,\"tab\":\"\"}"));
            Assert.Equal(0.0, old.Scale);
            Assert.False(new UiState { W = 1, H = 1 }.ToDictionary().ContainsKey("scale"));
        }
    }
}
