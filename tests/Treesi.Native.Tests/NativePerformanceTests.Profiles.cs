using System.Diagnostics;

namespace Treesi.Native.Tests;

public sealed partial class NativePerformanceTests
{
    private sealed record ManagedProfile(string Focus, string TraceFile, long TraceBytes, int CollectionExitCode, int InclusiveReportExitCode, int ExclusiveReportExitCode);

    private static async Task<ManagedProfile> CaptureManagedProfileAsync(int ownedPid, string directory, string focus)
    {
        var executable = Environment.GetEnvironmentVariable("TREESI_DOTNET_TRACE");
        if (executable is null || !Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            throw new InvalidOperationException("The pinned managed sampler must be configured in the isolated VM.");
        Directory.CreateDirectory(directory);
        var trace = Path.Combine(directory, focus + ".nettrace");
        var collected = await RunProfileCommandAsync(executable, directory, focus + "-collect",
            ["collect", "--process-id", ownedPid.ToString(System.Globalization.CultureInfo.InvariantCulture), "--duration", "00:00:00:30",
             "--profile", "dotnet-sampled-thread-time", "--buffersize", "16", "--buffering-mode", "Drop", "--output", trace]);
        if (collected != 0 || !File.Exists(trace) || new FileInfo(trace).Length is <= 0 or > 64 * 1024 * 1024)
            throw new InvalidOperationException("Managed collection failed or exceeded its 64 MiB trace bound; inspect the retained logs.");
        var inclusive = await RunProfileCommandAsync(executable, directory, focus + "-inclusive", ["report", trace, "topN", "--number", "40", "--inclusive", "--verbose"]);
        var exclusive = await RunProfileCommandAsync(executable, directory, focus + "-exclusive", ["report", trace, "topN", "--number", "40", "--verbose"]);
        if (inclusive != 0 || exclusive != 0) throw new InvalidOperationException("Managed stack reports failed; inspect the retained trace and logs.");
        return new(focus, Path.GetFileName(trace), new FileInfo(trace).Length, collected, inclusive, exclusive);
    }

    private static async Task<int> RunProfileCommandAsync(string executable, string directory, string name, string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("The owned managed sampler did not start.");
        var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await child.WaitForExitAsync(timeout.Token);
            return child.ExitCode;
        }
        finally
        {
            if (!child.HasExited) { child.Kill(true); child.WaitForExit(5000); }
            await File.WriteAllTextAsync(Path.Combine(directory, name + ".stdout.log"), await stdout);
            await File.WriteAllTextAsync(Path.Combine(directory, name + ".stderr.log"), await stderr);
        }
    }
}
