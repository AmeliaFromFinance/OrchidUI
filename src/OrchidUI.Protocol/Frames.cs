#if ORCHID_HOST
#nullable disable
#endif
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OrchidUI.Protocol
{
    // One message on the session link: {"v": 1, "id": n, "type": "...", "payload": {...}}.
    internal sealed class Frame
    {
        public Frame(string type, Dictionary<string, object> payload, int id = 0)
        {
            Type = type;
            Payload = payload ?? new Dictionary<string, object>(StringComparer.Ordinal);
            Id = id;
        }

        public int Id { get; private set; }
        public string Type { get; private set; }
        public Dictionary<string, object> Payload { get; private set; }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "v", ProtocolInfo.Major },
                { "id", Id },
                { "type", Type },
                { "payload", Payload },
            };
        }
    }

    internal sealed class FrameException : Exception
    {
        public FrameException() { }
        public FrameException(string message) : base(message) { }
        public FrameException(string message, Exception inner) : base(message, inner) { }
    }

    // Frames are a 4-byte big-endian length followed by that many bytes of UTF-8 JSON.
    internal static class FrameCodec
    {
        // 1 MiB per frame: specs for the largest example are about 40 KB.
        public const int MaxFrameBytes = 1024 * 1024;

        // Reading is strict; writing replaces unpaired surrogates (a stored value or a typed
        // character can carry one) with U+FFFD instead of failing the whole frame.
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly UTF8Encoding Utf8Write = new UTF8Encoding(false, false);

        public static byte[] Encode(Frame frame)
        {
            var json = Utf8Write.GetBytes(Json.Serialize(frame.ToDictionary(), false));
            if (json.Length > MaxFrameBytes) throw new FrameException("Frame of " + json.Length.ToString(CultureInfo.InvariantCulture) + " bytes is over the 1 MiB limit.");
            var buffer = new byte[4 + json.Length];
            buffer[0] = (byte)(json.Length >> 24);
            buffer[1] = (byte)(json.Length >> 16);
            buffer[2] = (byte)(json.Length >> 8);
            buffer[3] = (byte)json.Length;
            Buffer.BlockCopy(json, 0, buffer, 4, json.Length);
            return buffer;
        }

        public static void Write(Stream stream, Frame frame)
        {
            var bytes = Encode(frame);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        // Returns null at a clean end of stream (the other side closed between frames).
        public static Frame Read(Stream stream, ICollection<string> allowedTypes)
        {
            var header = new byte[4];
            int got = ReadFully(stream, header, 4);
            if (got == 0) return null;
            if (got < 4) throw new FrameException("The connection closed in the middle of a frame header.");
            long length = ((long)header[0] << 24) | ((long)header[1] << 16) | ((long)header[2] << 8) | header[3];
            if (length > MaxFrameBytes) throw new FrameException("Frame of " + length.ToString(CultureInfo.InvariantCulture) + " bytes is over the 1 MiB limit.");
            if (length == 0) throw new FrameException("Empty frame.");
            var body = new byte[length];
            if (ReadFully(stream, body, (int)length) < length) throw new FrameException("The connection closed in the middle of a frame.");
            return Decode(body, allowedTypes);
        }

        public static Frame Decode(byte[] body, ICollection<string> allowedTypes)
        {
            string text;
            try { text = Utf8.GetString(body); }
            catch (DecoderFallbackException ex) { throw new FrameException("Frame isn't valid UTF-8.", ex); }

            object parsed;
            try { parsed = Json.Parse(text, 32); }
            catch (FormatException ex) { throw new FrameException("Frame isn't valid JSON: " + ex.Message, ex); }

            var d = parsed as Dictionary<string, object>;
            if (d == null) throw new FrameException("Frame isn't a JSON object.");
            object v;
            if (!d.TryGetValue("v", out v) || !(v is int)) throw new FrameException("Frame has no protocol version.");
            if ((int)v != ProtocolInfo.Major) throw new FrameException("Protocol version " + v + " isn't supported (this side speaks " + ProtocolInfo.Major.ToString(CultureInfo.InvariantCulture) + ").");
            object id;
            if (!d.TryGetValue("id", out id) || !(id is int) || (int)id < 0) throw new FrameException("Frame has no valid id.");
            var type = Json.Str(d, "type");
            if (type == null) throw new FrameException("Frame has no type.");
            if (allowedTypes != null && !allowedTypes.Contains(type)) throw new FrameException("Unknown frame type '" + Truncate(type) + "'.");
            object payload;
            if (!d.TryGetValue("payload", out payload) || !(payload is Dictionary<string, object>)) throw new FrameException("Frame has no payload object.");
            var frame = new Frame(type, (Dictionary<string, object>)payload, (int)id);
            var problem = MessageSchema.Check(frame);
            if (problem != null) throw new FrameException("Frame '" + type + "' is malformed: " + problem);
            return frame;
        }

        private static string Truncate(string s) { return s.Length <= 40 ? s : s.Substring(0, 40) + "…"; }

        private static int ReadFully(Stream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = stream.Read(buffer, total, count - total);
                if (n <= 0) break;
                total += n;
            }
            return total;
        }
    }
}
