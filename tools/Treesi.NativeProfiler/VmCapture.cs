using System.Diagnostics;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace Treesi.NativeProfiler;

internal static class VmCapture
{
    private sealed record StackGroup(int ThreadId, ulong[] Addresses, int Count);

    public static async Task<int> RunAsync(int ownedPid, long createdUtcTicks, string focus)
    {
        if (Environment.GetEnvironmentVariable("TREESI_NATIVE_CAPTURE_SCOPE") != "disposable-vm-pid-export"
            || focus is not ("search-focus" or "graph-focus")) throw new InvalidOperationException("Explicit disposable-VM capture scope is required.");
        using var target = Process.GetProcessById(ownedPid);
        var expected = Environment.GetEnvironmentVariable("TREESI_NATIVE_APP");
        if (expected is null || !Path.IsPathFullyQualified(expected) || target.StartTime.ToUniversalTime().Ticks != createdUtcTicks
            || !string.Equals(target.MainModule?.FileName, Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The owned process identity differs.");
        var directory = Environment.GetEnvironmentVariable("TREESI_NATIVE_RESULTS");
        if (directory is null || !Path.IsPathFullyQualified(directory)) throw new InvalidOperationException("The isolated result directory must be configured.");
        Directory.CreateDirectory(directory);
        var transient = Path.Combine(Path.GetTempPath(), "treesi-native-transient-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(transient);
        var trace = Path.Combine(transient, "capture.etl"); var etlx = Path.ChangeExtension(trace, ".etlx");
        var sessionName = "treesi-native-probe-" + Guid.NewGuid().ToString("N");
        var groups = new Dictionary<string, StackGroup>(); var targetSamples = 0; var userStacks = 0; var excludedSamples = 0; long lost = 0;
        var status = "unavailable"; string? failure = null; double durationMs = 0; var bounded = true;
        var modules = target.Modules.Cast<ProcessModule>().Select(item => new ModuleRange((ulong)item.BaseAddress.ToInt64(), (ulong)item.BaseAddress.ToInt64() + (ulong)item.ModuleMemorySize,
            item.ModuleName, item.FileName, item.FileVersionInfo.FileVersion)).ToArray();
        var export = new List<object>();
        try
        {
            var before = Threads(target);
            using (var privilege = new ProfilePrivilege())
            using (var session = new TraceEventSession(sessionName, trace, TraceEventSessionOptions.Create | TraceEventSessionOptions.NoRestartOnCreate) { BufferSizeMB = 16 })
            {
                session.EnableKernelProvider(KernelTraceEventParser.Keywords.None);
                ClassicStackCapture.Enable(sessionName);
                session.EnableProvider(new Guid("bfeb0324-1cee-496f-a409-2ac2b48a6322"), TraceEventLevel.Verbose, 1);
                var elapsed = Stopwatch.StartNew(); await Task.Delay(20000); durationMs = elapsed.Elapsed.TotalMilliseconds;
                session.Stop();
            }
            var stable = before.Intersect(Threads(target)).ToDictionary(item => item.Id, item => item.Created);
            if (target.HasExited || target.StartTime.ToUniversalTime().Ticks != createdUtcTicks) throw new InvalidOperationException("The owned process lifetime changed.");
            if (new FileInfo(trace).Length > 64 * 1024 * 1024) throw new InvalidOperationException("The transient native trace exceeded 64 MiB.");
            var stdout = Console.Out; var stderr = Console.Error;
            try { Console.SetOut(TextWriter.Null); Console.SetError(TextWriter.Null); TraceLog.CreateFromEventTraceLogFile(trace, etlx); }
            finally { Console.SetOut(stdout); Console.SetError(stderr); }
            using (var log = new TraceLog(etlx))
            {
                lost = log.EventsLost;
                foreach (var item in log.Events)
                {
                    if (item is not SampledProfileTraceData sample || sample.NonProcess) continue;
                    if (!stable.ContainsKey(item.ThreadID) || (item.ProcessID >= 0 && item.ProcessID != ownedPid)) { excludedSamples++; continue; }
                    targetSamples++;
                    var stack = item.CallStackIndex() == CallStackIndex.Invalid ? null : log.CallStacks[item.CallStackIndex()];
                    var frames = new List<ulong>();
                    while (stack is not null && frames.Count < 256) { frames.Add(stack.CodeAddress.Address); stack = stack.Caller; }
                    if (stack is not null) bounded = false;
                    if (frames.Count < 2 || !frames.Any(address => modules.Any(module => address >= module.Start && address < module.End))) continue;
                    userStacks++;
                    var key = item.ThreadID + ":" + string.Join(',', frames);
                    if (groups.TryGetValue(key, out var group)) groups[key] = group with { Count = group.Count + 1 };
                    else if (groups.Count < 2048) groups.Add(key, new(item.ThreadID, frames.ToArray(), 1));
                    else bounded = false;
                }
            }
            DeleteRaw(trace, etlx);
            var selected = groups.Values.GroupBy(group => modules.FirstOrDefault(module => group.Addresses[0] >= module.Start && group.Addresses[0] < module.End)?.Name)
                .Where(group => group.Key is not null).OrderByDescending(group => group.Sum(item => item.Count)).Take(5).Select(group => group.Key!).ToArray();
            using (var symbols = new NativeSymbols(Path.Combine(transient, "symbols"), selected))
                foreach (var group in groups.Values.OrderByDescending(item => item.Count))
                    export.Add(new { group.ThreadId, createdUtcTicks = stable[group.ThreadId], samples = group.Count, frames = group.Addresses.Select(address => symbols.Describe(address, modules)).ToArray() });
            status = !bounded ? "truncated" : lost > 0 ? "events-lost" : targetSamples == 0 ? "no-target-samples" : userStacks == 0 ? "no-user-stacks" : "captured";
        }
        catch (Exception error) { failure = error.GetType().Name + ": " + error.Message; }
        finally
        {
            DeleteRaw(trace, etlx);
            var report = new
            {
                schemaVersion = "1",
                sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                ownedPid,
                createdUtcTicks,
                focus,
                status,
                failure,
                sessionName,
                durationMs,
                targetSamples,
                userStacks,
                excludedSamples,
                eventsLost = lost,
                bounded,
                modules = modules.Select(item => new { item.Name, item.Version }),
                stacks = export,
                scope = "Temporary VM-wide kernel sampling in an explicitly admitted disposable hosted VM; only stable owned-thread stacks exported. Raw ETL/ETLX are outside artifact paths and deleted before symbol lookup. Not source PID filtering, unprofiled CPU or presentation timing."
            };
            await File.WriteAllTextAsync(Path.Combine(directory, "native-" + focus + ".json"), JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        }
        return status is "captured" or "no-target-samples" ? 0 : 2;
    }

    private static HashSet<(int Id, long Created)> Threads(Process owned)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        owned.Refresh(); var entries = owned.Threads; var result = new HashSet<(int, long)>();
        try
        {
            if (entries.Count > 4096) throw new InvalidOperationException("The owned thread snapshot exceeded its bound.");
            foreach (ProcessThread thread in entries)
                try { result.Add((thread.Id, thread.StartTime.ToUniversalTime().Ticks)); }
                catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }
        finally { foreach (ProcessThread thread in entries) thread.Dispose(); }
        return result;
    }

    private static void DeleteRaw(params string[] files) { foreach (var file in files) if (File.Exists(file)) File.Delete(file); }
}
