using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Treesi.Application;
using Treesi.Git;
using Treesi.Desktop.Appearance;

namespace Treesi.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}

public sealed class App : Avalonia.Application
{
    private ThemeController? _appearance;
    public override void Initialize()
    {
        DesktopStyling.Install(this);
        _appearance = new ThemeController(this, new JsonThemePreferenceStore());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(new InstrumentedInspectionService(new InspectionService(new GitInspector())), desktop.Args?.FirstOrDefault(), _appearance!);
        base.OnFrameworkInitializationCompleted();
    }
}
