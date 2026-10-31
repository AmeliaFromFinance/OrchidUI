using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using OrchidUI.Protocol;
using OrchidUI.Sandbox;
using Xunit;

namespace OrchidUI.Tests
{
    // The shim end to end over real transports, with FakeCph standing in for Streamer.bot.
    [Collection("environment")]
    public class SessionTests
    {
        public static IEnumerable<object[]> Transports()
        {
            yield return new object[] { "tcp" };
            yield return new object[] { "pipe" };
        }

        private static void HypeTimer(Orchid ui)
        {
            var g = ui.Tab("General", Icons.Settings);
            g.AddToggleSwitch("Enabled", "Turn the timer on or off.", "enabled", true);
            g.AddToggleSwitch("Enable Time Limit", "", "timeLimit_enabled", false);
            g.AddDurationPicker("Maximum Added Time", "", "timeLimit_maxTime", 1, 0, 0, 0).ShowWhen("timeLimit_enabled", true);
            g.AddIntegerStepper("Minimum Cheer", "", "bits_minimum", 1, 100000, 100).EnableWhen("enabled", true);
            g.AddSlider("Duration", "", "happyHour_duration", 10, 86400, 3600).AsDuration();
            var chat = ui.Tab("Chat", Icons.Chat);
            chat.AddResponseBox("Time Added", "", "chat_timeAdded", "@%user% added time").Required();
            chat.AddTextbox("Webhook", "", "webhook").Validate(v => string.IsNullOrEmpty((string)v) || ((string)v).StartsWith("https://"), "Must start with https://");
            chat.AddPasswordBox("API Token", "", "apiToken");
        }

        [Theory]
        [MemberData(nameof(Transports))]
        public void Opens_edits_saves_and_closes(string transport)
        {
            using (var host = FakeHost.Start(transport, HypeTimer).Open())
            {
                Assert.True(host.Orchid.IsOpen);
                Assert.Equal("TEST EXT", host.Spec.Extension);
                Assert.Equal(new[] { "General", "Chat" }, host.Spec.Tabs.Select(t => t.Name).ToArray());
                Assert.False(File.Exists(host.SessionFilePath), "The session file must be gone after the handshake.");

                host.Change("enabled", false);
                var state = host.NextState("bits_minimum");
                Assert.False(state.Enabled);
                var status = host.NextStatus(s => s.Dirty);
                Assert.Equal("Unsaved changes", status.Text);

                host.Command(Commands.Save);
                host.NextStatus(s => !s.Dirty);
                Assert.Equal("Settings saved", Json.Str(host.Next(MessageTypes.Toast).Payload, "message"));
                Assert.Contains("\"enabled\":false", host.Cph.Raw("orchid_TEST EXT"));

                host.Send(MessageTypes.CloseRequest, new Dictionary<string, object> { { "state", new Dictionary<string, object> { { "w", 900.0 }, { "h", 640.0 }, { "x", 120.0 }, { "y", 80.0 }, { "max", false }, { "zoom", 1.2 }, { "tab", "t2" } } } });
                host.Next(MessageTypes.Close);
                Assert.True(FakeHost.WaitUntil(() => !host.Orchid.IsOpen));
                var window = Json.ParseObject(host.Cph.Raw("orchid_TEST EXT.window"));
                Assert.Equal(900, Convert.ToDouble(window["w"]));
                Assert.Equal("Chat", window["tab"]);
                Assert.Equal(1.2, Convert.ToDouble(window["zoom"]));
            }
        }

        [Fact]
        public void ShowWhen_and_validation_run_in_the_shim_and_come_back_as_state()
        {
            using (var host = FakeHost.Start("tcp", HypeTimer).Open())
            {
                var maxTime = host.Spec.States.First(s => s.Id == host.Spec.Fields.First(f => f.Key == "timeLimit_maxTime").Id);
                Assert.False(maxTime.Visible);
                host.Change("timeLimit_enabled", true);
                Assert.True(host.NextState("timeLimit_maxTime").Visible);

                host.Change("webhook", "http://nope");
                Assert.Equal("Must start with https://", host.NextState("webhook").Error);
                var status = host.NextStatus(s => s.Errors > 0);
                Assert.False(status.CanSave);
                Assert.Equal("1 setting(s) need attention", status.Text);

                host.Command(Commands.Save);
                Assert.Equal("danger", Json.Str(host.Next(MessageTypes.Toast).Payload, "kind"));
                Assert.Equal(host.Spec.Fields.First(f => f.Key == "webhook").Id, Json.Str(host.Next(MessageTypes.Jump).Payload, "field"));
                Assert.DoesNotContain("http://nope", host.Cph.Raw("orchid_TEST EXT"));
            }
        }

        [Fact]
        public void WithFormat_text_is_worked_out_in_the_shim()
        {
            using (var host = FakeHost.Start("tcp", HypeTimer).Open())
            {
                var id = host.Spec.Fields.First(f => f.Key == "happyHour_duration").Id;
                Assert.Equal("1h", host.Spec.States.First(s => s.Id == id).Display);
                host.Change("happyHour_duration", 5400);
                Assert.Equal("1h 30m", host.NextState("happyHour_duration").Display);
            }
        }

        [Fact]
        public void Out_of_range_input_is_clamped_and_sent_back()
        {
            using (var host = FakeHost.Start("tcp", HypeTimer).Open())
            {
                host.Change("bits_minimum", 999999);
                var patch = host.Next(MessageTypes.Patch);
                Assert.Equal(100000, Json.Int(Json.Obj(patch.Payload, "values"), "bits_minimum"));
            }
        }

        [Fact]
        public void Closing_with_unsaved_changes_asks_and_saves_when_told_to()
        {
            using (var host = FakeHost.Start("tcp", HypeTimer).Open())
            {
                host.Change("enabled", false);
                host.NextStatus(s => s.Dirty);
                host.Send(MessageTypes.CloseRequest, new Dictionary<string, object>());
                var dialog = host.Next(MessageTypes.Dialog);
                Assert.Equal("Save your changes?", Json.Str(dialog.Payload, "title"));
                host.Send(MessageTypes.DialogResult, new Dictionary<string, object> { { "request", Json.Int(dialog.Payload, "request") }, { "index", 2 } });
                host.Next(MessageTypes.Close);
                Assert.Contains("\"enabled\":false", host.Cph.Raw("orchid_TEST EXT"));
            }
        }

        [Fact]
        public void Cancelling_the_close_dialog_keeps_the_window()
        {
            using (var host = FakeHost.Start("tcp", HypeTimer).Open())
            {
                host.Change("enabled", false);
                host.NextStatus(s => s.Dirty);
                host.Send(MessageTypes.CloseRequest, new Dictionary<string, object>());
                var dialog = host.Next(MessageTypes.Dialog);
                host.Send(MessageTypes.DialogResult, new Dictionary<string, object> { { "request", Json.Int(dialog.Payload, "request") }, { "index", 0 } });
                Assert.Throws<TimeoutException>(() => host.Next(MessageTypes.Close, 1));
                Assert.True(host.Orchid.IsOpen);
            }
        }

        [Fact]
        public void Buttons_run_their_lambda_in_streamerbot_and_can_show_dialogs()
        {
            Orchid ui = null;
            bool? answer = null;
            using (var host = FakeHost.Start("tcp", o =>
            {
                ui = o;
                o.Tab("T").AddButton("Reset", "", "Reset now", () => answer = ui.ShowConfirm("Sure?", "Really?"));
            }).Open())
            {
                host.Send(MessageTypes.Click, new Dictionary<string, object> { { "field", host.Spec.Fields.Single().Id } });
                var dialog = host.Next(MessageTypes.Dialog);
                Assert.Equal(new object[] { "No", "Yes" }, Json.Arr(dialog.Payload, "buttons"));
                host.Send(MessageTypes.DialogResult, new Dictionary<string, object> { { "request", Json.Int(dialog.Payload, "request") }, { "index", 1 } });
                Assert.True(FakeHost.WaitUntil(() => answer.HasValue));
                Assert.True(answer.Value);
            }
        }

        [Fact]
        public void A_throwing_callback_is_logged_and_shown_not_fatal()
        {
            using (var host = FakeHost.Start("tcp", o => o.Tab("T").AddButton("Boom", "", "Go", () => { throw new InvalidOperationException("kaboom"); })).Open())
            {
                host.Send(MessageTypes.Click, new Dictionary<string, object> { { "field", host.Spec.Fields.Single().Id } });
                var dialog = host.Next(MessageTypes.Dialog);
                Assert.Contains("kaboom", Json.Str(dialog.Payload, "message"));
                Assert.Contains(host.Cph.Logged("ERROR"), l => l.Contains("kaboom"));
                Assert.True(host.Orchid.IsOpen);
            }
        }

        [Fact]
        public void A_host_crash_is_logged_streamerbot_keeps_running_and_ShowUI_works_again()
        {
            var cph = new FakeCph();
            using (var host = FakeHost.Start("tcp", HypeTimer, cph).Open())
            {
                host.Kill();
                Assert.True(FakeHost.WaitUntil(() => !host.Orchid.IsOpen));
                Assert.True(FakeHost.WaitUntil(() => cph.Logged("ERROR").Any(l => l.Contains("closed unexpectedly"))));
                Assert.False(Orchid.AlreadyOpened("TEST EXT"));
            }
            using (var again = FakeHost.Start("tcp", HypeTimer, cph).Open())
            {
                Assert.True(again.Orchid.IsOpen);
            }
        }

        [Theory]
        [MemberData(nameof(Transports))]
        public void A_wrong_token_is_refused_and_logged(string transport)
        {
            var cph = new FakeCph();
            using (var host = FakeHost.Start(transport, HypeTimer, cph))
            {
                host.Connect();
                host.Send(MessageTypes.Hello, new Dictionary<string, object> { { "token", new string('0', 64) } });
                Assert.Equal("wrong token", Json.Str(host.Next(MessageTypes.Refused).Payload, "reason"));
                Assert.True(host.ShowTask.Wait(TimeSpan.FromSeconds(10)));
                Assert.False(host.Orchid.IsOpen);
                Assert.Contains(cph.Logged("ERROR"), l => l.Contains("wrong token"));
            }
        }

        [Fact]
        public void The_listener_takes_exactly_one_connection()
        {
            using (var host = FakeHost.Start("tcp", HypeTimer).Open())
            {
                var port = Json.Int(host.SessionFile, "port");
                Assert.Throws<SocketException>(() => new TcpClient().Connect(System.Net.IPAddress.Loopback, port));
            }
        }

        [Fact]
        public void The_pipe_takes_exactly_one_connection()
        {
            // Pipes are the real transport only on Windows. On Unix .NET emulates them with a
            // socket whose listener stays bound, so a second client gets a socket nobody serves.
            Assert.SkipUnless(Environment.OSVersion.Platform == PlatformID.Win32NT, "Pipes are only the real transport on Windows.");
            using (var host = FakeHost.Start("pipe", HypeTimer).Open())
            {
                var second = new System.IO.Pipes.NamedPipeClientStream(".", Json.Str(host.SessionFile, "pipe"), System.IO.Pipes.PipeDirection.InOut);
                Assert.ThrowsAny<Exception>(() => second.Connect(1000));
                second.Dispose();
            }
        }

        [Theory]
        [MemberData(nameof(Transports))]
        public void An_expired_session_is_refused_even_with_the_right_token(string transport)
        {
            var old = SessionFile.Lifetime;
            SessionFile.Lifetime = TimeSpan.FromMilliseconds(300);
            try
            {
                var cph = new FakeCph();
                using (var host = FakeHost.Start(transport, HypeTimer, cph))
                {
                    Thread.Sleep(600);
                    host.Connect();
                    host.Send(MessageTypes.Hello, new Dictionary<string, object> { { "token", Json.Str(host.SessionFile, "token") } });
                    Assert.Equal("session expired", Json.Str(host.Next(MessageTypes.Refused).Payload, "reason"));
                    Assert.True(host.ShowTask.Wait(TimeSpan.FromSeconds(10)));
                    Assert.False(host.Orchid.IsOpen);
                }
            }
            finally { SessionFile.Lifetime = old; }
        }

        [Fact]
        public void An_oversized_frame_ends_the_session_without_hurting_streamerbot()
        {
            var cph = new FakeCph();
            using (var host = FakeHost.Start("tcp", HypeTimer, cph).Open())
            {
                host.SendRaw(new byte[] { 0x7F, 0xFF, 0xFF, 0xFF });
                Assert.True(FakeHost.WaitUntil(() => !host.Orchid.IsOpen));
                Assert.True(FakeHost.WaitUntil(() => cph.Logged("ERROR").Any(l => l.Contains("1 MiB"))));
            }
        }

        [Fact]
        public void A_silent_host_is_dropped_after_the_heartbeat_window()
        {
            var old = SessionLink.DeadAfter;
            var oldBeat = SessionLink.HeartbeatEvery;
            SessionLink.DeadAfter = TimeSpan.FromMilliseconds(600);
            SessionLink.HeartbeatEvery = TimeSpan.FromMilliseconds(200);
            try
            {
                var cph = new FakeCph();
                using (var host = FakeHost.Start("tcp", HypeTimer, cph).Open())
                {
                    Assert.True(FakeHost.WaitUntil(() => !host.Orchid.IsOpen, 5));
                    // IsOpen turns false as soon as closing starts; the error is logged just after, on the callback thread.
                    Assert.True(FakeHost.WaitUntil(() => cph.Logged("ERROR").Any(l => l.Contains("stopped responding")), 5));
                }
            }
            finally
            {
                SessionLink.DeadAfter = old;
                SessionLink.HeartbeatEvery = oldBeat;
            }
        }

        [Fact]
        public void Values_changed_while_open_with_SetValue_reach_the_window()
        {
            using (var host = FakeHost.Start("tcp", HypeTimer).Open())
            {
                host.Orchid.SetValue("bits_minimum", 7);
                var patch = host.Next(MessageTypes.Patch);
                Assert.Equal(7, Json.Int(Json.Obj(patch.Payload, "values"), "bits_minimum"));
                Assert.Contains("\"bits_minimum\":7", host.Cph.Raw("orchid_TEST EXT"));
            }
        }

        [Fact]
        public void Discard_restores_the_saved_values()
        {
            using (var host = FakeHost.Start("tcp", HypeTimer).Open())
            {
                host.Change("enabled", false);
                host.NextStatus(s => s.Dirty);
                host.Command(Commands.Discard);
                var patch = host.Next(MessageTypes.Patch);
                Assert.Equal(true, Json.Obj(patch.Payload, "values")["enabled"]);
                Assert.Equal("Changes discarded", Json.Str(host.Next(MessageTypes.Toast).Payload, "message"));
            }
        }

        [Fact]
        public void Second_ShowUI_brings_the_window_forward()
        {
            using (var host = FakeHost.Start("tcp", HypeTimer).Open())
            {
                Assert.True(Orchid.AlreadyOpened("TEST EXT"));
                host.Next(MessageTypes.Focus);
            }
        }

        [Fact]
        public void Auto_save_saves_after_the_delay()
        {
            using (var host = FakeHost.Start("tcp", o => { HypeTimer(o); o.SetAutoSave(true); o.Layout.AutoSaveDelayMs = 100; }).Open())
            {
                Assert.Equal("Changes save automatically", host.Spec.Status.Text);
                host.Change("enabled", false);
                Assert.True(FakeHost.WaitUntil(() => host.Cph.Raw("orchid_TEST EXT").Contains("\"enabled\":false")));
            }
        }

        [Fact]
        public void Password_values_are_masked_in_LogSettings()
        {
            var cph = new FakeCph();
            var o = new Orchid(cph, "MASK");
            o.Tab("T").AddPasswordBox("Token", "", "apiToken", "s3cret-value");
            o.LogSettings();
            Assert.DoesNotContain(cph.Log, l => l.Contains("s3cret-value"));
            Assert.Contains(cph.Log, l => l.Contains("apiToken") && l.Contains("masked"));
        }

        [Fact]
        public void The_session_file_holds_a_fresh_256_bit_token_and_expires()
        {
            using (var host = FakeHost.Start("tcp", HypeTimer))
            {
                var token = Json.Str(host.SessionFile, "token");
                Assert.Matches("^[0-9a-f]{64}$", token);
                var expires = (long)Json.Num(host.SessionFile, "expiresAt");
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Assert.InRange(expires - now, 1000, ProtocolInfo.TokenLifetimeSeconds * 1000);
#if NET
                if (!OperatingSystem.IsWindows())
                    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(host.SessionFilePath));
#endif
                host.Open();
            }
        }
    }

    [Collection("environment")]
    public class LaunchTests
    {
        [Fact]
        public void A_host_with_an_untrusted_checksum_is_refused()
        {
            var dir = Path.Combine(Path.GetTempPath(), "orchid-launch-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(dir);
            var fake = Path.Combine(dir, "orchid-host");
            File.WriteAllText(fake, "not the real host");
            var old = HostLauncher.TrustedHashes;
            HostLauncher.TrustedHashes = new[] { new string('a', 64) };
            try
            {
                var problem = HostLauncher.Verify(fake, s => { });
                Assert.Contains("isn't a build this OrchidUI.dll trusts", problem);
                HostLauncher.TrustedHashes = new[] { HostLauncher.Sha256(fake) };
                Assert.Null(HostLauncher.Verify(fake, s => { }));
            }
            finally
            {
                HostLauncher.TrustedHashes = old;
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void A_missing_host_is_one_clear_log_line()
        {
            var cph = new FakeCph();
            using (Env.Set("ORCHIDUI_HOST", Path.Combine(Path.GetTempPath(), "no-such-orchid-host"), "ORCHIDUI_NO_LAUNCH", null, "ORCHIDUI_TRANSPORT", "tcp"))
            {
                var o = new Orchid(cph, "MISSING");
                o.Tab("T").AddToggleSwitch("A", "", "a", true);
                o.ShowUI();
                Assert.False(o.IsOpen);
                Assert.Single(cph.Logged("ERROR"));
                Assert.Contains("orchid-host wasn't found", cph.Logged("ERROR")[0]);
                Assert.False(Orchid.AlreadyOpened("MISSING"));
            }
        }

        [Fact]
        public void Wine_paths_map_through_drive_Z()
        {
            Assert.Equal("/home/stan/.local/share/orchid", WinePaths.ToUnix(@"Z:\home\stan\.local\share\orchid"));
            Assert.Equal(@"Z:\run\user\1000\orchidui", WinePaths.FromUnix("/run/user/1000/orchidui"));
        }

        [Fact]
        public void Wine_sessions_use_a_unix_folder_the_host_can_read()
        {
            OrchidPlatform.OverrideWine(true);
            try
            {
                using (Env.Set("XDG_RUNTIME_DIR", "/run/user/1000", "ORCHIDUI_SESSION_DIR", null))
                {
                    var dir = SessionDirectory.Resolve();
                    Assert.Equal("/run/user/1000/orchidui", dir.HostPath);
                    Assert.Equal(@"Z:\run\user\1000\orchidui", dir.WindowsPath);
                }
            }
            finally { OrchidPlatform.OverrideWine(null); }
        }

        [Fact]
        public void Wine_without_XDG_RUNTIME_DIR_finds_run_user_from_proc()
        {
            // Bottles with GE-Proton: neither XDG_RUNTIME_DIR nor HOME reaches Streamer.bot, and the
            // prefix's AppData isn't private, so orchid-host would refuse a session file there.
            const string status = "Name:\twine64-preloader\nUmask:\t0022\nState:\tS (sleeping)\nUid:\t1000\t1000\t1000\t1000\nGid:\t1000\t1000\t1000\t1000\n";
            var read = SessionDirectory.ReadUnixFile;
            var exists = SessionDirectory.UnixDirExists;
            OrchidPlatform.OverrideWine(true);
            try
            {
                SessionDirectory.ReadUnixFile = path => path == "/proc/self/status" ? status : throw new IOException("no " + path);
                SessionDirectory.UnixDirExists = path => path == "/run/user/1000";
                using (Env.Set("XDG_RUNTIME_DIR", null, "ORCHIDUI_SESSION_DIR", null, "TMPDIR", null, "HOME", null))
                {
                    var dir = SessionDirectory.Resolve();
                    Assert.Equal("/run/user/1000/orchidui", dir.HostPath);
                    Assert.Equal(@"Z:\run\user\1000\orchidui", dir.WindowsPath);
                    Assert.Null(dir.Warning);

                    // No such folder (no logind), or no Z: drive: fall back to the other locations.
                    SessionDirectory.UnixDirExists = path => false;
                    Assert.NotEqual("/run/user/1000/orchidui", SessionDirectory.Resolve().HostPath);
                    SessionDirectory.ReadUnixFile = path => throw new DirectoryNotFoundException("no Z:");
                    Assert.NotEqual("/run/user/1000/orchidui", SessionDirectory.Resolve().HostPath);
                }
                // A visible XDG_RUNTIME_DIR still wins.
                SessionDirectory.UnixDirExists = path => true;
                using (Env.Set("XDG_RUNTIME_DIR", "/run/user/2000", "ORCHIDUI_SESSION_DIR", null))
                    Assert.Equal("/run/user/2000/orchidui", SessionDirectory.Resolve().HostPath);
            }
            finally
            {
                SessionDirectory.ReadUnixFile = read;
                SessionDirectory.UnixDirExists = exists;
                OrchidPlatform.OverrideWine(null);
            }
        }

        [Fact]
        public void Run_user_folder_comes_from_the_real_uid()
        {
            Assert.Equal("/run/user/1000", SessionDirectory.RuntimeDirFromUid(_ => "Uid:\t1000\t1000\t1000\t1000\n", _ => true));
            Assert.Equal("/run/user/0", SessionDirectory.RuntimeDirFromUid(_ => "Name:\tx\nUid: 0 0 0 0", _ => true));
            Assert.Null(SessionDirectory.RuntimeDirFromUid(_ => "Uid:\t1000\n", _ => false));
            Assert.Null(SessionDirectory.RuntimeDirFromUid(_ => "Uid:\tabc\n", _ => true));
            Assert.Null(SessionDirectory.RuntimeDirFromUid(_ => "Uid:\t-1\n", _ => true));
            Assert.Null(SessionDirectory.RuntimeDirFromUid(_ => "", _ => true));
            Assert.Null(SessionDirectory.RuntimeDirFromUid(_ => throw new IOException(), _ => true));
            Assert.Null(SessionDirectory.RuntimeDirFromUid(_ => "Uid:\t1000\n", _ => throw new IOException()));
        }

        [Fact]
        public void Tokens_compare_in_constant_time_shape()
        {
            Assert.True(Tokens.Equal("abc", "abc"));
            Assert.False(Tokens.Equal("abc", "abd"));
            Assert.False(Tokens.Equal("abc", "ab"));
            Assert.False(Tokens.Equal("abc", null));
            Assert.NotEqual(Tokens.New(), Tokens.New());
        }
    }
}
