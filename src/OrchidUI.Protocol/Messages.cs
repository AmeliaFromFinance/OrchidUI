#if ORCHID_HOST
#nullable disable
#endif
using System;
using System.Collections.Generic;

namespace OrchidUI.Protocol
{
    internal static class ProtocolInfo
    {
        // A host refuses any other major version; minor additions must stay optional.
        public const int Major = 1;

        // Session tokens are 32 random bytes as 64 hex characters.
        public const int TokenHexLength = 64;

        public const int HeartbeatSeconds = 5;

        // No frame for this long and the other side is treated as gone.
        public const int DeadAfterSeconds = 15;

        // A session file and its token are good for this long after the shim creates them.
        public const int TokenLifetimeSeconds = 30;
    }

    // Every frame type, by direction. Anything else is rejected before its payload is looked at.
    internal static class MessageTypes
    {
        // Host -> shim
        public const string Hello = "hello";
        public const string Ready = "ready";
        public const string Change = "change";
        public const string Click = "click";
        public const string Command = "command";
        public const string TabShown = "tab";
        public const string DialogResult = "dialogResult";
        public const string CloseRequest = "closeRequest";
        public const string Log = "log";

        // Shim -> host
        public const string Open = "open";
        public const string Patch = "patch";
        public const string Toast = "toast";
        public const string Dialog = "dialog";
        public const string DialogCancel = "dialogCancel";
        public const string Focus = "focus";
        public const string Jump = "jump";
        public const string Close = "close";
        public const string Refused = "refused";
        public const string Clipboard = "copy";

        // Both
        public const string Ping = "ping";

        public static readonly HashSet<string> ToShim = new HashSet<string>(StringComparer.Ordinal)
        {
            Hello, Ready, Change, Click, Command, TabShown, DialogResult, CloseRequest, Log, Ping,
        };

        public static readonly HashSet<string> ToHost = new HashSet<string>(StringComparer.Ordinal)
        {
            Open, Patch, Toast, Dialog, DialogCancel, Focus, Jump, Close, Refused, Clipboard, Ping,
        };
    }

    // Commands a host may send; the shim decides what each one does.
    internal static class Commands
    {
        public const string Save = "save";
        public const string SaveAndClose = "saveAndClose";
        public const string Discard = "discard";
        public const string Done = "done";
        public const string ResetField = "resetField";
        public const string Refresh = "refresh";
        public const string ResetAll = "resetAll";
        public const string Import = "import";
        public const string Export = "export";
        public const string About = "about";

        public static readonly HashSet<string> All = new HashSet<string>(StringComparer.Ordinal)
        {
            Save, SaveAndClose, Discard, Done, ResetField, Refresh, ResetAll, Import, Export, About,
        };
    }

    // The schema check every received frame passes before anything acts on it: required fields,
    // their JSON types, and size limits on strings that end up on screen or in logs.
    internal static class MessageSchema
    {
        public const int MaxIdLength = 32;
        public const int MaxTextLength = 64 * 1024;

        public static string Check(Frame f)
        {
            var p = f.Payload;
            switch (f.Type)
            {
                case MessageTypes.Hello:
                    return Need(p, "token", typeof(string)) ?? Optional(p, "host", typeof(Dictionary<string, object>));
                case MessageTypes.Ready:
                case MessageTypes.Ping:
                case MessageTypes.Focus:
                case MessageTypes.Close:
                    return null;
                case MessageTypes.Change:
                    return NeedId(p, "field") ?? Need(p, "key", typeof(string)) ?? (p.ContainsKey("value") ? null : "missing 'value'")
                        ?? Optional(p, "native", typeof(bool));
                case MessageTypes.Click:
                    return NeedId(p, "field");
                case MessageTypes.Command:
                {
                    var bad = Need(p, "name", typeof(string));
                    if (bad != null) return bad;
                    if (!Commands.All.Contains((string)p["name"])) return "unknown command";
                    return Optional(p, "field", typeof(string)) ?? Optional(p, "path", typeof(string)) ?? Optional(p, "native", typeof(bool));
                }
                case MessageTypes.TabShown:
                    return NeedId(p, "tab");
                case MessageTypes.DialogResult:
                    return Need(p, "request", typeof(int)) ?? Need(p, "index", typeof(int));
                case MessageTypes.CloseRequest:
                    return Optional(p, "state", typeof(Dictionary<string, object>));
                case MessageTypes.Log:
                    return Need(p, "level", typeof(string)) ?? Need(p, "message", typeof(string));
                case MessageTypes.Open:
                    return Need(p, "spec", typeof(Dictionary<string, object>));
                case MessageTypes.Patch:
                    return Optional(p, "values", typeof(Dictionary<string, object>)) ?? Optional(p, "states", typeof(object[]))
                        ?? Optional(p, "status", typeof(Dictionary<string, object>)) ?? Optional(p, "fields", typeof(object[]));
                case MessageTypes.Toast:
                    return Need(p, "message", typeof(string)) ?? Optional(p, "kind", typeof(string));
                case MessageTypes.Dialog:
                    return Need(p, "request", typeof(int)) ?? Need(p, "title", typeof(string)) ?? Need(p, "message", typeof(string)) ?? Need(p, "buttons", typeof(object[]));
                case MessageTypes.DialogCancel:
                    return Need(p, "request", typeof(int));
                case MessageTypes.Jump:
                    return NeedId(p, "field");
                case MessageTypes.Refused:
                    return Need(p, "reason", typeof(string));
                case MessageTypes.Clipboard:
                    return Need(p, "text", typeof(string));
                default:
                    return "unknown type";
            }
        }

        private static string Need(Dictionary<string, object> p, string key, Type type)
        {
            object v;
            if (!p.TryGetValue(key, out v) || v == null) return "missing '" + key + "'";
            if (!type.IsInstanceOfType(v)) return "'" + key + "' has the wrong type";
            var s = v as string;
            if (s != null && s.Length > MaxTextLength) return "'" + key + "' is too long";
            return null;
        }

        private static string Optional(Dictionary<string, object> p, string key, Type type)
        {
            object v;
            if (!p.TryGetValue(key, out v) || v == null) return null;
            return type.IsInstanceOfType(v) ? null : "'" + key + "' has the wrong type";
        }

        private static string NeedId(Dictionary<string, object> p, string key)
        {
            var bad = Need(p, key, typeof(string));
            if (bad != null) return bad;
            var s = (string)p[key];
            if (s.Length == 0 || s.Length > MaxIdLength) return "'" + key + "' isn't a valid id";
            foreach (var c in s)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))) return "'" + key + "' isn't a valid id";
            }
            return null;
        }
    }
}
