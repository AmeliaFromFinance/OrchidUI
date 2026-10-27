#if ORCHID_HOST
#nullable disable
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OrchidUI.Protocol
{
    // JSON for both sides of the link and for the saved settings. C# 7.3 and no dependencies, so it
    // compiles into the net481 DLL and the .NET 10 host alike.
    //
    // Parsing gives the same .NET types as JavaScriptSerializer: objects are
    // Dictionary<string, object>, arrays object[], whole numbers int (or long), other numbers
    // decimal (or double when decimal can't hold them). Serialize always writes the same
    // bytes for the same values, so the stored format never drifts.
    internal static class Json
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public const int DefaultMaxDepth = 64;

        public static Dictionary<string, object> ParseObject(string json)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(json)) return result;
            var parsed = Parse(json) as Dictionary<string, object>;
            if (parsed == null) return result;
            foreach (var kv in parsed) result[kv.Key] = kv.Value;
            return result;
        }

        public static object Parse(string json, int maxDepth = DefaultMaxDepth)
        {
            if (json == null) throw new FormatException("No JSON.");
            var p = new Parser(json, maxDepth);
            p.SkipWhitespace();
            var value = p.ReadValue(0);
            p.SkipWhitespace();
            if (!p.AtEnd) throw p.Error("Unexpected text after the JSON value");
            return value;
        }

        private sealed class Parser
        {
            private readonly string _s;
            private readonly int _maxDepth;
            private int _i;

            public Parser(string s, int maxDepth)
            {
                _s = s;
                _maxDepth = maxDepth;
            }

            public bool AtEnd { get { return _i >= _s.Length; } }

            public FormatException Error(string message)
            {
                return new FormatException(message + " at character " + (_i + 1).ToString(Inv) + ".");
            }

            public void SkipWhitespace()
            {
                while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\n' || _s[_i] == '\r' || _s[_i] == '\uFEFF')) _i++;
            }

            public object ReadValue(int depth)
            {
                if (depth > _maxDepth) throw Error("JSON nests deeper than " + _maxDepth.ToString(Inv) + " levels");
                if (AtEnd) throw Error("Unexpected end of JSON");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error("Unexpected character '" + c + "'");
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw Error("Expected " + word);
                _i += word.Length;
            }

            private Dictionary<string, object> ReadObject(int depth)
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                _i++;
                SkipWhitespace();
                if (!AtEnd && _s[_i] == '}') { _i++; return d; }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != '"') throw Error("Expected a property name");
                    string key = ReadString();
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != ':') throw Error("Expected ':'");
                    _i++;
                    SkipWhitespace();
                    d[key] = ReadValue(depth + 1);
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unexpected end of JSON");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return d; }
                    throw Error("Expected ',' or '}'");
                }
            }

            private object[] ReadArray(int depth)
            {
                var list = new List<object>();
                _i++;
                SkipWhitespace();
                if (!AtEnd && _s[_i] == ']') { _i++; return list.ToArray(); }
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unexpected end of JSON");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return list.ToArray(); }
                    throw Error("Expected ',' or ']'");
                }
            }

            private string ReadString()
            {
                _i++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("Unterminated string");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Error("Unterminated string");
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw Error("Bad \\u escape");
                            int code;
                            if (!int.TryParse(_s.Substring(_i, 4), NumberStyles.HexNumber, Inv, out code)) throw Error("Bad \\u escape");
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default: throw Error("Bad escape \\" + e);
                    }
                }
            }

            private object ReadNumber()
            {
                int start = _i;
                if (_s[_i] == '-') _i++;
                while (!AtEnd && ((_s[_i] >= '0' && _s[_i] <= '9') || _s[_i] == '.' || _s[_i] == 'e' || _s[_i] == 'E' || _s[_i] == '+' || _s[_i] == '-')) _i++;
                string text = _s.Substring(start, _i - start);
                bool whole = text.IndexOfAny(new[] { '.', 'e', 'E' }) < 0;
                if (whole)
                {
                    int i;
                    if (int.TryParse(text, NumberStyles.AllowLeadingSign, Inv, out i)) return i;
                    long l;
                    if (long.TryParse(text, NumberStyles.AllowLeadingSign, Inv, out l)) return l;
                }
                decimal m;
                if (decimal.TryParse(text, NumberStyles.Float, Inv, out m)) return m;
                double d;
                if (double.TryParse(text, NumberStyles.Float, Inv, out d)) return d;
                _i = start;
                throw Error("Bad number '" + text + "'");
            }
        }

        // Writer: keep it unchanged, or saved settings change format.

        public static string Serialize(object value, bool indent)
        {
            var sb = new StringBuilder();
            Write(sb, value, indent, 0);
            return sb.ToString();
        }

        private static void NewLine(StringBuilder sb, bool indent, int depth)
        {
            if (!indent) return;
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        private static void Write(StringBuilder sb, object o, bool indent, int depth)
        {
            if (o == null) { sb.Append("null"); return; }
            if (o is string) { WriteString(sb, (string)o); return; }
            if (o is bool) { sb.Append((bool)o ? "true" : "false"); return; }
            if (o is double || o is float || o is decimal)
            {
                double d = Convert.ToDouble(o, Inv);
                if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append('0');
                else sb.Append(d.ToString("R", Inv));
                return;
            }
            if (o is int || o is long || o is short || o is byte || o is uint || o is ulong || o is ushort || o is sbyte)
            {
                sb.Append(Convert.ToString(o, Inv));
                return;
            }
            if (o is Enum) { WriteString(sb, o.ToString()); return; }
            var dict = o as IDictionary;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry e in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    NewLine(sb, indent, depth + 1);
                    WriteString(sb, Convert.ToString(e.Key, Inv));
                    sb.Append(indent ? ": " : ":");
                    Write(sb, e.Value, indent, depth + 1);
                }
                if (!first) NewLine(sb, indent, depth);
                sb.Append('}');
                return;
            }
            var list = o as IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                bool nested = false;
                foreach (var item in list)
                {
                    if (!first) sb.Append(indent && !nested ? ", " : ",");
                    bool itemIsContainer = item is IEnumerable && !(item is string);
                    if (itemIsContainer) { nested = true; NewLine(sb, indent, depth + 1); }
                    first = false;
                    Write(sb, item, indent, depth + 1);
                }
                if (nested) NewLine(sb, indent, depth);
                sb.Append(']');
                return;
            }
            WriteString(sb, Convert.ToString(o, Inv));
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", Inv));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // Small helpers for reading parsed objects.

        public static string Str(IDictionary<string, object> d, string key, string fallback = null)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v is string ? (string)v : fallback;
        }

        public static bool Bool(IDictionary<string, object> d, string key, bool fallback = false)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v is bool ? (bool)v : fallback;
        }

        public static double Num(IDictionary<string, object> d, string key, double fallback = 0)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null || v is string || v is bool) return fallback;
            try { return Convert.ToDouble(v, Inv); }
            catch (FormatException) { return fallback; }
            catch (InvalidCastException) { return fallback; }
            catch (OverflowException) { return fallback; }
        }

        public static int Int(IDictionary<string, object> d, string key, int fallback = 0)
        {
            double n = Num(d, key, double.NaN);
            if (double.IsNaN(n) || n < int.MinValue || n > int.MaxValue) return fallback;
            return (int)Math.Round(n);
        }

        public static Dictionary<string, object> Obj(IDictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        public static object[] Arr(IDictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as object[] : null;
        }

        public static object Get(IDictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }
    }
}
