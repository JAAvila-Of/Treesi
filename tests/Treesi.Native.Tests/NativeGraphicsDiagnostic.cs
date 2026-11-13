using System.Reflection;
using System.Text.Json;
using Treesi.TestSupport;

namespace Treesi.Native.Tests;

internal static class NativeGraphicsDiagnostic
{
    public static void Write(string suite)
    {
        IsolatedExecution.Require(suite);
        var executable = Environment.GetEnvironmentVariable("TREESI_NATIVE_APP");
        var directory = Environment.GetEnvironmentVariable("TREESI_NATIVE_RESULTS");
        if (executable is null || !Path.IsPathFullyQualified(executable) || !File.Exists(executable)
            || directory is null || !Path.IsPathFullyQualified(directory))
            throw new InvalidOperationException("The isolated application and diagnostic paths must be configured.");
        // Load the published product's probe without starting its application or windowing subsystem.
        var assembly = Assembly.LoadFrom(Path.ChangeExtension(executable, ".dll"));
        var probe = assembly.GetType("Treesi.Desktop.Appearance.WindowsGraphicsProbe", throwOnError: true)!;
        var method = probe.GetMethod("HasHardwareAdapter", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException("The published graphics probe is unavailable.");
        var hardware = (bool?)method.Invoke(null, null);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "windows-graphics-probe.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = "1",
            sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
            hasUsableHardwareAdapter = hardware,
            expectedStartupPolicy = hardware == false ? "direct-software" : "framework-defaults",
            scope = "Published product probe in the isolated test host outside resource measurement windows. This is not the application's actual selected renderer or presentation evidence."
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }
}
