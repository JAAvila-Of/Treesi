using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Win32;
using Treesi.Application;
using Treesi.Git;
using Treesi.Desktop.Appearance;
using Treesi.Desktop.Workspaces;

namespace Treesi.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect();
        if (OperatingSystem.IsWindows() && WindowsGraphicsProbe.HasHardwareAdapter() == false)
            builder.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] });
        builder.StartWithClassicDesktopLifetime(args);
    }
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
        {
            var git = new GitInspector();
            desktop.MainWindow = new MainWindow(new InstrumentedInspectionService(new InspectionService(git)), desktop.Args?.FirstOrDefault(), _appearance!, new JsonMotionPreferenceStore(), new JsonWorkspaceHistoryStore(), new InstrumentedRevisionService(new RevisionService(git)), new InstrumentedHistoryService(new HistoryService(git)), new JsonWorkspaceCatalogStore(), new InstrumentedGraphService(new GraphService(git)), new InstrumentedWorktreeService(new WorktreeService(git)));
        }
        base.OnFrameworkInitializationCompleted();
    }
}
