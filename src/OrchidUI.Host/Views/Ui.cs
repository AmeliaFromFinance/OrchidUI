using System.Diagnostics;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using OrchidUI.Host.Theming;
using OrchidUI.Protocol;
using Path = Avalonia.Controls.Shapes.Path;

namespace OrchidUI.Host.Views;

internal static partial class Ui
{
    // URLs, %variables%, **bold** and `code`.
    [GeneratedRegex(@"(https?://[^\s<>""']+)|(%[A-Za-z0-9_.]+%)|(\*\*[^*]+\*\*)|(`[^`]+`)")]
    private static partial Regex RichPattern();

    public static TextBlock RichText(string? text, Palette p, IBrush foreground, double fontSize, IBrush? codeForeground = null, IBrush? codeBackground = null)
    {
        var tb = new RichTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = foreground,
            FontSize = fontSize,
            LineHeight = Math.Round(fontSize * 1.42),
        };
        var inlines = tb.Inlines!;
        text ??= "";
        int last = 0;
        foreach (Match m in RichPattern().Matches(text))
        {
            if (m.Index > last) inlines.Add(new Run(text[last..m.Index]));
            if (m.Groups[1].Success)
            {
                string url = m.Value;
                string trail = "";
                while (url.Length > 0 && ".,;:!?)]".Contains(url[^1]))
                {
                    trail = url[^1] + trail;
                    url = url[..^1];
                }
                inlines.Add(new InlineUIContainer(LinkButton(url, p, fontSize)) { BaselineAlignment = BaselineAlignment.TextBottom });
                if (trail.Length > 0) inlines.Add(new Run(trail));
            }
            else if (m.Groups[2].Success || m.Groups[4].Success)
            {
                string v = m.Groups[4].Success ? m.Value[1..^1] : m.Value;
                inlines.Add(new Run(" " + v + " ")
                {
                    Foreground = codeForeground ?? p["Code"],
                    FontFamily = p.MonoFont,
                    FontSize = Math.Max(9, p.SmallFontSize),
                    Background = codeBackground ?? p["CodeBackground"],
                });
            }
            else
            {
                inlines.Add(new Run(m.Value[2..^2]) { FontWeight = FontWeight.SemiBold, Foreground = p["Text"] });
            }
            last = m.Index + m.Length;
        }
        if (last < text.Length) inlines.Add(new Run(text[last..]));
        return tb;
    }

    private static Control LinkButton(string url, Palette p, double fontSize)
    {
        var b = new Button
        {
            Content = url,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            MinHeight = 0,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = p["Link"],
            FontSize = fontSize,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        b.Classes.Add("link");
        ToolTip.SetTip(b, url);
        AutomationProperties.SetName(b, url);
        b.Click += (_, _) => OpenUrl(url);
        return b;
    }

    public static Func<string, bool> UrlOpener = DefaultOpen;

    // Only http and https leave the window; opened with the OS's own handler.
    public static bool OpenUrl(string? url)
    {
        if (!IsWebUrl(url)) return false;
        return UrlOpener(url!);
    }

    public static bool IsWebUrl(string? url) =>
        url is not null && Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    private static bool DefaultOpen(string url)
    {
        try
        {
            ProcessStartInfo psi;
            if (OperatingSystem.IsWindows()) psi = new ProcessStartInfo(url) { UseShellExecute = true };
            else if (OperatingSystem.IsMacOS()) psi = new ProcessStartInfo("open", new[] { url }) { UseShellExecute = false };
            else psi = new ProcessStartInfo("xdg-open", new[] { url }) { UseShellExecute = false };
            Process.Start(psi)?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            HostLog.Warn("Couldn't open " + url + ": " + ex.Message);
            return false;
        }
    }

    // An icon from the shim: path data on a grid, or a short text (emoji).
    public static Control? Icon(IconSpec? spec, double size, IBrush? brush = null)
    {
        if (spec is null) return null;
        if (spec.Text is not null)
        {
            var tb = new TextBlock
            {
                Text = spec.Text,
                FontSize = Math.Round(size * 0.9),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false,
            };
            if (brush is not null) tb.Foreground = brush;
            return new Panel { Width = size, Height = size, Children = { tb }, IsHitTestVisible = false };
        }
        Geometry geometry;
        try { geometry = Geometry.Parse(spec.Path ?? ""); }
        catch (Exception) { return null; }
        var path = new Path { Data = geometry };
        if (spec.Filled)
        {
            if (brush is not null) path.Fill = brush;
            else path.Bind(Shape.FillProperty, path.GetObservable(TextElement.ForegroundProperty));
        }
        else
        {
            path.StrokeThickness = spec.Stroke;
            path.StrokeLineCap = PenLineCap.Round;
            path.StrokeJoin = PenLineJoin.Round;
            if (brush is not null) path.Stroke = brush;
            else path.Bind(Shape.StrokeProperty, path.GetObservable(TextElement.ForegroundProperty));
        }
        var canvas = new Canvas { Width = spec.Grid, Height = spec.Grid, Children = { path } };
        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = canvas,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
        };
    }

    public static Control SeverityIcon(string kind, Palette p)
    {
        var (color, glyph) = kind switch
        {
            "success" => ("Success", "M5,8.4 L7.1,10.5 L11,6"),
            "danger" => ("Danger", "M5.6,5.6 L10.4,10.4 M10.4,5.6 L5.6,10.4"),
            "warning" => ("Warning", "M8,4.4 V8.9 M8,11.5 V11.6"),
            _ => ("Info", "M8,7.4 V11.6 M8,4.6 V4.7"),
        };
        var g = new Panel { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Ellipse { Fill = p[color] });
        g.Children.Add(Icon(new IconSpec { Path = glyph, Grid = 16, Stroke = 1.7 }, 16, p["OnStatus"])!);
        return g;
    }

    public static IBrush SeverityBackground(string kind, Palette p) => kind switch
    {
        "success" => p["SuccessBg"],
        "warning" => p["WarningBg"],
        "danger" => p["DangerBg"],
        _ => p["InfoBg"],
    };

    public static void Named(Control c, string? name, string? help = null)
    {
        if (!string.IsNullOrEmpty(name)) AutomationProperties.SetName(c, name);
        if (!string.IsNullOrEmpty(help)) AutomationProperties.SetHelpText(c, help);
    }
}

// Text with links in it: each link is a control inside the text. Avalonia draws a text that was
// hidden when it joined the window (by the search or ShowWhen) without measuring it first, so its
// layout is built mid-draw. With a link inside, that throws "Visual was invalidated during the
// render pass", so a hidden text builds its layout here instead.
internal sealed class RichTextBlock : TextBlock
{
    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Prepare();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        Prepare();
    }

    private void Prepare()
    {
        if (!IsVisible && VisualRoot is not null && Inlines is { Count: > 0 } inlines && inlines.OfType<InlineUIContainer>().Any())
            _ = TextLayout;
    }
}
