using System;
using System.Collections.Generic;
using System.Linq;
using OrchidUI.Protocol;

namespace OrchidUI
{
    internal enum CardLayout { Inline, Stacked, Expander }

    public sealed class Column
    {
        internal enum ColumnKind { Text, Number, Choice }

        internal ColumnKind Kind;
        internal string Header;
        internal double Weight;
        internal double Min, Max;
        internal string[] Options;
        internal string Placeholder;

        private Column() { }

        public static Column Text(string header, double weight = 2, string placeholder = null)
        {
            return new Column { Kind = ColumnKind.Text, Header = header, Weight = weight, Placeholder = placeholder };
        }

        public static Column Number(string header, double min = 0, double max = 1000000000, double weight = 1)
        {
            return new Column { Kind = ColumnKind.Number, Header = header, Min = Math.Min(min, max), Max = Math.Max(min, max), Weight = weight };
        }

        public static Column Choice(string header, string[] options, double weight = 1.5)
        {
            return new Column { Kind = ColumnKind.Choice, Header = header, Options = options ?? new string[0], Weight = weight };
        }
    }

    // Live (unsaved) values of the open window. Used by ShowWhen / EnableWhen / Validate.
    public sealed class OrchidValues
    {
        private readonly Orchid _owner;
        internal OrchidValues(Orchid owner) { _owner = owner; }

        public object Raw(string key) { return _owner.GetPendingRaw(key); }
        public T Get<T>(string key, T fallback = default(T)) { return ValueConverter.To(Raw(key), fallback); }
        public bool IsTrue(string key) { return Get(key, false); }
    }

    // Returned by every Add* method; chain modifiers onto it. The internals describe the field
    // to the host, which draws it.
    public class Field
    {
        internal Field() { }

        public string Key { get; internal set; }
        public string Label { get; internal set; }
        public string Description { get; internal set; }
        public string Tab { get; internal set; }

        internal Orchid Owner;
        internal object DefaultValue;
        internal Type ValueType = typeof(string);
        internal Func<OrchidValues, bool> VisibleWhenPredicate;
        internal Func<OrchidValues, bool> EnabledWhenPredicate;
        internal readonly List<Action<object>> ChangeHandlers = new List<Action<object>>();
        internal readonly List<Func<object, string>> Validators = new List<Func<object, string>>();
        internal string PlaceholderText;
        internal Func<double, string> Formatter;
        internal string TooltipText;
        internal string IconSpec;
        internal bool StartCollapsed;
        internal double? ControlWidth;
        internal bool? ForceStacked;

        // Set when the window spec is built. Fields added later aren't in the window.
        internal bool Built;
        internal string Id;
        internal string CurrentError;

        public Field ShowWhen(string key, object equals) { return ShowWhen(v => ValueConverter.LooseEquals(v.Raw(key), equals)); }
        public Field ShowWhen(Func<OrchidValues, bool> predicate) { VisibleWhenPredicate = predicate; return this; }

        public Field EnableWhen(string key, object equals) { return EnableWhen(v => ValueConverter.LooseEquals(v.Raw(key), equals)); }
        public Field EnableWhen(Func<OrchidValues, bool> predicate) { EnabledWhenPredicate = predicate; return this; }

        public Field OnChange(Action<object> handler)
        {
            if (handler != null) ChangeHandlers.Add(handler);
            return this;
        }

        // Return null when the value is fine, or an error message. Errors block saving.
        public Field Validate(Func<object, string> rule)
        {
            if (rule != null) Validators.Add(rule);
            return this;
        }

        public Field Validate(Func<object, bool> isValid, string message)
        {
            if (isValid == null) return this;
            return Validate(v => isValid(v) ? null : message);
        }

        public Field Required(string message = null)
        {
            return Validate(v =>
            {
                bool empty = v == null
                    || (v is string && string.IsNullOrWhiteSpace((string)v))
                    || (v is Array && ((Array)v).Length == 0);
                return empty ? (message ?? (Owner != null ? Owner.Strings.Required : "Required")) : null;
            });
        }

        public Field WithPlaceholder(string text) { PlaceholderText = text; return this; }
        public Field WithFormat(Func<double, string> formatter) { Formatter = formatter; return this; }
        public Field AsDuration() { return WithFormat(DurationUtil.Humanize); }
        public Field WithTooltip(string text) { TooltipText = text; return this; }
        public Field WithIcon(string icon) { IconSpec = icon; return this; }
        public Field Collapsed(bool collapsed = true) { StartCollapsed = collapsed; return this; }
        public Field WithWidth(double width) { ControlWidth = width > 0 ? width : (double?)null; return this; }

        // Puts the control under the label instead of on the right.
        public Field Stacked(bool stacked = true) { ForceStacked = stacked; return this; }

        internal virtual bool IsCard { get { return Label != null; } }
        internal virtual CardLayout NaturalLayout { get { return CardLayout.Inline; } }

        internal CardLayout Layout
        {
            get
            {
                var natural = NaturalLayout;
                if (natural == CardLayout.Expander || !ForceStacked.HasValue) return natural;
                return ForceStacked.Value ? CardLayout.Stacked : CardLayout.Inline;
            }
        }

        internal virtual IEnumerable<string> Keys
        {
            get { return Key == null ? Enumerable.Empty<string>() : new[] { Key }; }
        }

        internal virtual object DefaultFor(string key) { return DefaultValue; }
        internal virtual Type TypeFor(string key) { return ValueType; }

        // The stored form of a value: what Read(key) returns after Write(key, value). Must be
        // deterministic, or saved settings drift in format.
        internal virtual object Normalize(string key, object value) { return value; }

        internal virtual string Kind { get { return FieldKinds.Custom; } }

        // Per-kind settings for the host.
        internal virtual void Describe(FieldSpec spec) { }

        // Text the host shows next to a slider (WithFormat / AsDuration), or null.
        internal virtual string DisplayFor(object value) { return null; }

        // A short line for the card's header, e.g. "3 item(s)" on a list, or null.
        internal virtual string SummaryFor(object value, UiStrings strings) { return null; }

        internal string RunValidators(object value)
        {
            foreach (var v in Validators)
            {
                string error;
                try { error = v(value); }
                catch (Exception ex) { error = ex.Message; }
                if (!string.IsNullOrEmpty(error)) return error;
            }
            return null;
        }

        internal FieldSpec ToSpec(string tabId)
        {
            var spec = new FieldSpec
            {
                Id = Id,
                Kind = Kind,
                Key = Key,
                Keys = Keys.ToList(),
                Label = Label,
                Description = Description,
                Tab = tabId,
                Icon = IconResolver.ToSpec(IconSpec),
                Layout = Layout == CardLayout.Stacked ? "stacked" : Layout == CardLayout.Expander ? "expander" : "inline",
                Tooltip = TooltipText,
                Placeholder = PlaceholderText,
                Width = ControlWidth ?? 0,
                Collapsed = StartCollapsed,
                HasFormat = Formatter != null,
            };
            Describe(spec);
            return spec;
        }
    }
}
