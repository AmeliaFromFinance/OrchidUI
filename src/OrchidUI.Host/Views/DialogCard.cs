using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OrchidUI.Host.Theming;

namespace OrchidUI.Host.Views;

// A message card with buttons; the last button is the highlighted one.
internal static class DialogCard
{
    public static Border Build(Palette p, WindowContext ctx, string title, string message, string[] buttons, int primary, Action<int> finish, out Button? primaryButton)
    {
        primaryButton = null;
        var stack = new StackPanel { Spacing = 0 };
        var heading = new TextBlock { Text = title, FontFamily = p.HeadingFont, FontWeight = p.HeadingWeight, FontSize = Math.Round(p.FontSize * 1.43), Foreground = p["Text"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        stack.Children.Add(heading);
        var body = Ui.RichText(message, p, p["TextSecondary"], p.FontSize);
        stack.Children.Add(body);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0), Spacing = 8 };
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            var b = new Button { Content = buttons[i], MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (i == primary)
            {
                b.Classes.Add("accent");
                b.IsDefault = true;
                primaryButton = b;
            }
            b.Click += (_, _) => finish(index);
            row.Children.Add(b);
        }
        stack.Children.Add(row);
        var card = new Border
        {
            Background = p["Popup"],
            BorderBrush = p["PopupBorder"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(p.LargeRadius),
            Padding = new Thickness(24, 22, 24, 20),
            MinWidth = 340,
            MaxWidth = 520,
            BoxShadow = p.Theme.Shadows ? BoxShadows.Parse("0 10 36 0 #80000000") : default,
            Child = stack,
        };
        Ui.Named(card, title, message);
        return card;
    }
}
