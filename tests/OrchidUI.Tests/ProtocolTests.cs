using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using OrchidUI.Protocol;
using Xunit;

namespace OrchidUI.Tests
{
    public class JsonTests
    {
        [Fact]
        public void Parses_the_types_JavaScriptSerializer_gives()
        {
            var d = Json.ParseObject("{\"i\":5,\"big\":12345678901,\"dec\":0.5,\"neg\":-3,\"exp\":1e3,\"s\":\"a\\nb\\u00e9\",\"b\":true,\"n\":null,\"arr\":[1,\"x\"],\"o\":{\"k\":1}}");
            Assert.IsType<int>(d["i"]);
            Assert.IsType<long>(d["big"]);
            Assert.Equal(0.5m, d["dec"]);
            Assert.Equal(-3, d["neg"]);
            Assert.Equal(1000m, d["exp"]);
            Assert.Equal("a\nbé", d["s"]);
            Assert.Equal(true, d["b"]);
            Assert.Null(d["n"]);
            Assert.Equal(new object[] { 1, "x" }, (object[])d["arr"]);
            Assert.Equal(1, ((Dictionary<string, object>)d["o"])["k"]);
        }

        [Fact]
        public void Writes_the_saved_format_exactly()
        {
            var d = new Dictionary<string, object>
            {
                { "s", "q\"\\\t\u0001" }, { "d", 0.1 + 0.2 }, { "m", 0.5m }, { "i", 3 }, { "b", false },
                { "list", new[] { "a", "b" } }, { "rows", new[] { new[] { "x", "1" } } }, { "nan", double.NaN },
            };
            Assert.Equal("{\"s\":\"q\\\"\\\\\\t\\u0001\",\"d\":0.30000000000000004,\"m\":0.5,\"i\":3,\"b\":false,\"list\":[\"a\",\"b\"],\"rows\":[[\"x\",\"1\"]],\"nan\":0}", Json.Serialize(d, false));
            Assert.Equal("{\n  \"list\": [\"a\", \"b\"],\n  \"rows\": [\n    [\"x\", \"1\"]\n  ]\n}", Json.Serialize(new Dictionary<string, object> { { "list", new[] { "a", "b" } }, { "rows", new[] { new[] { "x", "1" } } } }, true));
        }

        [Theory]
        [InlineData("{")]
        [InlineData("{\"a\":}")]
        [InlineData("[1,]")]
        [InlineData("\"unterminated")]
        [InlineData("{\"a\":1} trailing")]
        [InlineData("nul")]
        [InlineData("{\"a\":\"\\x\"}")]
        public void Rejects_malformed_json(string text)
        {
            Assert.Throws<FormatException>(() => Json.Parse(text));
        }

        [Fact]
        public void Refuses_deep_nesting()
        {
            var deep = new string('[', 100) + new string(']', 100);
            var ex = Assert.Throws<FormatException>(() => Json.Parse(deep));
            Assert.Contains("deeper", ex.Message);
        }

        [Fact]
        public void An_empty_store_is_an_empty_object()
        {
            Assert.Empty(Json.ParseObject(null));
            Assert.Empty(Json.ParseObject("  "));
            Assert.Empty(Json.ParseObject("[1,2]"));
        }
    }

    public class FrameTests
    {
        private static byte[] Raw(string json)
        {
            var body = Encoding.UTF8.GetBytes(json);
            var b = new byte[4 + body.Length];
            b[0] = (byte)(body.Length >> 24); b[1] = (byte)(body.Length >> 16); b[2] = (byte)(body.Length >> 8); b[3] = (byte)body.Length;
            Buffer.BlockCopy(body, 0, b, 4, body.Length);
            return b;
        }

        private static Frame Read(byte[] bytes, ICollection<string> allowed = null)
        {
            return FrameCodec.Read(new MemoryStream(bytes), allowed ?? MessageTypes.ToShim);
        }

        [Fact]
        public void Round_trips_a_frame()
        {
            var f = new Frame(MessageTypes.Change, new Dictionary<string, object> { { "field", "f3" }, { "key", "enabled" }, { "value", true } }, 7);
            var back = Read(FrameCodec.Encode(f));
            Assert.Equal(7, back.Id);
            Assert.Equal("change", back.Type);
            Assert.Equal(true, back.Payload["value"]);
        }

        [Fact]
        public void Clean_end_of_stream_is_null()
        {
            Assert.Null(Read(new byte[0]));
        }

        [Fact]
        public void Rejects_a_truncated_header_and_body()
        {
            Assert.Throws<FrameException>(() => Read(new byte[] { 0, 0 }));
            var full = FrameCodec.Encode(new Frame("ping", null));
            Assert.Throws<FrameException>(() => Read(full.Take(full.Length - 3).ToArray()));
        }

        [Fact]
        public void Rejects_frames_over_1_MiB_without_reading_them()
        {
            var header = new byte[] { 0x00, 0x10, 0x00, 0x01 };
            var ex = Assert.Throws<FrameException>(() => Read(header));
            Assert.Contains("1 MiB", ex.Message);
            Assert.Throws<FrameException>(() => FrameCodec.Encode(new Frame("toast", new Dictionary<string, object> { { "message", new string('x', FrameCodec.MaxFrameBytes) } })));
        }

        [Theory]
        [InlineData("{\"v\":2,\"id\":1,\"type\":\"ping\",\"payload\":{}}", "version 2")]
        [InlineData("{\"v\":1,\"id\":1,\"type\":\"launch\",\"payload\":{}}", "Unknown frame type")]
        [InlineData("{\"v\":1,\"id\":1,\"type\":\"open\",\"payload\":{}}", "Unknown frame type")]
        [InlineData("{\"v\":1,\"id\":-1,\"type\":\"ping\",\"payload\":{}}", "id")]
        [InlineData("{\"v\":1,\"id\":1,\"type\":\"ping\",\"payload\":[]}", "payload")]
        [InlineData("{\"v\":1,\"id\":1,\"type\":\"change\",\"payload\":{\"field\":\"f1\",\"key\":\"k\"}}", "value")]
        [InlineData("{\"v\":1,\"id\":1,\"type\":\"change\",\"payload\":{\"field\":\"../x\",\"key\":\"k\",\"value\":1}}", "valid id")]
        [InlineData("{\"v\":1,\"id\":1,\"type\":\"command\",\"payload\":{\"name\":\"format-disk\"}}", "unknown command")]
        [InlineData("{\"v\":1,\"id\":1,\"type\":\"dialogResult\",\"payload\":{\"request\":\"1\",\"index\":0}}", "wrong type")]
        [InlineData("{\"v\":1,\"id\":1,\"type\":\"change\",\"payload\":{\"field\":\"f1\",\"key\":\"k\",\"value\":\"/x\",\"native\":\"yes\"}}", "native")]
        [InlineData("{\"v\":1,\"id\":1,\"type\":\"command\",\"payload\":{\"name\":\"import\",\"path\":7}}", "path")]
        [InlineData("{\"v\":1,\"id\":1,\"type\":\"command\",\"payload\":{\"name\":\"export\",\"path\":\"/x.json\",\"native\":1}}", "native")]
        [InlineData("[1]", "object")]
        public void Rejects_malformed_frames(string json, string expected)
        {
            var ex = Assert.Throws<FrameException>(() => Read(Raw(json)));
            Assert.Contains(expected, ex.Message);
        }

        // A native path on change and command, and copy (to the host).
        [Fact]
        public void Reads_native_paths_and_the_copy_frame()
        {
            var change = Read(Raw("{\"v\":1,\"id\":1,\"type\":\"change\",\"payload\":{\"field\":\"f1\",\"key\":\"k\",\"value\":\"/home/quietfern/a.json\",\"native\":true}}"), MessageTypes.ToShim);
            Assert.True(Json.Bool(change.Payload, "native"));
            var command = Read(Raw("{\"v\":1,\"id\":2,\"type\":\"command\",\"payload\":{\"name\":\"import\",\"path\":\"/home/quietfern/a.json\",\"native\":true}}"), MessageTypes.ToShim);
            Assert.Equal("/home/quietfern/a.json", Json.Str(command.Payload, "path"));
            var copy = FrameCodec.Encode(new Frame(MessageTypes.Clipboard, new Dictionary<string, object> { { "text", "OrchidUI diagnostics" } }));
            Assert.Equal("OrchidUI diagnostics", Json.Str(Read(copy, MessageTypes.ToHost).Payload, "text"));
            Assert.Throws<FrameException>(() => Read(copy, MessageTypes.ToShim));
            Assert.Throws<FrameException>(() => Read(Raw("{\"v\":1,\"id\":3,\"type\":\"copy\",\"payload\":{}}"), MessageTypes.ToHost));
        }

        [Fact]
        public void Host_bound_types_are_refused_by_the_shim_and_the_other_way_round()
        {
            var open = FrameCodec.Encode(new Frame(MessageTypes.Open, new Dictionary<string, object> { { "spec", new Dictionary<string, object>() } }));
            Assert.Throws<FrameException>(() => Read(open, MessageTypes.ToShim));
            Assert.NotNull(Read(open, MessageTypes.ToHost));
        }

        [Fact]
        public void Rejects_invalid_utf8()
        {
            var bytes = new byte[] { 0, 0, 0, 2, 0xC3, 0x28 };
            Assert.Throws<FrameException>(() => Read(bytes));
        }

        private static readonly string[] SafeTypes = { MessageTypes.Ping, MessageTypes.Ready, MessageTypes.Change, MessageTypes.Log, MessageTypes.Click };

        // Any frame the shim could build survives encode/decode.
        [FsCheck.Xunit.Property(MaxTest = 300)]
        public bool Generated_frames_round_trip(NonNegativeInt id, string text, int number, bool flag)
        {
            var payload = new Dictionary<string, object>
            {
                { "field", "f" + (id.Get % 1000) }, { "key", "k" }, { "value", text ?? "" },
                { "level", "info" }, { "message", text ?? "" }, { "n", number }, { "flag", flag },
            };
            var type = SafeTypes[id.Get % SafeTypes.Length];
            var back = Read(FrameCodec.Encode(new Frame(type, payload, id.Get)));
            return back.Id == id.Get && back.Type == type && Equals(back.Payload["n"], number) && (string)back.Payload["message"] == (text ?? "");
        }

        // Random bytes either decode or fail with FrameException; nothing else escapes, nothing hangs.
        [FsCheck.Xunit.Property(MaxTest = 500)]
        public bool Random_bytes_only_ever_raise_frame_errors(byte[] data)
        {
            try
            {
                Read(data ?? new byte[0]);
                return true;
            }
            catch (FrameException)
            {
                return true;
            }
        }

        // A valid header followed by random JSON-ish text.
        [FsCheck.Xunit.Property(MaxTest = 500)]
        public bool Random_bodies_only_ever_raise_frame_errors(string body)
        {
            try
            {
                Read(Raw(body ?? ""));
                return true;
            }
            catch (FrameException)
            {
                return true;
            }
        }

        [Fact]
        public void An_unpaired_surrogate_is_replaced_instead_of_failing_the_frame()
        {
            var bytes = FrameCodec.Encode(new Frame(MessageTypes.Toast, new Dictionary<string, object> { { "message", "hi \ud83d there" } }, 1));
            var body = new byte[bytes.Length - 4];
            Array.Copy(bytes, 4, body, 0, body.Length);
            var f = FrameCodec.Decode(body, MessageTypes.ToHost);
            Assert.Equal("hi \ufffd there", Json.Str(f.Payload, "message"));
        }
    }
}
