using System;
using System.IO;
using System.Threading;
using OrchidUI.Protocol;

namespace OrchidUI.Sandbox
{
    public static class Program
    {
        private const string Usage = @"orchid-sandbox: runs an OrchidUI example action with a fake CPH.

  orchid-sandbox <example> --compile         check it compiles against this OrchidUI.dll
  orchid-sandbox <example> --dump            print the WindowSpec JSON it would send
  orchid-sandbox <example> [--store F.json]  open it for real (needs orchid-host; set ORCHIDUI_HOST)

Examples: settings, runtime, minimal, customised (or part of a file name in examples/).
--examples DIR picks the folder (default: ./examples, then ../examples).";

        public static int Main(string[] args)
        {
            string name = null, store = null, examples = null;
            bool dump = false, compileOnly = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--dump": dump = true; break;
                    case "--compile": compileOnly = true; break;
                    case "--store": store = i + 1 < args.Length ? args[++i] : null; break;
                    case "--examples": examples = i + 1 < args.Length ? args[++i] : null; break;
                    case "-h":
                    case "--help": Console.WriteLine(Usage); return 0;
                    default: name = args[i]; break;
                }
            }
            if (name == null) { Console.WriteLine(Usage); return 64; }
            examples = examples ?? FindExamples();
            if (examples == null) { Console.Error.WriteLine("Couldn't find the examples folder; pass --examples."); return 66; }

            if (compileOnly)
            {
                var source = ExampleRunner.FileFor(examples, name);
                try { ExampleRunner.Compile(source); }
                catch (InvalidOperationException ex) { Console.Error.WriteLine(ex.Message); return 1; }
                Console.WriteLine(Path.GetFileName(source) + " compiles.");
                return 0;
            }

            var cph = new FakeCph(store) { EchoToConsole = !dump };
            if (!dump)
            {
                // A log beside the exe, started afresh each run: Steam shows no console.
                cph.LogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "orchid-sandbox.log");
                try { File.WriteAllText(cph.LogFile, ""); } catch (Exception) { cph.LogFile = null; }
                var logFile = cph.LogFile;
                FakeCph.AppendToLogFile(logFile, "orchid-sandbox " + string.Join(" ", args) + " (working folder " + Environment.CurrentDirectory + ")");
                AppDomain.CurrentDomain.UnhandledException += (sender, e) => FakeCph.AppendToLogFile(logFile, "[CRASH] " + e.ExceptionObject);
            }
            var file = ExampleRunner.FileFor(examples, name);
            if (dump)
            {
                var spec = ExampleRunner.Dump(file, cph);
                if (spec == null) { Console.Error.WriteLine(Path.GetFileName(file) + " didn't call ShowUI()."); return 1; }
                Console.WriteLine(Json.Serialize(spec.ToJson(), true));
                return 0;
            }

            FakeCph.AppendToLogFile(cph.LogFile, "running " + file);
            ExampleRunner.Run(ExampleRunner.Compile(file), cph);
            // ShowUI() returns once the window is up; keep the process alive until it's closed.
            FakeCph.AppendToLogFile(cph.LogFile, OpenWindowCount() > 0 ? "window open; waiting for it to close" : "no window open; exiting");
            while (OpenWindowCount() > 0) Thread.Sleep(200);
            FakeCph.AppendToLogFile(cph.LogFile, "done");
            return 0;
        }

        private static int OpenWindowCount()
        {
            var field = typeof(Orchid).GetField("OpenWindows", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var dict = field == null ? null : field.GetValue(null) as System.Collections.ICollection;
            return dict == null ? 0 : dict.Count;
        }

        private static string FindExamples()
        {
            foreach (var candidate in new[] { "examples", Path.Combine("..", "examples"), Path.Combine(AppContext.BaseDirectory, "examples") })
            {
                if (Directory.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
