using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace OrchidUI
{
    // Sets public properties/fields from "name": value pairs (case-insensitive). Returns unknown/invalid keys.
    internal static class ConfigApplier
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static List<string> Apply(object target, IDictionary<string, object> values)
        {
            var bad = new List<string>();
            if (target == null || values == null) return bad;
            var type = target.GetType();
            foreach (var kv in values)
            {
                if (kv.Key.StartsWith("$") || kv.Key.Equals("preset", StringComparison.OrdinalIgnoreCase)) continue;
                var prop = type.GetProperty(kv.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                var field = prop == null ? type.GetField(kv.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) : null;
                Type t = prop != null && prop.CanWrite ? prop.PropertyType : field != null ? field.FieldType : null;
                if (t == null) { bad.Add(kv.Key); continue; }
                object converted;
                if (!TryConvert(kv.Value, t, out converted)) { bad.Add(kv.Key); continue; }
                if (prop != null) prop.SetValue(target, converted, null);
                else field.SetValue(target, converted);
            }
            return bad;
        }

        private static bool TryConvert(object value, Type t, out object result)
        {
            result = null;
            var under = Nullable.GetUnderlyingType(t);
            if (value == null) return under != null || !t.IsValueType;
            if (under != null) t = under;
            try
            {
                if (t == typeof(string)) { result = Convert.ToString(value, Inv); return true; }
                if (t.IsEnum) { result = Enum.Parse(t, Convert.ToString(value, Inv), true); return true; }
                if (t == typeof(bool))
                {
                    var s = value as string;
                    if (s != null)
                    {
                        s = s.Trim().ToLowerInvariant();
                        result = s == "true" || s == "1" || s == "yes" || s == "on";
                        return true;
                    }
                    result = Convert.ToBoolean(value, Inv);
                    return true;
                }
                if (t == typeof(double) || t == typeof(int))
                {
                    double d = value is string ? double.Parse(((string)value).Trim(), NumberStyles.Float, Inv) : Convert.ToDouble(value, Inv);
                    if (t == typeof(int)) result = (int)Math.Round(d); else result = d;
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}
