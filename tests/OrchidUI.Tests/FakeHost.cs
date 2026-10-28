using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using OrchidUI.Protocol;
using OrchidUI.Sandbox;
using Xunit;

namespace OrchidUI.Tests
{
    // Plays orchid-host at the protocol level over a real pipe or real loopback TCP, so the whole
    // shim (session file, listener, handshake, threads, callbacks, storage) runs for real.
    internal sealed class FakeHost : IDisposable
    {
        // Every frame received, in order. Searches start at the mark set by the last Send, so a
        // test only ever looks at what came back after its own action.
        private readonly List<Frame> _all = new List<Frame>();
        private readonly Dictionary<string, int> _cursor = new Dictionary<string, int>();
        private volatile bool _ended;
        private int _mark;
        private readonly Env _env;
        private Stream _stream;
        private Action _close;
        private int _id;

        public string SessionDir { get; private set; }
        public Orchid Orchid { get; private set; }
        public FakeCph Cph { get; private set; }
        public WindowSpec Spec { get; private set; }
        public Task ShowTask { get; private set; }
        public Dictionary<string, object> SessionFile { get; private set; }
        public string SessionFilePath { get; private set; }

        private FakeHost(string transport)
        {
            SessionDir = Path.Combine(Path.GetTempPath(), "orchid-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(SessionDir);
            _env = Env.Set("ORCHIDUI_NO_LAUNCH", "1", "ORCHIDUI_SESSION_DIR", SessionDir, "ORCHIDUI_TRANSPORT", transport, "ORCHIDUI_THEME", null);
        }

        // Starts ShowUI() on a background thread and waits for the session file to appear.
        public static FakeHost Start(string transport, Action<Orchid> build, FakeCph cph = null, string name = "TEST EXT")
        {
            return StartWith(transport, build, o => o.ShowUI(), cph, name);
        }

        // Like Start, but runs any call that makes the shim open a session (e.g. a standalone dialog).
        public static FakeHost StartWith(string transport, Action<Orchid> build, Action<Orchid> run, FakeCph cph = null, string name = "TEST EXT")
        {
            var host = new FakeHost(transport);
            host.Cph = cph ?? new FakeCph();
            host.Orchid = new Orchid(host.Cph, name, "1.0.0");
            build(host.Orchid);
            host.ShowTask = Task.Run(() => run(host.Orchid));
            host.SessionFilePath = WaitFor(() => Directory.GetFiles(host.SessionDir, "session-*.json").FirstOrDefault(), TimeSpan.FromSeconds(10));
            Assert.NotNull(host.SessionFilePath);
            host.SessionFile = Json.ParseObject(File.ReadAllText(host.SessionFilePath));
            return host;
        }

        public void Connect()
        {
            var transport = Json.Str(SessionFile, "transport");
            if (transport == "pipe")
            {
                // Asynchronous like the real host: on Windows a synchronous pipe handle runs one read or
                // write at a time, so the hello would wait behind the reader thread's pending read.
                var pipe = new NamedPipeClientStream(".", Json.Str(SessionFile, "pipe"), PipeDirection.InOut, PipeOptions.Asynchronous);
                pipe.Connect(5000);
                _stream = pipe;
                _close = pipe.Dispose;
            }
            else
            {
                var tcp = new TcpClient();
                tcp.Connect(IPAddress.Loopback, Json.Int(SessionFile, "port"));
                _stream = tcp.GetStream();
                _close = tcp.Close;
            }
            var reader = new Thread(() =>
            {
                try
                {
                    Frame f;
                    while ((f = FrameCodec.Read(_stream, MessageTypes.ToHost)) != null)
                    {
                        lock (_all) { _all.Add(f); Monitor.PulseAll(_all); }
                    }
                }
                catch (Exception) { }
                finally
                {
                    _ended = true;
                    lock (_all) Monitor.PulseAll(_all);
                }
            }) { IsBackground = true };
            reader.Start();
        }

        // Connect, say hello with the right token, receive the spec and report the window as ready.
        public FakeHost Open()
        {
            OpenNoWait();
            Assert.True(ShowTask.Wait(TimeSpan.FromSeconds(10)), "ShowUI() didn't return after the window opened.");
            return this;
        }

        public FakeHost OpenNoWait()
        {
            Connect();
            Send(MessageTypes.Hello, new Dictionary<string, object> { { "token", Json.Str(SessionFile, "token") }, { "host", new Dictionary<string, object> { { "version", "test" }, { "os", "test" } } } });
            var open = Next(MessageTypes.Open);
            Spec = WindowSpec.FromJson(Json.Obj(open.Payload, "spec"));
            Send(MessageTypes.Ready, new Dictionary<string, object>());
            return this;
        }

        public void Send(string type, Dictionary<string, object> payload)
        {
            lock (_all) _mark = _all.Count;
            FrameCodec.Write(_stream, new Frame(type, payload, Interlocked.Increment(ref _id)));
        }

        // Waits for the first frame since the mark that matches.
        private Frame Find(Func<Frame, bool> match, double seconds, int start, out int index)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            lock (_all)
            {
                while (true)
                {
                    for (int i = start; i < _all.Count; i++)
                    {
                        if (match(_all[i])) { index = i; return _all[i]; }
                    }
                    start = Math.Max(start, _all.Count);
                    var left = deadline - DateTime.UtcNow;
                    if (left <= TimeSpan.Zero || _ended) { index = -1; return null; }
                    Monitor.Wait(_all, left);
                }
            }
        }

        public void SendRaw(byte[] bytes)
        {
            _stream.Write(bytes, 0, bytes.Length);
            _stream.Flush();
        }

        public void Change(string key, object value)
        {
            var field = Spec.Fields.First(f => f.Keys.Contains(key));
            Send(MessageTypes.Change, new Dictionary<string, object> { { "field", field.Id }, { "key", key }, { "value", value } });
        }

        public void Command(string name, string field = null)
        {
            var p = new Dictionary<string, object> { { "name", name } };
            if (field != null) p["field"] = field;
            Send(MessageTypes.Command, p);
        }

        public Frame Next(string type, double seconds = 10)
        {
            int start;
            lock (_all)
            {
                int cursor;
                start = Math.Max(_mark, _cursor.TryGetValue(type, out cursor) ? cursor : 0);
            }
            int index;
            var f = Find(x => x.Type == type, seconds, start, out index);
            if (f == null) throw new TimeoutException("No '" + type + "' frame within " + seconds + " s.");
            lock (_all) _cursor[type] = index + 1;
            return f;
        }

        // The state for this field in a patch since the last action.
        public FieldState NextState(string key, double seconds = 10)
        {
            var id = Spec.Fields.First(f => f.Keys.Contains(key) || f.Label == key).Id;
            FieldState found = null;
            int index;
            Find(x =>
            {
                if (x.Type != MessageTypes.Patch) return false;
                var states = Json.Arr(x.Payload, "states");
                found = states == null ? null : states.OfType<Dictionary<string, object>>().Select(FieldState.FromJson).FirstOrDefault(s => s.Id == id);
                return found != null;
            }, seconds, _mark, out index);
            if (found == null) throw new TimeoutException("No state for " + key);
            return found;
        }

        public StatusSpec NextStatus(Func<StatusSpec, bool> until = null, double seconds = 10)
        {
            StatusSpec found = null;
            int index;
            Find(x =>
            {
                if (x.Type != MessageTypes.Patch) return false;
                var status = StatusSpec.FromJson(Json.Obj(x.Payload, "status"));
                if (status == null || (until != null && !until(status))) return false;
                found = status;
                return true;
            }, seconds, _mark, out index);
            if (found == null) throw new TimeoutException("No matching status.");
            return found;
        }

        public bool Ended(double seconds = 10)
        {
            return WaitUntil(() => _ended, seconds);
        }

        public void Kill()
        {
            if (_close != null) _close();
        }

        public static T WaitFor<T>(Func<T> probe, TimeSpan timeout) where T : class
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var v = probe();
                if (v != null) return v;
                Thread.Sleep(20);
            }
            return null;
        }

        public static bool WaitUntil(Func<bool> probe, double seconds = 10)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < deadline)
            {
                if (probe()) return true;
                Thread.Sleep(20);
            }
            return false;
        }

        public void Dispose()
        {
            Kill();
            if (ShowTask != null) ShowTask.Wait(TimeSpan.FromSeconds(40));
            // The shim notices the closed link on its own thread; the next test may reuse the name.
            if (Orchid != null) WaitUntil(() => !Orchid.IsOpen && !Orchid.AlreadyOpened(Orchid.ExtensionName), 15);
            _env.Dispose();
            try { Directory.Delete(SessionDir, true); } catch (Exception) { }
        }
    }
}
