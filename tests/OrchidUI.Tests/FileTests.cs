using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OrchidUI.Protocol;
using Xunit;

namespace OrchidUI.Tests
{
    // Image previews never make a frame too big, files are read with a hard cap and only when
    // they're regular local files, and the More menu's messages match auto-save.
    [Collection("environment")]
    public class FileTests
    {
        private static string BigPng(string dir, string name, int size)
        {
            var bytes = new byte[size];
            new Random(size + name.Length).NextBytes(bytes);
            var header = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10 };
            Array.Copy(header, bytes, header.Length);
            var path = Path.Combine(dir, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static string TempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "orchid-files-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static List<Dictionary<string, object>> States(Frame patch)
        {
            var states = Json.Arr(patch.Payload, "states");
            return states == null ? new List<Dictionary<string, object>>() : states.OfType<Dictionary<string, object>>().ToList();
        }

        private static int Size(Frame f) { return Encoding.UTF8.GetByteCount(Json.Serialize(f.Payload, false)); }

        [Fact]
        public void Previews_go_once_and_never_push_a_frame_past_the_limit()
        {
            var dir = TempDir();
            try
            {
                var big = Enumerable.Range(1, 3).Select(i => BigPng(dir, "logo" + i + ".png", 500 * 1024)).ToArray();
                var small = BigPng(dir, "small.png", 2 * 1024);
                using (var host = FakeHost.Start("tcp", o =>
                {
                    var t = o.Tab("Look");
                    t.AddToggleSwitch("Show logos", "", "show", true);
                    for (int i = 0; i < 3; i++) t.AddImageInput("Logo " + (i + 1), "", "logo" + (i + 1), big[i]).ShowWhen(v => v.Get<bool>("show"));
                }).Open())
                {
                    // Three 500 KB previews don't fit in one 1 MiB frame: the window opens with one,
                    // and the others follow, each in a frame under the limit.
                    var opened = host.Spec.States.Where(s => s.ImageSource != null).ToList();
                    Assert.Equal(3, opened.Count);
                    Assert.Single(opened, s => s.Image != null);
                    Assert.Equal(2, opened.Count(s => s.Image == null && s.Display == "Loading preview…"));
                    AllArrive(host, opened.Where(s => s.Image != null).Select(s => s.Id));

                    // A state change that isn't the image's leaves the preview out.
                    host.Change("show", false);
                    var hidden = States(host.Next(MessageTypes.Patch)).Where(s => Json.Str(s, "id") != host.Spec.Fields.First(f => f.Key == "show").Id).ToList();
                    Assert.Equal(3, hidden.Count);
                    Assert.All(hidden, s => Assert.False(s.ContainsKey("image")));

                    foreach (var key in new[] { "logo1", "logo2", "logo3" })
                    {
                        host.Change(key, small);
                        var withImage = States(host.Next(MessageTypes.Patch)).Single(s => s.ContainsKey("image"));
                        Assert.NotNull(Json.Obj(withImage, "image"));
                    }

                    // Reset to defaults brings back all three at once: still one frame under the limit.
                    host.Command(Commands.ResetAll);
                    var dialog = host.Next(MessageTypes.Dialog);
                    host.Send(MessageTypes.DialogResult, new Dictionary<string, object> { { "request", Json.Int(dialog.Payload, "request") }, { "index", 1 } });
                    var first = host.Next(MessageTypes.Patch);
                    Assert.True(Size(first) < FrameCodec.MaxFrameBytes);
                    Assert.Single(States(first), s => Json.Obj(s, "image") != null);
                    AllArrive(host, States(first).Where(s => Json.Obj(s, "image") != null).Select(s => Json.Str(s, "id")));
                }
            }
            finally { Directory.Delete(dir, true); }
        }

        // Waits for the three previews, the ones named having come already.
        private static void AllArrive(FakeHost host, IEnumerable<string> already)
        {
            var got = new HashSet<string>(already);
            while (got.Count < 3)
            {
                var patch = host.Next(MessageTypes.Patch);
                Assert.True(Size(patch) < FrameCodec.MaxFrameBytes);
                foreach (var st in States(patch))
                {
                    if (Json.Obj(st, "image") == null) continue;
                    got.Add(Json.Str(st, "id"));
                    Assert.Null(Json.Str(st, "display"));
                }
            }
        }

        [Fact]
        public void Paths_keep_their_percent_signs_and_hashes()
        {
            var dir = TempDir();
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, "#2"));
                var png = BigPng(Path.Combine(dir, "#2"), "logo%41.png", 1024);
                string problem;
                Assert.Equal(png, WindowController.UserFilePath(png, out problem));
                Assert.Equal(png, WindowController.UserFilePath(FileUrl(png), out problem));
                using (var host = FakeHost.Start("tcp", o => o.Tab("Look").AddImageInput("Logo", "", "logo")).Open())
                {
                    host.Change("logo", png);
                    Assert.Equal("image/png", host.NextState("logo").Image.Mime);
                }
            }
            finally { Directory.Delete(dir, true); }
        }

        // file:///tmp/x/%232/logo%2541.png, or file:///C:/x/%232/logo%2541.png on Windows.
        private static string FileUrl(string path)
        {
            var parts = path.Split(Path.DirectorySeparatorChar);
            var escaped = parts.Select((s, i) => i == 0 && s.EndsWith(":", StringComparison.Ordinal) ? s : Uri.EscapeDataString(s));
            var url = string.Join("/", escaped);
            return url.StartsWith("/", StringComparison.Ordinal) ? "file://" + url : "file:///" + url;
        }

        [Fact]
        public void Only_image_files_are_previewed()
        {
            var dir = TempDir();
            try
            {
                var secret = Path.Combine(dir, "secrets.txt");
                File.WriteAllText(secret, "oauth-token-for-duskbadger");
                var renamed = Path.Combine(dir, "secrets.png");
                File.WriteAllText(renamed, "oauth-token-for-duskbadger");
                using (var host = FakeHost.Start("tcp", o => o.Tab("Look").AddImageInput("Logo", "", "logo")).Open())
                {
                    foreach (var path in new[] { secret, renamed, dir, @"\\mossylantern\share\logo.png", "//mossylantern/share/logo.png", "file://mossylantern/share/logo.png" })
                    {
                        host.Change("logo", path);
                        var state = host.NextState("logo");
                        Assert.Null(state.Image);
                        Assert.Equal("Can't be previewed", state.Display);
                    }
                }
            }
            finally { Directory.Delete(dir, true); }
        }

        // A stream whose length says it's tiny and that never ends (a pipe, /dev/zero, a file that
        // grows while it's read).
        private sealed class EndlessStream : Stream
        {
            public long Served;
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { return 1; } }
            public override long Position { get { return Served; } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (Served > 64L * 1024 * 1024) return 0;
                Served += count;
                return count;
            }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }

        [Fact]
        public void Files_are_read_with_a_hard_cap_and_only_when_local_and_regular()
        {
            var endless = new EndlessStream();
            Assert.Null(WindowController.ReadCapped(endless, 1024 * 1024));
            Assert.True(endless.Served <= 1024 * 1024 + 81920);
            Assert.Equal(100, WindowController.ReadCapped(new MemoryStream(new byte[100]), 100).Length);
            Assert.Null(WindowController.ReadCapped(new MemoryStream(new byte[101]), 100));

            string problem;
            foreach (var p in new[] { @"\\mossylantern\share\font.ttf", "//mossylantern/share/font.ttf", @"\\.\CON", "file://mossylantern/share/font.ttf", "/dev/zero", @"Z:\dev\zero", "/proc/self/environ", Path.GetTempPath() })
            {
                Assert.Null(WindowController.UserFilePath(p, out problem));
                Assert.NotNull(problem);
            }
            var warnings = new List<string>();
            Assert.Null(WindowController.ReadUserFile("/dev/zero", 1024 * 1024, "Font file", warnings.Add));
            Assert.Contains(warnings, w => w.Contains("network share or a device"));
        }

        [Fact]
        public void An_export_is_always_a_json_file_on_a_local_disk()
        {
            var dir = TempDir();
            try
            {
                Assert.Equal(Path.Combine(dir, "settings.json"), WindowController.ExportPath(Path.Combine(dir, "settings.json")));
                Assert.Equal(Path.Combine(dir, "settings.JSON"), WindowController.ExportPath(Path.Combine(dir, "settings.JSON")));
                Assert.Equal(Path.Combine(dir, ".bashrc.json"), WindowController.ExportPath(Path.Combine(dir, ".bashrc")));
                Assert.Equal(Path.Combine(dir, "orchid-host.json"), WindowController.ExportPath(Path.Combine(dir, "orchid-host")));
                foreach (var p in new[] { "settings.json", @"\\mossylantern\share\settings.json", "//mossylantern/share/settings.json", "/proc/self/x.json" })
                    Assert.Throws<IOException>(() => WindowController.ExportPath(p));

                // Through the window: the host names a file that isn't JSON; it's left alone.
                var victim = Path.Combine(dir, "startup.sh");
                File.WriteAllText(victim, "echo pixelpancake");
                using (var host = FakeHost.Start("tcp", o => o.Tab("General").AddIntegerStepper("Minimum", "", "minimum", 1, 100, 5)).Open())
                {
                    host.Send(MessageTypes.Command, new Dictionary<string, object> { { "name", Commands.Export }, { "path", victim } });
                    Assert.Contains("startup.sh.json", Json.Str(host.Next(MessageTypes.Toast).Payload, "message"));
                }
                Assert.Equal("echo pixelpancake", File.ReadAllText(victim));
                Assert.Contains("\"minimum\"", File.ReadAllText(victim + ".json"));

                // That added .json isn't a file the dialog asked about replacing: it's kept.
                using (var host = FakeHost.Start("tcp", o => o.Tab("General").AddIntegerStepper("Minimum", "", "minimum", 1, 100, 7)).Open())
                {
                    host.Send(MessageTypes.Command, new Dictionary<string, object> { { "name", Commands.Export }, { "path", victim } });
                    var dialog = host.Next(MessageTypes.Dialog);
                    Assert.Contains("already exists", Json.Str(dialog.Payload, "message"));
                    host.Send(MessageTypes.DialogResult, new Dictionary<string, object> { { "request", Json.Int(dialog.Payload, "request") }, { "index", 0 } });
                }
                Assert.Contains("\"minimum\": 5", File.ReadAllText(victim + ".json"));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Import_refuses_what_isnt_a_regular_file()
        {
            using (var host = FakeHost.Start("tcp", o => o.Tab("General").AddIntegerStepper("Minimum", "", "minimum", 1, 100, 5)).Open())
            {
                host.Send(MessageTypes.Command, new Dictionary<string, object> { { "name", Commands.Import }, { "path", "/dev/zero" } });
                var dialog = host.Next(MessageTypes.Dialog);
                Assert.Contains("network share or a device", Json.Str(dialog.Payload, "message"));
                host.Send(MessageTypes.DialogResult, new Dictionary<string, object> { { "request", Json.Int(dialog.Payload, "request") }, { "index", 0 } });
                Assert.Equal(5, host.Orchid.GetPendingValue<int>("minimum"));
            }
        }

        [Fact]
        public void With_auto_save_an_import_with_an_error_doesnt_say_it_was_saved()
        {
            var file = Path.Combine(Path.GetTempPath(), "orchid-import-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(file, "{\"name\": \"\"}");
                using (var host = FakeHost.Start("tcp", o =>
                {
                    o.ConfigureLayout(l => l.AutoSave = true);
                    o.Tab("General").AddTextbox("Name", "", "name", "QuietFern").Required();
                }).Open())
                {
                    host.Send(MessageTypes.Command, new Dictionary<string, object> { { "name", Commands.Import }, { "path", file } });
                    var dialog = host.Next(MessageTypes.Dialog);
                    Assert.Contains("click Save", Json.Str(dialog.Payload, "message"));
                    Assert.DoesNotContain("saved", Json.Str(dialog.Payload, "message"));
                }
            }
            finally { File.Delete(file); }
        }

        [Fact]
        public void With_auto_save_the_reset_says_it_was_saved()
        {
            using (var host = FakeHost.Start("tcp", o =>
            {
                o.ConfigureLayout(l => l.AutoSave = true);
                o.Tab("General").AddIntegerStepper("Minimum", "", "minimum", 1, 100, 5);
            }).Open())
            {
                host.Command(Commands.ResetAll);
                var dialog = host.Next(MessageTypes.Dialog);
                Assert.DoesNotContain("until you click Save", Json.Str(dialog.Payload, "message"));
                Assert.Contains("is saved", Json.Str(dialog.Payload, "message"));
                host.Send(MessageTypes.DialogResult, new Dictionary<string, object> { { "request", Json.Int(dialog.Payload, "request") }, { "index", 1 } });
                Assert.Equal("Defaults restored and saved.", Json.Str(host.Next(MessageTypes.Toast).Payload, "message"));
            }
        }
    }
}
