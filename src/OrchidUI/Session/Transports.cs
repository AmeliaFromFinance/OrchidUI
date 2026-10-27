using System;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
#if NETFRAMEWORK
using System.Security.AccessControl;
using System.Security.Principal;
#endif

namespace OrchidUI
{
    // An accepted connection from orchid-host.
    internal sealed class Connection : IDisposable
    {
        private readonly Action _close;
        private int _closed;

        public Connection(Stream stream, string describe, Action close)
        {
            Stream = stream;
            Describe = describe;
            _close = close;
        }

        public Stream Stream { get; private set; }
        public string Describe { get; private set; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 1) return;
            try { _close(); } catch { }
        }
    }

    // Waits for exactly one host to connect, then stops listening.
    internal interface IListener : IDisposable
    {
        string Kind { get; }
        Connection Accept(TimeSpan timeout);
    }

    // Wine sessions: Wine's named pipes can't reach a native process and its AF_UNIX support is
    // Staging-only, so it's loopback TCP. The port closes after the first client.
    internal sealed class TcpListenerTransport : IListener
    {
        private readonly TcpListener _listener;

        public TcpListenerTransport()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Server.ExclusiveAddressUse = true;
            _listener.Start(1);
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        }

        public string Kind { get { return "tcp"; } }
        public int Port { get; private set; }

        public Connection Accept(TimeSpan timeout)
        {
            try
            {
                var task = _listener.AcceptTcpClientAsync();
                if (!task.Wait(timeout)) return null;
                var client = task.Result;
                client.NoDelay = true;
                // Whatever happens next, nobody else gets to connect.
                _listener.Stop();
                var remote = client.Client.RemoteEndPoint as IPEndPoint;
                if (remote == null || !IPAddress.IsLoopback(remote.Address))
                {
                    client.Close();
                    return null;
                }
                return new Connection(client.GetStream(), "127.0.0.1:" + Port, () => client.Close());
            }
            catch (AggregateException) { return null; }
            catch (ObjectDisposedException) { return null; }
            catch (SocketException) { return null; }
            finally { Stop(); }
        }

        private void Stop()
        {
            try { _listener.Stop(); } catch { }
        }

        public void Dispose() { Stop(); }
    }

    // Windows sessions: a named pipe only the current user can open.
    internal sealed class PipeListenerTransport : IListener
    {
        private readonly NamedPipeServerStream _server;

        public PipeListenerTransport(string name)
        {
            Name = name;
#if NETFRAMEWORK
            var security = new PipeSecurity();
            var me = WindowsIdentity.GetCurrent().User;
            security.AddAccessRule(new PipeAccessRule(me, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
            security.SetOwner(me);
            _server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 64 * 1024, 64 * 1024, security);
#else
            // CurrentUserOnly: on Windows the pipe's ACL allows only this user; on Unix the socket file is 0600.
            _server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
#endif
        }

        public string Kind { get { return "pipe"; } }
        public string Name { get; private set; }

        public Connection Accept(TimeSpan timeout)
        {
            try
            {
                var task = _server.WaitForConnectionAsync();
                if (!task.Wait(timeout)) { _server.Dispose(); return null; }
                return new Connection(_server, "pipe " + Name, () => _server.Dispose());
            }
            catch (AggregateException) { _server.Dispose(); return null; }
            catch (IOException) { _server.Dispose(); return null; }
            catch (ObjectDisposedException) { return null; }
        }

        public void Dispose()
        {
            if (!_server.IsConnected) { try { _server.Dispose(); } catch { } }
        }
    }

    internal static class Tokens
    {
        public static string New()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var sb = new StringBuilder(64);
            foreach (var b in bytes) sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        // Compares every character whatever the input, so timing says nothing about the token.
        public static bool Equal(string expected, string actual)
        {
            if (expected == null || actual == null) return false;
            int diff = expected.Length ^ actual.Length;
            for (int i = 0; i < expected.Length; i++)
            {
                char a = expected[i];
                char b = i < actual.Length ? actual[i] : '\0';
                diff |= a ^ b;
            }
            return diff == 0;
        }
    }
}
