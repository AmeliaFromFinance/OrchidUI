using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using OrchidUI.Protocol;

namespace OrchidUI
{
    // Receives what orchid-host sends, on the link's own callback thread (the "UI thread").
    internal interface ISessionHandler
    {
        Dictionary<string, object> BuildOpenPayload();
        void Handle(Frame frame);
        void OnClosed(string reason, bool unexpected);
    }

    // One live connection to orchid-host. Nothing here runs on the Streamer.bot action thread:
    // accepting, reading, writing, heartbeats and callbacks each have their own background thread.
    internal sealed class SessionLink : IDisposable
    {
        private readonly IListener _listener;
        private readonly SessionFile _file;
        private readonly ISessionHandler _handler;
        private readonly Action<string> _log;
        private readonly BlockingCollection<Frame> _outbox = new BlockingCollection<Frame>(new ConcurrentQueue<Frame>(), 4096);
        private readonly BlockingCollection<Action> _callbacks = new BlockingCollection<Action>(new ConcurrentQueue<Action>());
        private readonly ManualResetEvent _opened = new ManualResetEvent(false);
        private readonly ManualResetEvent _ended = new ManualResetEvent(false);
        private readonly Dictionary<int, PendingDialog> _dialogs = new Dictionary<int, PendingDialog>();
        private readonly object _dialogLock = new object();
        private Connection _connection;
        private Thread _callbackThread;
        private Timer _heartbeat;
        private long _lastSeenTicks;
        private int _nextId;
        private int _nextDialog;
        private int _closed;
        private volatile bool _closeSent;

        private sealed class PendingDialog
        {
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public int Result = -1;
        }

        public SessionLink(IListener listener, SessionFile file, ISessionHandler handler, Action<string> log)
        {
            _listener = listener;
            _file = file;
            _handler = handler;
            _log = log ?? (s => { });
        }

        public string Describe { get { var c = _connection; return c == null ? _listener.Kind : c.Describe; } }
        // False as soon as Close() starts, not only once OnClosed has run.
        public bool IsOpen { get { return _opened.WaitOne(0) && _closed == 0; } }
        public bool HasEnded { get { return _ended.WaitOne(0); } }
        public bool IsCallbackThread { get { return Thread.CurrentThread == _callbackThread; } }

        // Dialog-only windows: the host answers and exits, so the end of the stream after an
        // answer is the normal way the session ends.
        public bool EndsAfterDialog { get; set; }

        // How long the host may take to connect and say hello.
        public static TimeSpan ConnectTimeout = TimeSpan.FromSeconds(ProtocolInfo.TokenLifetimeSeconds);
        public static TimeSpan HelloTimeout = TimeSpan.FromSeconds(5);
        public static TimeSpan DeadAfter = TimeSpan.FromSeconds(ProtocolInfo.DeadAfterSeconds);
        public static TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(ProtocolInfo.HeartbeatSeconds);

        public void Start()
        {
            _callbackThread = new Thread(CallbackLoop) { IsBackground = true, Name = "OrchidUI callbacks" };
            _callbackThread.Start();
            var accept = new Thread(AcceptAndRun) { IsBackground = true, Name = "OrchidUI link" };
            accept.Start();
        }

        public bool WaitOpened(TimeSpan timeout)
        {
            return WaitHandle.WaitAny(new WaitHandle[] { _opened, _ended }, timeout) == 0;
        }

        public void WaitEnded() { _ended.WaitOne(); }

        public void Send(string type, Dictionary<string, object> payload)
        {
            if (_closed != 0) return;
            if (type == MessageTypes.Close) _closeSent = true;
            var frame = new Frame(type, payload, Interlocked.Increment(ref _nextId));
            bool added;
            // Close() may complete the outbox between the check above and here.
            try { added = _outbox.TryAdd(frame); }
            catch (InvalidOperationException) { return; }
            if (!added)
            {
                // The host has stopped reading: drop it rather than let Streamer.bot wait.
                Close("orchid-host stopped reading messages", true);
            }
        }

        // Runs on the callback thread, in order with incoming messages. Inline if already there.
        public void Run(Action action)
        {
            if (IsCallbackThread) { action(); return; }
            try { _callbacks.Add(action); }
            catch (InvalidOperationException) { }
        }

        // Shows a dialog in the open window and waits for the answer (or -1 on timeout or close).
        public int Dialog(string title, string message, string[] buttons, int primary, TimeSpan timeout)
        {
            var pending = new PendingDialog();
            int id = Interlocked.Increment(ref _nextDialog);
            lock (_dialogLock)
            {
                // Close() sets _closed before it wakes dialogs under this lock, so a dialog is
                // either registered in time to be woken or sees the link closed here.
                if (!_opened.WaitOne(0) || _closed != 0) return -1;
                _dialogs[id] = pending;
            }
            Send(MessageTypes.Dialog, new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "request", id }, { "title", title ?? "" }, { "message", message ?? "" }, { "buttons", buttons }, { "primary", primary },
            });
            bool answered = pending.Done.Wait(timeout);
            lock (_dialogLock) _dialogs.Remove(id);
            if (!answered && IsOpen)
            {
                Send(MessageTypes.DialogCancel, new Dictionary<string, object>(StringComparer.Ordinal) { { "request", id } });
                _log("A dialog got no answer within " + timeout.TotalMinutes.ToString("0", CultureInfo.InvariantCulture) + " minutes and was closed.");
            }
            return answered ? pending.Result : -1;
        }

        private void AcceptAndRun()
        {
            try
            {
                var conn = _listener.Accept(ConnectTimeout);
                if (conn == null)
                {
                    Close("orchid-host didn't connect within " + ConnectTimeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s", true);
                    return;
                }
                _connection = conn;
                if (!Handshake(conn)) return;
                _file.Delete();

                var writer = new Thread(WriteLoop) { IsBackground = true, Name = "OrchidUI writer" };
                writer.Start();
                Send(MessageTypes.Open, _handler.BuildOpenPayload());
                Touch();
                var heartbeat = new Timer(_ => Beat(), null, HeartbeatEvery, HeartbeatEvery);
                _heartbeat = heartbeat;
                if (_closed != 0) heartbeat.Dispose();
                ReadLoop(conn);
            }
            catch (Exception ex)
            {
                Close("link error: " + ex.Message, true);
            }
            finally
            {
                _file.Delete();
            }
        }

        private bool Handshake(Connection conn)
        {
            Frame hello = null;
            Exception error = null;
            var reader = new Thread(() =>
            {
                try { hello = FrameCodec.Read(conn.Stream, MessageTypes.ToShim); }
                catch (Exception ex) { error = ex; }
            }) { IsBackground = true };
            reader.Start();
            if (!reader.Join(HelloTimeout))
            {
                Close("orchid-host connected but didn't say hello", true);
                return false;
            }
            if (hello == null || hello.Type != MessageTypes.Hello)
            {
                Close("orchid-host sent no valid hello" + (error != null ? ": " + error.Message : ""), true);
                return false;
            }
            var expired = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > _file.ExpiresAt;
            if (expired || !Tokens.Equal(_file.Token, Json.Str(hello.Payload, "token")))
            {
                try { FrameCodec.Write(conn.Stream, new Frame(MessageTypes.Refused, new Dictionary<string, object> { { "reason", expired ? "session expired" : "wrong token" } })); }
                catch { }
                Close(expired ? "the session expired before orchid-host connected" : "a connection with the wrong token was refused", true);
                return false;
            }
            var host = Json.Obj(hello.Payload, "host");
            if (host != null) _log("orchid-host " + Json.Str(host, "version", "?") + " on " + Json.Str(host, "os", "?") + " connected (" + conn.Describe + ").");
            return true;
        }

        private void ReadLoop(Connection conn)
        {
            while (_closed == 0)
            {
                Frame frame;
                try { frame = FrameCodec.Read(conn.Stream, MessageTypes.ToShim); }
                catch (FrameException ex)
                {
                    Close("orchid-host sent a bad frame: " + ex.Message, true);
                    return;
                }
                catch (Exception ex)
                {
                    Close(_closeSent ? "window closed" : "the connection to orchid-host was lost: " + ex.Message, !_closeSent);
                    return;
                }
                if (frame == null)
                {
                    Close(_closeSent ? "window closed" : "orchid-host closed the connection", !_closeSent);
                    return;
                }
                Touch();
                switch (frame.Type)
                {
                    case MessageTypes.Ping:
                        break;
                    case MessageTypes.Hello:
                        Close("orchid-host sent a second hello", true);
                        return;
                    case MessageTypes.Ready:
                        _opened.Set();
                        Run(() => _handler.Handle(frame));
                        break;
                    case MessageTypes.DialogResult:
                        CompleteDialog(Json.Int(frame.Payload, "request"), Json.Int(frame.Payload, "index", -1));
                        break;
                    default:
                        var f = frame;
                        Run(() => _handler.Handle(f));
                        break;
                }
            }
        }

        private void CompleteDialog(int id, int index)
        {
            PendingDialog pending;
            lock (_dialogLock) _dialogs.TryGetValue(id, out pending);
            if (pending == null) return;
            if (EndsAfterDialog) _closeSent = true;
            pending.Result = index;
            pending.Done.Set();
        }

        private void WriteLoop()
        {
            try
            {
                foreach (var frame in _outbox.GetConsumingEnumerable())
                {
                    var conn = _connection;
                    if (conn == null) break;
                    FrameCodec.Write(conn.Stream, frame);
                }
            }
            catch (Exception ex)
            {
                Close(_closeSent ? "window closed" : "writing to orchid-host failed: " + ex.Message, !_closeSent);
            }
        }

        private void CallbackLoop()
        {
            foreach (var action in _callbacks.GetConsumingEnumerable())
            {
                try { action(); }
                catch (Exception ex) { _log("Error while handling the settings window: " + ex); }
            }
        }

        private void Touch() { Interlocked.Exchange(ref _lastSeenTicks, DateTime.UtcNow.Ticks); }

        // Runs on a timer thread: nothing may escape, or .NET Framework ends the process.
        private void Beat()
        {
            try { BeatOnce(); }
            catch (Exception ex) { _log("Heartbeat failed: " + ex.Message); }
        }

        private void BeatOnce()
        {
            if (_closed != 0) return;
            var silent = DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastSeenTicks), DateTimeKind.Utc);
            if (silent > DeadAfter)
            {
                Close("orchid-host stopped responding (no heartbeat for " + silent.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s)", true);
                return;
            }
            Send(MessageTypes.Ping, new Dictionary<string, object>());
        }

        public void Close(string reason, bool unexpected)
        {
            if (Interlocked.Exchange(ref _closed, 1) == 1) return;
            try { if (_heartbeat != null) _heartbeat.Dispose(); } catch { }
            _outbox.CompleteAdding();
            var conn = _connection;
            if (conn != null)
            {
                // Give the writer a moment to flush a final "close" before the stream goes away.
                if (!unexpected) Thread.Sleep(50);
                conn.Dispose();
            }
            try { _listener.Dispose(); } catch { }
            _file.Delete();
            lock (_dialogLock)
            {
                foreach (var d in _dialogs.Values) d.Done.Set();
            }
            Action finish = () =>
            {
                try { _handler.OnClosed(reason, unexpected); }
                finally { _ended.Set(); }
            };
            if (IsCallbackThread) finish();
            else
            {
                try { _callbacks.Add(finish); }
                catch (InvalidOperationException) { _ended.Set(); }
            }
            _callbacks.CompleteAdding();
        }

        public void Dispose() { Close("disposed", false); }
    }
}
