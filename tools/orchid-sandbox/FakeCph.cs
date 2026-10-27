using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OrchidUI.Protocol;

namespace OrchidUI.Sandbox
{
    // Stands in for Streamer.bot's CPH: the members OrchidUI's CphBridge looks up by reflection
    // (GetGlobalVar<T>, SetGlobalVar, RunAction, Log*), plus what the examples call.
    public sealed class FakeCph
    {
        private readonly Dictionary<string, object> _globals = new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly object _lock = new object();
        private readonly string _storePath;

        public FakeCph(string storePath = null)
        {
            _storePath = storePath;
            if (storePath != null && File.Exists(storePath))
            {
                foreach (var kv in Json.ParseObject(File.ReadAllText(storePath))) _globals[kv.Key] = kv.Value;
            }
        }

        public readonly List<string> Log = new List<string>();
        public readonly List<string> ActionsRun = new List<string>();
        public readonly List<string> Messages = new List<string>();
        public readonly Dictionary<string, object> Args = new Dictionary<string, object>(StringComparer.Ordinal);
        public bool EchoToConsole;
        public bool Streaming;
        public bool FailWrites;

        public T GetGlobalVar<T>(string name, bool persisted = true)
        {
            lock (_lock)
            {
                object v;
                if (!_globals.TryGetValue(name, out v) || v == null) return default(T);
                if (v is T) return (T)v;
                return (T)Convert.ChangeType(v, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        public void SetGlobalVar(string name, object value, bool persisted = true)
        {
            if (FailWrites) throw new IOException("Global variables are read-only in this test.");
            lock (_lock)
            {
                _globals[name] = value;
                if (_storePath != null) File.WriteAllText(_storePath, Json.Serialize(_globals, true));
            }
        }

        public string Raw(string name)
        {
            lock (_lock)
            {
                object v;
                return _globals.TryGetValue(name, out v) ? v as string : null;
            }
        }

        public bool RunAction(string action, bool runImmediately = true)
        {
            lock (_lock) ActionsRun.Add(action);
            return true;
        }

        public void LogInfo(string message) { Write("INFO", message); }
        public void LogWarn(string message) { Write("WARN", message); }
        public void LogError(string message) { Write("ERROR", message); }
        public void LogDebug(string message) { Write("DEBUG", message); }

        // Also written here when set (orchid-sandbox.log next to the exe), for launchers that don't
        // show a console: Steam, Lutris without its log window.
        public string LogFile;

        private void Write(string level, string message)
        {
            lock (_lock) Log.Add(level + " " + message);
            if (EchoToConsole)
            {
                try { Console.Error.WriteLine("[" + level + "] " + message); }
                catch (Exception) { }
            }
            AppendToLogFile(LogFile, "[" + level + "] " + message);
        }

        public static void AppendToLogFile(string path, string line)
        {
            if (path == null) return;
            try { File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture) + " " + line + Environment.NewLine); }
            catch (Exception) { }
        }

        public bool TryGetArg<T>(string name, out T value)
        {
            object v;
            if (Args.TryGetValue(name, out v) && v is T) { value = (T)v; return true; }
            value = default(T);
            return false;
        }

        public void SendMessage(string message, bool bot = true, bool fallback = true) { lock (_lock) Messages.Add(message); }

        public bool ObsIsStreaming(int connection = 0) { return Streaming; }

        public List<FakeReward> TwitchGetRewards()
        {
            return new[] { "Hydrate", "Add 5 minutes", "Song request", "Pet the cat" }.Select(t => new FakeReward { Title = t }).ToList();
        }

        public string[] Logged(string level) { lock (_lock) return Log.Where(l => l.StartsWith(level + " ", StringComparison.Ordinal)).ToArray(); }
    }

    public sealed class FakeReward
    {
        public string Title { get; set; }
    }

    // Gives the examples their "CPH" and "args" members, as Streamer.bot's CPHInlineBase does.
    public abstract class SandboxBase
    {
        public FakeCph CPH { get; set; }
        public Dictionary<string, object> args { get; set; }
    }
}
