using System;
using System.IO;
using System.Text;
using Xunit;

namespace OrchidUI.Tests
{
    public static class Repo
    {
        private static readonly Lazy<string> RootPath = new Lazy<string>(() =>
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "OrchidUI.slnx"))) return dir.FullName;
            }
            throw new InvalidOperationException("Couldn't find OrchidUI.slnx above " + AppContext.BaseDirectory);
        });

        public static string Root { get { return RootPath.Value; } }
        public static string Fixtures { get { return Path.Combine(Root, "tests", "fixtures"); } }
        public static string Examples { get { return Path.Combine(Root, "examples"); } }
        public static string Themes { get { return Path.Combine(Root, "themes"); } }
    }

    // Golden files: tests/fixtures/<dir>/<name>. ORCHIDUI_UPDATE_GOLDEN=1 rewrites them.
    public static class Golden
    {
        public static void Verify(string dir, string name, string actual)
        {
            actual = actual.Replace("\r\n", "\n");
            var path = Path.Combine(Repo.Fixtures, dir, name);
            if (Environment.GetEnvironmentVariable("ORCHIDUI_UPDATE_GOLDEN") == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, actual, new UTF8Encoding(false));
                return;
            }
            Assert.True(File.Exists(path), "No golden file " + path + "; run with ORCHIDUI_UPDATE_GOLDEN=1 and review it.");
            var expected = File.ReadAllText(path).Replace("\r\n", "\n");
            if (expected == actual) return;
            var received = path + ".received";
            File.WriteAllText(received, actual, new UTF8Encoding(false));
            var e = expected.Split('\n');
            var a = actual.Split('\n');
            int line = 0;
            while (line < e.Length && line < a.Length && e[line] == a[line]) line++;
            Assert.Fail(name + " differs at line " + (line + 1) + ":\n  expected: " + (line < e.Length ? e[line] : "(end)") + "\n  actual:   " + (line < a.Length ? a[line] : "(end)") + "\nWrote " + received);
        }
    }
}
