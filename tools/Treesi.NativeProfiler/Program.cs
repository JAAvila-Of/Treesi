using System.Diagnostics;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Treesi.TestSupport;

namespace Treesi.NativeProfiler;

internal static class Program
{
    private static readonly Guid ProfileProvider = new("bfeb0324-1cee-496f-a409-2ac2b48a6322");

    public static async Task<int> Main(string[] args)
    {
        IsolatedExecution.Require("native-performance");
        if (args is ["collect-vm", var pid, var created, var focus])
            return await VmCapture.RunAsync(int.Parse(pid, System.Globalization.CultureInfo.InvariantCulture), long.Parse(created, System.Globalization.CultureInfo.InvariantCulture), focus);
        if (args is ["--burn"])
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(30)) Thread.SpinWait(100000);
            return 0;
        }
        if (args is not ["probe"]) throw new ArgumentException("Only the isolated capability probe is supported.");
        var directory = Environment.GetEnvironmentVariable("TREESI_NATIVE_RESULTS");
        if (directory is null || !Path.IsPathFullyQualified(directory)) throw new InvalidOperationException("The isolated result directory must be configured.");
        Directory.CreateDirectory(directory);
        using var target = StartBurner(); using var control = StartBurner();
        var sessionName = "treesi-native-probe-" + Guid.NewGuid().ToString("N");
        var tracePath = Path.Combine(directory, "native-provider-probe.etl");
        var status = "unavailable"; string? failure = null; var targetSamples = 0; var foreignSamples = 0; var stacks = 0; long lost = 0;
        var eventKinds = new Dictionary<string, int>();
        var controlSamples = 0; var unresolvedSamples = 0; var samplesResolvedByThread = 0;
        double targetCpu = 0, controlCpu = 0, durationMs = 0;
        try
        {
            await Task.Delay(1000);
            var modules = target.Modules.Cast<ProcessModule>().Select(item => (Start: (ulong)item.BaseAddress.ToInt64(), End: (ulong)item.BaseAddress.ToInt64() + (ulong)item.ModuleMemorySize)).ToArray();
            var targetBeforeThreads = ThreadIdentities(target); var controlBeforeThreads = ThreadIdentities(control);
            var targetBefore = target.TotalProcessorTime.TotalMilliseconds; var controlBefore = control.TotalProcessorTime.TotalMilliseconds;
            using (var privilege = new ProfilePrivilege())
            using (var session = new TraceEventSession(sessionName, tracePath, TraceEventSessionOptions.Create | TraceEventSessionOptions.NoRestartOnCreate) { BufferSizeMB = 16 })
            {
                session.EnableKernelProvider(KernelTraceEventParser.Keywords.None);
                ClassicStackCapture.Enable(sessionName);
                session.EnableProvider(ProfileProvider, TraceEventLevel.Verbose, 1,
                    new TraceEventProviderOptions { ProcessIDFilter = [target.Id], StacksEnabled = true });
                var elapsed = Stopwatch.StartNew();
                await Task.Delay(5000);
                durationMs = elapsed.Elapsed.TotalMilliseconds;
                session.Stop();
            }
            target.Refresh(); control.Refresh();
            targetCpu = target.TotalProcessorTime.TotalMilliseconds - targetBefore; controlCpu = control.TotalProcessorTime.TotalMilliseconds - controlBefore;
            var targetThreads = targetBeforeThreads.Intersect(ThreadIdentities(target)).Select(item => item.Id).ToHashSet();
            var controlThreads = controlBeforeThreads.Intersect(ThreadIdentities(control)).Select(item => item.Id).ToHashSet();
            if (new FileInfo(tracePath).Length > 64 * 1024 * 1024) throw new InvalidOperationException("The native probe exceeded 64 MiB.");
            var etlx = TraceLog.CreateFromEventTraceLogFile(tracePath);
            using (var log = new TraceLog(etlx))
            {
                lost = log.EventsLost;
                foreach (var item in log.Events)
                {
                    var key = item.ProviderName + "/" + item.EventName;
                    eventKinds[key] = eventKinds.GetValueOrDefault(key) + 1;
                    if (item is not SampledProfileTraceData) continue;
                    var isTarget = item.ProcessID == target.Id;
                    if (item.ProcessID < 0 && targetThreads.Contains(item.ThreadID) && !controlThreads.Contains(item.ThreadID)) { isTarget = true; samplesResolvedByThread++; }
                    if (!isTarget)
                    {
                        foreignSamples++;
                        if (item.ProcessID == control.Id || controlThreads.Contains(item.ThreadID)) controlSamples++; else unresolvedSamples++;
                        continue;
                    }
                    targetSamples++;
                    var stack = item.CallStackIndex() == CallStackIndex.Invalid ? null : log.CallStacks[item.CallStackIndex()];
                    var depth = 0; var userFrame = false;
                    while (stack is not null && depth < 256)
                    {
                        var address = stack.CodeAddress.Address;
                        userFrame |= modules.Any(module => address >= module.Start && address < module.End);
                        depth++; stack = stack.Caller;
                    }
                    if (depth >= 2 && userFrame) stacks++;
                }
            }
            status = NativeProfileAcceptance.Status(durationMs, targetCpu, controlCpu, targetSamples, foreignSamples, stacks, lost);
        }
        catch (Exception error) { failure = error.GetType().Name + ": " + error.Message; }
        finally
        {
            foreach (var child in new[] { target, control }) if (!child.HasExited) { child.Kill(true); child.WaitForExit(5000); }
            if (status != "supported")
                foreach (var path in new[] { tracePath, Path.ChangeExtension(tracePath, ".etlx") }) if (File.Exists(path)) File.Delete(path);
            var report = new
            {
                schemaVersion = "1",
                sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                status,
                failure,
                sessionName,
                targetPid = target.Id,
                controlPid = control.Id,
                durationMs,
                targetCpuMs = targetCpu,
                controlCpuMs = controlCpu,
                targetSamples,
                foreignSamples,
                controlSamples,
                unresolvedSamples,
                samplesResolvedByThread,
                stacks,
                lost,
                eventKinds,
                method = "Two owned synthetic CPU loads; source PID filter requested, unique non-restarting system logger with no legacy global flags. Only validated target-only stack captures are retained. No global sampling interval is changed."
            };
            await File.WriteAllTextAsync(Path.Combine(directory, "native-provider-probe.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        }
        return status == "supported" ? 0 : 2;
    }

    private static Process StartBurner()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--burn");
        return Process.Start(start) ?? throw new InvalidOperationException("The owned CPU probe did not start.");
    }

    private static HashSet<(int Id, long Created)> ThreadIdentities(Process owned)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The isolated native probe requires Windows.");
        owned.Refresh(); var result = new HashSet<(int, long)>(); var threads = owned.Threads;
        try
        {
            foreach (ProcessThread thread in threads)
                try { result.Add((thread.Id, thread.StartTime.ToUniversalTime().Ticks)); }
                catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }
        finally { foreach (ProcessThread thread in threads) thread.Dispose(); }
        return result;
    }
}
