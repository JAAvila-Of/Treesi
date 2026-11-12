using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Treesi.Desktop.Appearance;

[assembly: AvaloniaTestApplication(typeof(Treesi.Desktop.Tests.HeadlessApplication))]

namespace Treesi.Desktop.Tests;

public sealed class HeadlessApplication : Avalonia.Application
{
    public override void Initialize() => DesktopStyling.Install(this);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<HeadlessApplication>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
