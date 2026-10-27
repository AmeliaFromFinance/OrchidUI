using Avalonia;
using Avalonia.Headless;
using OrchidUI.Host.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace OrchidUI.Host.Tests;

public sealed class TestApp
{
    // Real Skia rendering (not the headless stub), so snapshots are actual pixels.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
