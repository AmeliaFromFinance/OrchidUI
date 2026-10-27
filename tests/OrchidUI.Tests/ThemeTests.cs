using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OrchidUI.Protocol;
using OrchidUI.Sandbox;
using Xunit;

namespace OrchidUI.Tests
{
    // tests/fixtures/themes/*.resolved.json are the reference colours for every theme file. The
    // shim's ThemeResolver must agree exactly.
    public class ThemeTests
    {
        public static IEnumerable<object[]> ThemeFiles()
        {
            return Directory.GetFiles(Repo.Themes, "*.json").OrderBy(f => f, StringComparer.Ordinal).Select(f => new object[] { Path.GetFileName(f) });
        }

        [Fact]
        public void All_seven_theme_files_are_here()
        {
            Assert.Equal(7, Directory.GetFiles(Repo.Themes, "*.json").Length);
        }

        [Theory]
        [MemberData(nameof(ThemeFiles))]
        public void Theme_files_resolve_to_their_colours(string file)
        {
            var cph = new FakeCph();
            var o = new Orchid(cph, "THEMES");
            o.LoadThemeFile(Path.Combine(Repo.Themes, file));
            var resolved = ThemeResolver.Resolve(o.Theme);
            var expected = Json.ParseObject(File.ReadAllText(Path.Combine(Repo.Fixtures, "themes", Path.GetFileNameWithoutExtension(file) + ".resolved.json")));
            Assert.Equal((bool)expected["isDark"], resolved.IsDark);
            var colours = (Dictionary<string, object>)expected["colors"];
            foreach (var kv in colours) Assert.True((string)kv.Value == resolved.Colors[kv.Key].Argb, file + ": " + kv.Key + " is " + resolved.Colors[kv.Key].Argb + ", expected " + kv.Value);
            Assert.Equal(colours.Count, resolved.Colors.Count);
            var ignored = ((object[])expected["ignoredKeys"]).Cast<string>().ToArray();
            if (ignored.Length > 0) Assert.Contains(cph.Logged("WARN"), l => ignored.All(k => l.Contains(k)));
        }

        // What the host receives for each theme: OrchidUI.Host.Tests renders snapshots from these.
        [Theory]
        [MemberData(nameof(ThemeFiles))]
        public void Theme_specs_for_the_host_match_their_golden_files(string file)
        {
            var o = new Orchid(new FakeCph(), "THEMES");
            o.LoadThemeFile(Path.Combine(Repo.Themes, file));
            var spec = WindowController.ThemeSpecFor(o.Theme, s => { });
            spec.Name = Path.GetFileNameWithoutExtension(file);
            Golden.Verify("themes", Path.GetFileNameWithoutExtension(file) + ".spec.json", Json.Serialize(spec.ToJson(), true) + "\n");
        }

        [Fact]
        public void The_host_gets_resolved_colours_never_the_theme_file()
        {
            var theme = OrchidTheme.Preset("Daylight");
            var spec = WindowController.ThemeSpecFor(theme, s => { });
            Assert.False(spec.IsDark);
            Assert.Equal(ThemeResolver.Resolve(theme).Colors.Count, spec.Colors.Count);
            Assert.All(spec.Colors.Values, v => Assert.Matches("^#[0-9A-F]{8}$", v));
            Assert.Contains(spec.Gradients, g => g.Key == "Hero");
        }

        [Fact]
        public void A_theme_file_with_unknown_keys_is_applied_and_the_keys_logged()
        {
            var cph = new FakeCph();
            var o = new Orchid(cph, "THEMES");
            o.LoadThemeJson("{\"preset\":\"Sakura\",\"theme\":{\"Accent\":\"#123456\",\"Evil\":\"x\"},\"window\":{\"Width\":700},\"script\":\"rm\"}");
            Assert.Equal("#123456", o.Theme.Accent);
            Assert.Equal(700, o.Window.Width);
            Assert.Contains(cph.Logged("WARN"), l => l.Contains("theme.Evil") && l.Contains("script"));
        }
    }
}
