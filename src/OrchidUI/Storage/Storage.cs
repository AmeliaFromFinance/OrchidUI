using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using OrchidUI.Protocol;

namespace OrchidUI
{
    // Calls Streamer.bot's CPH through reflection, so the DLL never references Streamer.bot's
    // assemblies and keeps working across Streamer.bot updates.
    internal sealed class CphBridge
    {
        private readonly object _cph;
        private readonly List<MethodInfo> _methods = new List<MethodInfo>();

        public CphBridge(object cph)
        {
            if (cph == null) throw new ArgumentNullException("cph", "Pass the CPH object from your Streamer.bot action.");
            _cph = cph;
            var type = cph.GetType();
            _methods.AddRange(type.GetMethods(BindingFlags.Public | BindingFlags.Instance));
            foreach (var itf in type.GetInterfaces()) _methods.AddRange(itf.GetMethods());
        }

        public object Raw { get { return _cph; } }

        private MethodInfo Find(string name, int paramCount, bool generic)
        {
            foreach (var m in _methods)
            {
                if (m.Name != name || m.IsGenericMethodDefinition != generic) continue;
                var p = m.GetParameters();
                if (p.Length != paramCount) continue;
                if (paramCount > 0 && p[0].ParameterType != typeof(string)) continue;
                return m;
            }
            return null;
        }

        private object Call(MethodInfo m, params object[] args)
        {
            if (m == null) return null;
            try { return m.Invoke(_cph, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        public string GetGlobalString(string name)
        {
            var m = Find("GetGlobalVar", 2, true);
            if (m == null) return null;
            try { return Call(m.MakeGenericMethod(typeof(string)), name, true) as string; }
            catch
            {
                try
                {
                    var o = Call(m.MakeGenericMethod(typeof(object)), name, true);
                    return o == null ? null : o.ToString();
                }
                catch { return null; }
            }
        }

        public void SetGlobal(string name, object value)
        {
            var m = Find("SetGlobalVar", 3, false);
            if (m == null) throw new MissingMethodException("CPH.SetGlobalVar(string, object, bool) was not found.");
            Call(m, name, value, true);
        }

        public bool RunAction(string actionName)
        {
            var m = Find("RunAction", 2, false);
            if (m == null) return false;
            var r = Call(m, actionName, true);
            return r is bool && (bool)r;
        }

        // level is a CPH method name: LogInfo, LogWarn, LogError, LogDebug
        public void Log(string level, string message)
        {
            var m = Find(level, 1, false) ?? Find("LogInfo", 1, false);
            if (m == null) return;
            try { Call(m, message); } catch { }
        }
    }

    internal static class ValueConverter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static T To<T>(object value, T fallback)
        {
            object r;
            return TryConvert(value, typeof(T), out r) && r != null ? (T)r : fallback;
        }

        public static object ConvertOrDefault(object value, Type type, object fallback)
        {
            object r;
            return TryConvert(value, type, out r) && r != null ? r : fallback;
        }

        private static bool IsNumericType(Type t)
        {
            return t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
                || t == typeof(double) || t == typeof(float) || t == typeof(decimal)
                || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte);
        }

        private static bool IsNumber(object o) { return o != null && IsNumericType(o.GetType()); }

        public static bool TryConvert(object value, Type type, out object result)
        {
            result = null;
            if (value == null) return false;
            var t = Nullable.GetUnderlyingType(type) ?? type;
            try
            {
                if (t == typeof(object)) { result = value; return true; }

                if (t == typeof(string))
                {
                    if (value is string) { result = value; return true; }
                    if (value is bool) { result = (bool)value ? "true" : "false"; return true; }
                    if (value is IEnumerable) { result = string.Join(", ", ToStringArray(value)); return true; }
                    result = Convert.ToString(value, Inv);
                    return true;
                }

                if (t == typeof(bool))
                {
                    var s = value as string;
                    if (s != null)
                    {
                        s = s.Trim().ToLowerInvariant();
                        if (s == "true" || s == "1" || s == "yes" || s == "on") { result = true; return true; }
                        if (s == "false" || s == "0" || s == "no" || s == "off" || s.Length == 0) { result = false; return true; }
                        return false;
                    }
                    result = Convert.ToBoolean(value, Inv);
                    return true;
                }

                if (t.IsEnum)
                {
                    result = Enum.Parse(t, Convert.ToString(value, Inv), true);
                    return true;
                }

                if (t == typeof(TimeSpan))
                {
                    if (value is TimeSpan) { result = value; return true; }
                    long secs;
                    if (DurationUtil.TryParseSeconds(Convert.ToString(value, Inv), out secs)) { result = TimeSpan.FromSeconds(secs); return true; }
                    return false;
                }

                if (t == typeof(string[])) { result = ToStringArray(value); return result != null; }
                if (t == typeof(List<string>))
                {
                    var a = ToStringArray(value);
                    if (a == null) return false;
                    result = new List<string>(a);
                    return true;
                }
                if (t == typeof(string[][])) { result = ToJagged(value); return result != null; }
                if (t == typeof(List<string[]>))
                {
                    var j = ToJagged(value);
                    if (j == null) return false;
                    result = new List<string[]>(j);
                    return true;
                }

                if (IsNumericType(t))
                {
                    var s = value as string;
                    if (s != null)
                    {
                        double parsed;
                        if (!double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, Inv, out parsed)) return false;
                        value = parsed;
                    }
                    if (value is bool) value = (bool)value ? 1 : 0;
                    if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
                        || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte))
                    {
                        double d = Convert.ToDouble(value, Inv);
                        result = Convert.ChangeType(Math.Round(d, MidpointRounding.AwayFromZero), t, Inv);
                        return true;
                    }
                    result = Convert.ChangeType(value, t, Inv);
                    return true;
                }

                if (t.IsInstanceOfType(value)) { result = value; return true; }
                if (typeof(IConvertible).IsAssignableFrom(t) && value is IConvertible)
                {
                    result = Convert.ChangeType(value, t, Inv);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static string[] ToStringArray(object value)
        {
            if (value == null) return null;
            var s = value as string;
            if (s != null)
            {
                var trimmed = s.Trim();
                if (trimmed.StartsWith("["))
                {
                    try
                    {
                        var parsed = Json.Parse(trimmed);
                        if (parsed is IEnumerable && !(parsed is string)) return ToStringArray(parsed);
                    }
                    catch { }
                }
                return trimmed.Length == 0 ? new string[0] : new[] { s };
            }
            var e = value as IEnumerable;
            if (e == null) return new[] { Convert.ToString(value, Inv) };
            var list = new List<string>();
            foreach (var item in e)
            {
                if (item == null) list.Add("");
                else if (item is string) list.Add((string)item);
                else if (item is IEnumerable) list.Add(string.Join("|", ToStringArray(item)));
                else if (item is bool) list.Add((bool)item ? "true" : "false");
                else list.Add(Convert.ToString(item, Inv));
            }
            return list.ToArray();
        }

        public static string[][] ToJagged(object value)
        {
            if (value == null) return null;
            var s = value as string;
            if (s != null)
            {
                var trimmed = s.Trim();
                if (trimmed.Length == 0) return new string[0][];
                if (trimmed.StartsWith("["))
                {
                    try
                    {
                        var parsed = Json.Parse(trimmed);
                        if (parsed is IEnumerable && !(parsed is string)) return ToJagged(parsed);
                    }
                    catch { }
                }
                return new[] { s.Split('|') };
            }
            var e = value as IEnumerable;
            if (e == null) return null;
            var rows = new List<string[]>();
            foreach (var item in e)
            {
                if (item == null) rows.Add(new string[0]);
                else if (item is string) rows.Add(((string)item).Split('|'));
                else if (item is IEnumerable) rows.Add(ToStringArray(item));
                else rows.Add(new[] { Convert.ToString(item, Inv) });
            }
            return rows.ToArray();
        }

        // Copies arrays so stored values are never shared with UI state.
        public static object Clone(object value)
        {
            var jag = value as string[][];
            if (jag != null) return jag.Select(r => r == null ? new string[0] : (string[])r.Clone()).ToArray();
            var arr = value as string[];
            if (arr != null) return arr.Clone();
            var list = value as List<string>;
            if (list != null) return new List<string>(list);
            return value;
        }

        public static bool LooseEquals(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a is bool || b is bool)
            {
                object x, y;
                return TryConvert(a, typeof(bool), out x) && TryConvert(b, typeof(bool), out y) && (bool)x == (bool)y;
            }
            if (IsNumber(a) || IsNumber(b))
            {
                object x, y;
                if (TryConvert(a, typeof(double), out x) && TryConvert(b, typeof(double), out y))
                    return Math.Abs((double)x - (double)y) < 1e-9;
                return false;
            }
            bool aList = a is IEnumerable && !(a is string);
            bool bList = b is IEnumerable && !(b is string);
            if (aList || bList)
                return aList && bList && Json.Serialize(a, false) == Json.Serialize(b, false);
            return string.Equals(Convert.ToString(a, Inv), Convert.ToString(b, Inv), StringComparison.Ordinal);
        }
    }

    internal static class DurationUtil
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static long Total(long d, long h, long m, long s) { return d * 86400 + h * 3600 + m * 60 + s; }

        // dd:hh:mm:ss
        public static string Format(long total)
        {
            if (total < 0) total = 0;
            long d = total / 86400; total %= 86400;
            long h = total / 3600; total %= 3600;
            long m = total / 60;
            long s = total % 60;
            return string.Format(Inv, "{0:00}:{1:00}:{2:00}:{3:00}", d, h, m, s);
        }

        // Accepts dd:hh:mm:ss, hh:mm:ss, mm:ss or plain seconds.
        public static bool TryParseSeconds(string text, out long seconds)
        {
            seconds = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = text.Trim().Split(':');
            var nums = new long[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                double v;
                if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, Inv, out v)) return false;
                nums[i] = (long)Math.Max(0, Math.Round(v));
            }
            switch (nums.Length)
            {
                case 1: seconds = nums[0]; return true;
                case 2: seconds = Total(0, 0, nums[0], nums[1]); return true;
                case 3: seconds = Total(0, nums[0], nums[1], nums[2]); return true;
                case 4: seconds = Total(nums[0], nums[1], nums[2], nums[3]); return true;
                default: return false;
            }
        }

        // "2d 4h", "1h 30m", "3m 20s", "45s"
        public static string Humanize(double secondsValue)
        {
            long total = (long)Math.Max(0, Math.Round(secondsValue));
            long d = total / 86400, h = (total % 86400) / 3600, m = (total % 3600) / 60, s = total % 60;
            if (d > 0) return h > 0 ? d + "d " + h + "h" : d + "d";
            if (h > 0) return m > 0 ? h + "h " + m + "m" : h + "h";
            if (m > 0) return s > 0 ? m + "m " + s + "s" : m + "m";
            return s + "s";
        }
    }

    internal static class Store
    {
        public static Dictionary<string, object> Load(CphBridge cph, string storageKey, Action<string> warn)
        {
            try { return Json.ParseObject(cph.GetGlobalString(storageKey)); }
            catch (Exception ex)
            {
                if (warn != null) warn("Stored settings in '" + storageKey + "' could not be read and were ignored: " + ex.Message);
                return new Dictionary<string, object>(StringComparer.Ordinal);
            }
        }

        public static void Write(CphBridge cph, string storageKey, Dictionary<string, object> data)
        {
            cph.SetGlobal(storageKey, Json.Serialize(data, false));
        }
    }

    // Window position/size/zoom/tab, stored separately from the settings in "orchid_{name}.window".
    internal sealed class UiState
    {
        public double X, Y, W, H, Zoom;

        // Scale of the screen the window was on. Picks the right screen when DIP rectangles
        // of a mixed-DPI setup overlap.
        public double Scale;
        public bool HasPosition, Maximized;
        public string Tab;

        public Dictionary<string, object> ToDictionary()
        {
            var d = new Dictionary<string, object>
            {
                { "w", W }, { "h", H }, { "max", Maximized }, { "zoom", Zoom }, { "tab", Tab ?? "" },
            };
            if (HasPosition) { d["x"] = X; d["y"] = Y; }
            if (HasPosition && Scale > 0) d["scale"] = Scale;
            return d;
        }

        public static UiState From(Dictionary<string, object> d)
        {
            if (d == null || d.Count == 0) return null;
            var s = new UiState
            {
                W = Num(d, "w"),
                H = Num(d, "h"),
                Zoom = Num(d, "zoom"),
                Maximized = ValueConverter.To(Get(d, "max"), false),
                Tab = ValueConverter.To(Get(d, "tab"), ""),
            };
            if (d.ContainsKey("x") && d.ContainsKey("y"))
            {
                s.X = Num(d, "x");
                s.Y = Num(d, "y");
                s.HasPosition = !double.IsNaN(s.X) && !double.IsNaN(s.Y);
                s.Scale = Num(d, "scale");
            }
            return s;
        }

        private static object Get(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) ? v : null; }
        private static double Num(Dictionary<string, object> d, string k) { return ValueConverter.To(Get(d, k), 0.0); }
    }

    // Read (and optionally write) an extension's saved settings from your runtime actions.
    public sealed class OrchidSettings
    {
        private readonly Dictionary<string, object> _data;
        private readonly CphBridge _cph;

        internal OrchidSettings(string extensionName, Dictionary<string, object> data, CphBridge cph)
        {
            ExtensionName = extensionName;
            _data = data ?? new Dictionary<string, object>(StringComparer.Ordinal);
            _cph = cph;
        }

        public string ExtensionName { get; private set; }

        public static OrchidSettings Load(object cph, string extensionName)
        {
            var bridge = new CphBridge(cph);
            var data = Store.Load(bridge, Orchid.StorageKeyFor(extensionName), null);
            return new OrchidSettings(extensionName, data, bridge);
        }

        public IEnumerable<string> Keys { get { return _data.Keys.ToArray(); } }

        public bool Has(string key) { return key != null && _data.ContainsKey(key); }

        public object GetRaw(string key)
        {
            object v;
            return key != null && _data.TryGetValue(key, out v) ? v : null;
        }

        public T Get<T>(string key, T fallback = default(T)) { return ValueConverter.To(GetRaw(key), fallback); }

        public string GetString(string key, string fallback = "") { return Get(key, fallback); }
        public bool GetBool(string key, bool fallback = false) { return Get(key, fallback); }
        public int GetInt(string key, int fallback = 0) { return Get(key, fallback); }
        public double GetDouble(string key, double fallback = 0) { return Get(key, fallback); }
        public string[] GetList(string key) { return Get(key, new string[0]); }
        public string[][] GetRows(string key) { return Get(key, new string[0][]); }
        public TimeSpan GetDuration(string key, TimeSpan fallback = default(TimeSpan)) { return Get(key, fallback); }

        // For AddSliderWithToggleSwitch: reads "{key}_enabled".
        public bool IsEnabled(string key, bool fallback = false) { return GetBool(key + "_enabled", fallback); }

        public void Set(string key, object value)
        {
            if (key == null) return;
            _data[key] = ValueConverter.Clone(value);
            _changed.Add(key);
        }

        // Re-reads the store and writes only the keys changed with Set(), so edits made elsewhere survive.
        public void Save()
        {
            if (_cph == null) throw new InvalidOperationException("This settings object was not loaded with CPH and cannot be saved.");
            if (_changed.Count == 0) return;
            var key = Orchid.StorageKeyFor(ExtensionName);
            var latest = Store.Load(_cph, key, null);
            foreach (var k in _changed) latest[k] = _data[k];
            Store.Write(_cph, key, latest);
            _changed.Clear();
        }

        private readonly HashSet<string> _changed = new HashSet<string>(StringComparer.Ordinal);

        public string ToJson(bool indent = true) { return Json.Serialize(_data, indent); }
    }
}
