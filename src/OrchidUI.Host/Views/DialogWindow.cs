using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using OrchidUI.Host.Theming;
using OrchidUI.Protocol;

namespace OrchidUI.Host.Views;

// ShowMessage / ShowConfirm / ShowChoice when no settings window is open: a small window holding
// only the dialog card.
internal sealed class DialogWindow : Window
{
    private readonly Palette P;
    private readonly WindowContext _ctx;
    private int _request = -1;
    private bool _allowClose;

    public DialogWindow(WindowSpec spec, ILinkSink sink)
    {
        P = new Palette(spec.Theme, "normal");
        _ctx = new WindowContext(spec, P, sink);
        P.ApplyTo(Resources, this);
        Title = spec.Extension;
        FontFamily = P.Font;
        FontSize = P.FontSize;
        Background = P["WindowBg"];
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        CanMaximize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new TextBlock { Text = "…", Margin = new Thickness(40), Foreground = P["TextMuted"] };
        Closing += (_, e) =>
        {
            if (_allowClose) return;
            if (_request >= 0) Answer(-1);
        };
    }

    public void Show(int request, string title, string message, string[] buttons, int primary)
    {
        _request = request;
        Title = title;
        var card = DialogCard.Build(P, _ctx, title, message, buttons, primary, Answer, out var primaryButton);
        card.BorderThickness = new Thickness(0);
        card.Margin = new Thickness(0);
        card.HorizontalAlignment = HorizontalAlignment.Stretch;
        Content = card;
        Activate();
        primaryButton?.Focus();
    }

    private void Answer(int index)
    {
        if (_request < 0) return;
        _ctx.Sink.Send(MessageTypes.DialogResult, new Dictionary<string, object>(StringComparer.Ordinal) { { "request", _request }, { "index", index } });
        _request = -1;
        Content = new TextBlock { Text = "…", Margin = new Thickness(40), Foreground = P["TextMuted"] };
    }

    public void CloseNow()
    {
        _allowClose = true;
        Close();
    }
}
