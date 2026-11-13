using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Treesi.Desktop;
using Treesi.TestSupport;

namespace Treesi.RenderingLab;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args is ["drag-source", var file])
        {
            IsolatedExecution.Require("native-ui");
            if (!Path.IsPathFullyQualified(file)
                || !Path.GetFullPath(file).StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(file))
                throw new ArgumentException("The drag source must be an existing temporary fixture file.");
            AppBuilder.Configure<DragSourceApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime([file]);
            return;
        }
        IsolatedExecution.Require("native-performance");
        if (args is not ["angle" or "software", _, _]) throw new ArgumentException("A renderer, diagnostic path and fixture path are required.");
        var directory = Environment.GetEnvironmentVariable("TREESI_NATIVE_RESULTS");
        if (directory is null || !Path.IsPathFullyQualified(directory) || !Path.IsPathFullyQualified(args[1])
            || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(args[1])), Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The diagnostic must be a direct child of the configured isolated result directory.");
        var requested = args[0];
        AppBuilder.Configure<App>().UsePlatformDetect()
            .With(new Win32PlatformOptions { RenderingMode = [requested == "software" ? Win32RenderingMode.Software : Win32RenderingMode.AngleEgl] })
            .AfterSetup(_ => WriteDiagnostic(requested, args[1]))
            .StartWithClassicDesktopLifetime([args[2]]);
    }

    private static void WriteDiagnostic(string requested, string path)
    {
        // Read-only, version-pinned observation of Avalonia 12.1.6 implementation services.
        // This dependency is confined to the lab; product code does not use the locator.
        var assembly = typeof(AvaloniaObject).Assembly;
        var locator = assembly.GetType("Avalonia.AvaloniaLocator", throwOnError: true)!;
        var resolver = locator.GetProperty("Current", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
        var getService = resolver.GetType().GetMethod("GetService", [typeof(Type)])!;
        string? ServiceType(string name) => getService.Invoke(resolver, [assembly.GetType(name, throwOnError: true)!])?.GetType().FullName;
        var graphics = ServiceType("Avalonia.Platform.IPlatformGraphics");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schemaVersion = "1",
            sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
            ownedPid = Environment.ProcessId,
            createdUtcTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,
            requested,
            platformGraphicsType = graphics,
            renderInterfaceType = ServiceType("Avalonia.Platform.IPlatformRenderInterface"),
            avaloniaVersion = assembly.GetName().Version?.ToString(),
            scope = "Explicit lab startup using the product App. Observed initialized services, not GPU utilization, native driver identity or presentation timing."
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }
}
