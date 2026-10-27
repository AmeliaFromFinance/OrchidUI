using System.IO.Pipes;
using System.Net.Sockets;
using Avalonia.Threading;
using OrchidUI.Host.Views;
using OrchidUI.Protocol;

namespace OrchidUI.Host.Session;

// What the session file says.
internal sealed record SessionInfo(string Id, string Transport, string? Pipe, int Port, string Token, long ExpiresAt)
{
    // Reads the file and deletes it straight away: the token is single-use.
    public static SessionInfo Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Session file not found.", path);
        if (info.Length > 64 * 1024) throw new InvalidDataException("Session file is too large.");
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(path);
            if ((mode & (UnixFileMode.OtherWrite | UnixFileMode.GroupWrite)) != 0) throw new InvalidDataException("Session file is writable by other users; refusing it.");
            // Under Wine the DLL can't set Unix permissions, so its files are usually 0644. That's
            // only safe inside a folder other users can't enter (XDG_RUNTIME_DIR, macOS TMPDIR, or
            // a folder --watch made 0700).
            if ((mode & (UnixFileMode.OtherRead | UnixFileMode.GroupRead)) != 0 && !IsBehindPrivateDirectory(path))
            {
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw new InvalidDataException("Session file can be read by other users and its folder isn't private; refusing it. Set ORCHIDUI_SESSION_DIR to a folder only you can open (see docs/linux.md).");
            }
        }
        string text = File.ReadAllText(path);
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        var d = Json.Parse(text) as Dictionary<string, object> ?? throw new InvalidDataException("Session file isn't a JSON object.");
        if (Json.Int(d, "protocol", -1) != ProtocolInfo.Major) throw new InvalidDataException("Session file is for another protocol version.");
        var s = new SessionInfo(Json.Str(d, "id", ""), Json.Str(d, "transport", "tcp"), Json.Str(d, "pipe"), Json.Int(d, "port"), Json.Str(d, "token", ""), (long)Json.Num(d, "expiresAt"));
        if (s.Token.Length != ProtocolInfo.TokenHexLength) throw new InvalidDataException("Session file has no valid token.");
        if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > s.ExpiresAt) throw new InvalidDataException("Session file has expired.");
        if (s.Transport == "tcp" && (s.Port <= 0 || s.Port > 65535)) throw new InvalidDataException("Session file has no valid port.");
        if (s.Transport == "pipe" && string.IsNullOrEmpty(s.Pipe)) throw new InvalidDataException("Session file has no pipe name.");
        return s;
    }

    // True when some folder above the file can't be entered by group or others, so nobody else
    // can reach the file whatever its own mode.
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    internal static bool IsBehindPrivateDirectory(string path)
    {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!); dir is not null; dir = dir.Parent)
        {
            UnixFileMode mode;
            try { mode = File.GetUnixFileMode(dir.FullName); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            if ((mode & (UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0) return true;
        }
        return false;
    }
}

// The host's end of the link: frames in on a reader thread, handed to the UI thread; frames out
// from any thread under a lock; heartbeats both ways.
internal sealed class SessionClient : ILinkSink, IDisposable
{
    private readonly Stream _stream;
    private readonly Action _closeTransport;
    private readonly object _writeLock = new();
    private readonly Timer _heartbeat;
    private long _lastSeen = DateTime.UtcNow.Ticks;
    private int _nextId;
    private int _closed;

    private SessionClient(Stream stream, Action close)
    {
        _stream = stream;
        _closeTransport = close;
        _heartbeat = new Timer(_ => Beat(), null, TimeSpan.FromSeconds(ProtocolInfo.HeartbeatSeconds), TimeSpan.FromSeconds(ProtocolInfo.HeartbeatSeconds));
    }

    public event Action<Frame>? FrameReceived;
    public event Action<string>? Lost;

    public static async Task<SessionClient> ConnectAsync(SessionInfo info, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        if (info.Transport == "pipe")
        {
            var pipe = new NamedPipeClientStream(".", info.Pipe!, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token);
            return new SessionClient(pipe, pipe.Dispose);
        }
        var tcp = new TcpClient { NoDelay = true };
        await tcp.ConnectAsync(System.Net.IPAddress.Loopback, info.Port, timeout.Token);
        return new SessionClient(tcp.GetStream(), tcp.Dispose);
    }

    public void Hello(string token)
    {
        Send(MessageTypes.Hello, new Dictionary<string, object>(StringComparer.Ordinal)
        {
            { "token", token },
            { "host", new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "version", HostInfo.Version },
                    { "os", HostInfo.Os },
                    { "windowSystem", HostInfo.WindowSystem },
                } },
        });
    }

    public void StartReading()
    {
        var t = new Thread(ReadLoop) { IsBackground = true, Name = "orchid-host reader" };
        t.Start();
    }

    private void ReadLoop()
    {
        try
        {
            while (_closed == 0)
            {
                var frame = FrameCodec.Read(_stream, MessageTypes.ToHost);
                if (frame is null) { Fail("Streamer.bot closed the connection"); return; }
                Interlocked.Exchange(ref _lastSeen, DateTime.UtcNow.Ticks);
                if (frame.Type == MessageTypes.Ping) continue;
                Dispatcher.UIThread.Post(() => FrameReceived?.Invoke(frame));
            }
        }
        catch (FrameException ex) { Fail("bad frame from Streamer.bot: " + ex.Message); }
        catch (Exception ex) when (_closed == 0) { Fail("connection lost: " + ex.Message); }
        catch (Exception) { }
    }

    public void Send(string type, Dictionary<string, object> payload)
    {
        if (_closed != 0) return;
        try
        {
            lock (_writeLock) FrameCodec.Write(_stream, new Frame(type, payload, Interlocked.Increment(ref _nextId)));
        }
        catch (Exception ex)
        {
            // Called from UI event handlers: a failure ends the session, it never escapes.
            Fail("sending failed: " + ex.Message);
        }
    }

    private void Beat()
    {
        if (_closed != 0) return;
        var silent = DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastSeen), DateTimeKind.Utc);
        if (silent > TimeSpan.FromSeconds(ProtocolInfo.DeadAfterSeconds)) { Fail("Streamer.bot stopped responding"); return; }
        Send(MessageTypes.Ping, new Dictionary<string, object>());
    }

    private void Fail(string reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1) return;
        _heartbeat.Dispose();
        try { _closeTransport(); } catch (Exception) { }
        Dispatcher.UIThread.Post(() => Lost?.Invoke(reason));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1) return;
        _heartbeat.Dispose();
        try { _closeTransport(); } catch (Exception) { }
    }
}

internal static class HostInfo
{
    public static string Version => typeof(HostInfo).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public static string Os =>
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsLinux() ? "linux" : "other";

    public static string WindowSystem
    {
        get
        {
            if (OperatingSystem.IsWindows()) return "win32";
            if (OperatingSystem.IsMacOS()) return "cocoa";
            bool wayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
            return wayland ? "x11 (xwayland)" : "x11";
        }
    }
}
