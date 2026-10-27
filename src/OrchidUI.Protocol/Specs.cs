#if ORCHID_HOST
#nullable disable
#endif
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OrchidUI.Protocol
{
    // What the host draws. The shim builds all of this; the host only reads it and never keeps
    // state of its own. Every type maps to and from the plain JSON objects Json produces.

    internal static class FieldKinds
    {
        public const string Title = "title";
        public const string Description = "description";
        public const string Separator = "separator";
        public const string Info = "info";
        public const string Custom = "custom";
        public const string Text = "text";
        public const string Copy = "copy";
        public const string Toggle = "toggle";
        public const string Segmented = "segmented";
        public const string Slider = "slider";
        public const string Stepper = "stepper";
        public const string Dropdown = "dropdown";
        public const string Color = "color";
        public const string Duration = "duration";
        public const string Button = "button";
        public const string List = "list";
        public const string MultiSelect = "multiselect";
        public const string Path = "path";
        public const string Image = "image";
        public const string Unsupported = "unsupported";
    }

    internal sealed class IconSpec
    {
        public string Path;
        public double Grid = 24;
        public double Stroke = 2;
        public bool Filled;
        public string Text;

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            if (Text != null) { d["text"] = Text; return d; }
            d["path"] = Path;
            d["grid"] = Grid;
            d["stroke"] = Stroke;
            d["filled"] = Filled;
            return d;
        }

        public static IconSpec FromJson(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var text = Json.Str(d, "text");
            if (text != null) return new IconSpec { Text = text };
            var path = Json.Str(d, "path");
            if (path == null) return null;
            return new IconSpec { Path = path, Grid = Json.Num(d, "grid", 24), Stroke = Json.Num(d, "stroke", 2), Filled = Json.Bool(d, "filled") };
        }
    }

    // An image the shim read from disk (bytes, never a path), or an http(s) URL the host may fetch.
    internal sealed class ImageData
    {
        public string Mime;
        public string Base64;
        public string Url;

        public Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal) { { "mime", Mime }, { "data", Base64 }, { "url", Url } };
        }

        public static ImageData FromJson(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var data = Json.Str(d, "data");
            var url = Json.Str(d, "url");
            if (data == null && !IsWebUrl(url)) return null;
            return new ImageData { Mime = Json.Str(d, "mime", "application/octet-stream"), Base64 = data, Url = data == null ? url : null };
        }

        public static bool IsWebUrl(string url)
        {
            Uri uri;
            return url != null && Uri.TryCreate(url, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }
    }

    internal sealed class TabSpec
    {
        public string Id;
        public string Name;
        public IconSpec Icon;
        public int Order;

        public Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "id", Id }, { "name", Name }, { "icon", Icon == null ? null : Icon.ToJson() }, { "order", Order },
            };
        }

        public static TabSpec FromJson(Dictionary<string, object> d)
        {
            return new TabSpec { Id = Json.Str(d, "id", ""), Name = Json.Str(d, "name", ""), Icon = IconSpec.FromJson(Json.Obj(d, "icon")), Order = Json.Int(d, "order") };
        }
    }

    internal sealed class FieldSpec
    {
        public string Id;
        public string Kind;
        public string Key;
        public List<string> Keys = new List<string>();
        public string Label;
        public string Description;
        public string Tab;
        public IconSpec Icon;
        public string Layout = "inline";
        public string Tooltip;
        public string Placeholder;
        public double Width;
        public bool Collapsed;
        public bool HasFormat;
        public Dictionary<string, object> Props = new Dictionary<string, object>(StringComparer.Ordinal);

        public Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "id", Id },
                { "kind", Kind },
                { "key", Key },
                { "keys", Keys.ToArray() },
                { "label", Label },
                { "description", Description },
                { "tab", Tab },
                { "icon", Icon == null ? null : Icon.ToJson() },
                { "layout", Layout },
                { "tooltip", Tooltip },
                { "placeholder", Placeholder },
                { "width", Width },
                { "collapsed", Collapsed },
                { "hasFormat", HasFormat },
                { "props", Props },
            };
        }

        public static FieldSpec FromJson(Dictionary<string, object> d)
        {
            var keys = Json.Arr(d, "keys");
            return new FieldSpec
            {
                Id = Json.Str(d, "id", ""),
                Kind = Json.Str(d, "kind", FieldKinds.Unsupported),
                Key = Json.Str(d, "key"),
                Keys = keys == null ? new List<string>() : keys.OfType<string>().ToList(),
                Label = Json.Str(d, "label"),
                Description = Json.Str(d, "description"),
                Tab = Json.Str(d, "tab", ""),
                Icon = IconSpec.FromJson(Json.Obj(d, "icon")),
                Layout = Json.Str(d, "layout", "inline"),
                Tooltip = Json.Str(d, "tooltip"),
                Placeholder = Json.Str(d, "placeholder"),
                Width = Json.Num(d, "width"),
                Collapsed = Json.Bool(d, "collapsed"),
                HasFormat = Json.Bool(d, "hasFormat"),
                Props = Json.Obj(d, "props") ?? new Dictionary<string, object>(StringComparer.Ordinal),
            };
        }

        public string PropStr(string name, string fallback = null) { return Json.Str(Props, name, fallback); }
        public double PropNum(string name, double fallback = 0) { return Json.Num(Props, name, fallback); }
        public bool PropBool(string name, bool fallback = false) { return Json.Bool(Props, name, fallback); }

        public string[] PropStrings(string name)
        {
            var a = Json.Arr(Props, name);
            return a == null ? new string[0] : a.Select(x => x as string ?? "").ToArray();
        }
    }

    // The result of the callbacks that run in Streamer.bot, sent after every change.
    internal sealed class FieldState
    {
        public string Id;
        public bool Visible = true;
        public bool Enabled = true;
        public string Error;
        public string Display;
        public bool Dirty;
        public string Summary;
        public string Text;

        // An image field's preview (bytes the shim read, or an http(s) URL), and what it was made
        // from (compared, not sent).
        public ImageData Image;
        public string ImageSource;

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "id", Id }, { "visible", Visible }, { "enabled", Enabled }, { "error", Error },
                { "display", Display }, { "dirty", Dirty }, { "summary", Summary }, { "text", Text },
            };
            if (ImageSource != null) d["image"] = Image == null ? null : Image.ToJson();
            return d;
        }

        public static FieldState FromJson(Dictionary<string, object> d)
        {
            return new FieldState
            {
                Id = Json.Str(d, "id", ""),
                Visible = Json.Bool(d, "visible", true),
                Enabled = Json.Bool(d, "enabled", true),
                Error = Json.Str(d, "error"),
                Display = Json.Str(d, "display"),
                Dirty = Json.Bool(d, "dirty"),
                Summary = Json.Str(d, "summary"),
                Text = Json.Str(d, "text"),
                Image = ImageData.FromJson(Json.Obj(d, "image")),
                ImageSource = d.ContainsKey("image") ? "" : null,
            };
        }

        public bool SameAs(FieldState o)
        {
            return o != null && o.Id == Id && o.Visible == Visible && o.Enabled == Enabled && o.Error == Error
                && o.Display == Display && o.Dirty == Dirty && o.Summary == Summary && o.Text == Text && o.ImageSource == ImageSource;
        }
    }

    internal sealed class StatusSpec
    {
        public bool Dirty;
        public int Errors;
        public string Text = "";
        public string Tone = "muted";
        public bool CanSave = true;
        public bool CanDiscard;

        public Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "dirty", Dirty }, { "errors", Errors }, { "text", Text }, { "tone", Tone }, { "canSave", CanSave }, { "canDiscard", CanDiscard },
            };
        }

        public static StatusSpec FromJson(Dictionary<string, object> d)
        {
            if (d == null) return null;
            return new StatusSpec
            {
                Dirty = Json.Bool(d, "dirty"),
                Errors = Json.Int(d, "errors"),
                Text = Json.Str(d, "text", ""),
                Tone = Json.Str(d, "tone", "muted"),
                CanSave = Json.Bool(d, "canSave", true),
                CanDiscard = Json.Bool(d, "canDiscard"),
            };
        }
    }

    internal sealed class GradientData
    {
        public string Key;
        public double X1, Y1, X2, Y2;
        public string[] Colors = new string[0];
        public double[] Offsets = new double[0];

        public Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "key", Key }, { "x1", X1 }, { "y1", Y1 }, { "x2", X2 }, { "y2", Y2 },
                { "colors", Colors }, { "offsets", Offsets.Cast<object>().ToArray() },
            };
        }

        public static GradientData FromJson(Dictionary<string, object> d)
        {
            var colors = Json.Arr(d, "colors") ?? new object[0];
            var offsets = Json.Arr(d, "offsets") ?? new object[0];
            return new GradientData
            {
                Key = Json.Str(d, "key", ""),
                X1 = Json.Num(d, "x1"), Y1 = Json.Num(d, "y1"), X2 = Json.Num(d, "x2"), Y2 = Json.Num(d, "y2"),
                Colors = colors.Select(c => c as string ?? "#00000000").ToArray(),
                Offsets = offsets.Select(o => o == null || o is string || o is bool ? 0 : Convert.ToDouble(o, CultureInfo.InvariantCulture)).ToArray(),
            };
        }
    }

    // ThemeResolver's output: the host never parses a theme file.
    internal sealed class ThemeSpec
    {
        public bool IsDark = true;
        public Dictionary<string, string> Colors = new Dictionary<string, string>(StringComparer.Ordinal);
        public List<GradientData> Gradients = new List<GradientData>();
        public string FontFamily;
        public string HeadingFontFamily;
        public string MonoFontFamily;
        public string FontFileName;
        public string FontFileData;
        public double FontSize = 14;
        public double SmallFontSize = 12;
        public double PageTitleSize = 28;
        public double SectionTitleSize = 14;
        public double HeroTitleSize = 30;
        public string HeadingWeight = "SemiBold";
        public double CornerRadius = 6;
        public double LargeCornerRadius = 10;
        public double BorderThickness = 1;
        public bool Shadows = true;
        public string Name;

        public Dictionary<string, object> ToJson()
        {
            var colors = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var kv in Colors.OrderBy(k => k.Key, StringComparer.Ordinal)) colors[kv.Key] = kv.Value;
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "name", Name },
                { "isDark", IsDark },
                { "colors", colors },
                { "gradients", Gradients.Select(g => (object)g.ToJson()).ToArray() },
                { "fonts", new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        { "body", FontFamily }, { "heading", HeadingFontFamily }, { "mono", MonoFontFamily },
                        { "fileName", FontFileName }, { "fileData", FontFileData },
                    } },
                { "sizes", new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        { "font", FontSize }, { "small", SmallFontSize }, { "pageTitle", PageTitleSize },
                        { "sectionTitle", SectionTitleSize }, { "heroTitle", HeroTitleSize },
                    } },
                { "headingWeight", HeadingWeight },
                { "radii", new Dictionary<string, object>(StringComparer.Ordinal) { { "corner", CornerRadius }, { "large", LargeCornerRadius } } },
                { "border", BorderThickness },
                { "shadows", Shadows },
            };
        }

        public static ThemeSpec FromJson(Dictionary<string, object> d)
        {
            var t = new ThemeSpec();
            if (d == null) return t;
            t.Name = Json.Str(d, "name");
            t.IsDark = Json.Bool(d, "isDark", true);
            var colors = Json.Obj(d, "colors");
            if (colors != null)
            {
                foreach (var kv in colors)
                {
                    var s = kv.Value as string;
                    if (s != null) t.Colors[kv.Key] = s;
                }
            }
            var gradients = Json.Arr(d, "gradients");
            if (gradients != null) t.Gradients = gradients.OfType<Dictionary<string, object>>().Select(GradientData.FromJson).ToList();
            var fonts = Json.Obj(d, "fonts");
            t.FontFamily = Json.Str(fonts, "body");
            t.HeadingFontFamily = Json.Str(fonts, "heading");
            t.MonoFontFamily = Json.Str(fonts, "mono");
            t.FontFileName = Json.Str(fonts, "fileName");
            t.FontFileData = Json.Str(fonts, "fileData");
            var sizes = Json.Obj(d, "sizes");
            t.FontSize = Json.Num(sizes, "font", 14);
            t.SmallFontSize = Json.Num(sizes, "small", 12);
            t.PageTitleSize = Json.Num(sizes, "pageTitle", 28);
            t.SectionTitleSize = Json.Num(sizes, "sectionTitle", 14);
            t.HeroTitleSize = Json.Num(sizes, "heroTitle", 30);
            t.HeadingWeight = Json.Str(d, "headingWeight", "SemiBold");
            var radii = Json.Obj(d, "radii");
            t.CornerRadius = Json.Num(radii, "corner", 6);
            t.LargeCornerRadius = Json.Num(radii, "large", 10);
            t.BorderThickness = Json.Num(d, "border", 1);
            t.Shadows = Json.Bool(d, "shadows", true);
            return t;
        }
    }

    internal sealed class WindowSpec
    {
        public int Protocol = ProtocolInfo.Major;
        public string Extension;
        public string Version;
        public string Title;
        public string Subtitle;
        public string HeaderTitle;
        public string HeaderSubtitle;
        public ImageData HeaderImage;
        public double HeaderHeight = 120;
        public string Footer;
        public List<KeyValuePair<string, string>> Links = new List<KeyValuePair<string, string>>();
        public List<TabSpec> Tabs = new List<TabSpec>();
        public List<FieldSpec> Fields = new List<FieldSpec>();
        public ThemeSpec Theme = new ThemeSpec();
        public Dictionary<string, object> Window = new Dictionary<string, object>(StringComparer.Ordinal);
        public Dictionary<string, object> Layout = new Dictionary<string, object>(StringComparer.Ordinal);
        public Dictionary<string, string> Strings = new Dictionary<string, string>(StringComparer.Ordinal);
        public string StartTab;
        public double Zoom = 1;
        public Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.Ordinal);
        public List<FieldState> States = new List<FieldState>();
        public StatusSpec Status = new StatusSpec();

        public Dictionary<string, object> ToJson()
        {
            var strings = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var kv in Strings) strings[kv.Key] = kv.Value;
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "protocol", Protocol },
                { "extension", Extension },
                { "version", Version },
                { "title", Title },
                { "subtitle", Subtitle },
                { "header", new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        { "title", HeaderTitle }, { "subtitle", HeaderSubtitle },
                        { "image", HeaderImage == null ? null : HeaderImage.ToJson() }, { "height", HeaderHeight },
                    } },
                { "footer", Footer },
                { "links", Links.Select(l => (object)new Dictionary<string, object>(StringComparer.Ordinal) { { "text", l.Key }, { "url", l.Value } }).ToArray() },
                { "tabs", Tabs.Select(t => (object)t.ToJson()).ToArray() },
                { "fields", Fields.Select(f => (object)f.ToJson()).ToArray() },
                { "theme", Theme.ToJson() },
                { "window", Window },
                { "layout", Layout },
                { "strings", strings },
                { "startTab", StartTab },
                { "zoom", Zoom },
                { "values", Values },
                { "states", States.Select(s => (object)s.ToJson()).ToArray() },
                { "status", Status.ToJson() },
            };
        }

        public static WindowSpec FromJson(Dictionary<string, object> d)
        {
            if (d == null) throw new FormatException("No window spec.");
            int protocol = Json.Int(d, "protocol", -1);
            if (protocol != ProtocolInfo.Major) throw new FormatException("Window spec protocol " + protocol.ToString(CultureInfo.InvariantCulture) + " isn't supported.");
            var header = Json.Obj(d, "header");
            var spec = new WindowSpec
            {
                Protocol = protocol,
                Extension = Json.Str(d, "extension", ""),
                Version = Json.Str(d, "version"),
                Title = Json.Str(d, "title", ""),
                Subtitle = Json.Str(d, "subtitle"),
                HeaderTitle = Json.Str(header, "title"),
                HeaderSubtitle = Json.Str(header, "subtitle"),
                HeaderImage = ImageData.FromJson(Json.Obj(header, "image")),
                HeaderHeight = Json.Num(header, "height", 120),
                Footer = Json.Str(d, "footer"),
                Theme = ThemeSpec.FromJson(Json.Obj(d, "theme")),
                Window = Json.Obj(d, "window") ?? new Dictionary<string, object>(StringComparer.Ordinal),
                Layout = Json.Obj(d, "layout") ?? new Dictionary<string, object>(StringComparer.Ordinal),
                StartTab = Json.Str(d, "startTab"),
                Zoom = Json.Num(d, "zoom", 1),
                Values = Json.Obj(d, "values") ?? new Dictionary<string, object>(StringComparer.Ordinal),
                Status = StatusSpec.FromJson(Json.Obj(d, "status")) ?? new StatusSpec(),
            };
            foreach (var l in (Json.Arr(d, "links") ?? new object[0]).OfType<Dictionary<string, object>>())
                spec.Links.Add(new KeyValuePair<string, string>(Json.Str(l, "text", ""), Json.Str(l, "url", "")));
            spec.Tabs = (Json.Arr(d, "tabs") ?? new object[0]).OfType<Dictionary<string, object>>().Select(TabSpec.FromJson).ToList();
            spec.Fields = (Json.Arr(d, "fields") ?? new object[0]).OfType<Dictionary<string, object>>().Select(FieldSpec.FromJson).ToList();
            spec.States = (Json.Arr(d, "states") ?? new object[0]).OfType<Dictionary<string, object>>().Select(FieldState.FromJson).ToList();
            var strings = Json.Obj(d, "strings");
            if (strings != null)
            {
                foreach (var kv in strings)
                {
                    var s = kv.Value as string;
                    if (s != null) spec.Strings[kv.Key] = s;
                }
            }
            return spec;
        }

        public string String(string key, string fallback)
        {
            string s;
            return Strings.TryGetValue(key, out s) && s != null ? s : fallback;
        }
    }
}
