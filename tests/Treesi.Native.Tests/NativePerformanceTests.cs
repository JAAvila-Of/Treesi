using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using JAAvila.FluentOperations;
using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;
using Treesi.TestSupport;

namespace Treesi.Native.Tests;

[TestFixture, NonParallelizable, Category("NativePerformance")]
public sealed partial class NativePerformanceTests
{
    private sealed record IdleObservation(ProcessResourceSummary Summary, IReadOnlyList<ProcessResourceSample> Samples, ThreadObservation Threads);
    private sealed record Interaction(string Operation, double ObservedMs);
    private sealed record LaunchObservation(int Sequence, int OwnedProcessId, double WindowObservedMs, double AppiumAttachMs,
        double ReviewObservedMs, string Theme, uint Dpi, IdleObservation WorkingIdle, IReadOnlyList<Interaction> Interactions,
        IdleObservation? Graph1000Idle, bool WindowWithinTarget, bool ReviewWithinTarget, bool WorkingSetWithinTarget,
        bool PrivateCommitWithinTarget, bool IdleCpuWithinTarget);

    [Test]
    public async Task RecordOwnedProcessOpeningAndIdleResources()
    {
        IsolatedExecution.Require("native-performance");
        NativeGraphicsDiagnostic.Write("native-performance");
        var executable = Environment.GetEnvironmentVariable("TREESI_NATIVE_APP");
        var directory = Environment.GetEnvironmentVariable("TREESI_NATIVE_RESULTS");
        var commit = Environment.GetEnvironmentVariable("GITHUB_SHA");
        if (executable is null || !Path.IsPathFullyQualified(executable) || !File.Exists(executable)
            || directory is null || !Path.IsPathFullyQualified(directory) || commit is null || commit.Length != 40 || !commit.All(Uri.IsHexDigit))
            throw new InvalidOperationException("The isolated native performance paths and exact source SHA must be configured.");
        using var fixture = new SyntheticRepository(1000);
        fixture.AddLinearHistory(10000);
        var observations = new List<LaunchObservation>(); string? failure = null;
        try
        {
            for (var sequence = 1; sequence <= 3; sequence++) observations.Add(await ObserveAsync(executable, fixture.SamplePath, sequence));
            observations.Count.Test().Be(3);
            observations.All(item => item.WorkingIdle.Summary.DurationMs >= 4000).Test().BeTrue();
            observations[^1].Interactions.Count.Test().Be(5);
            observations[^1].Graph1000Idle.Test().NotBeNull();
        }
        catch (Exception error) { failure = error.GetType().Name; throw; }
        finally
        {
            Directory.CreateDirectory(directory);
            var report = new
            {
                schemaVersion = "2",
                sourceCommit = commit,
                measuredAtUtc = DateTimeOffset.UtcNow,
                completed = failure is null && observations.Count == 3,
                failure,
                runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                logicalProcessors = Environment.ProcessorCount,
                fixture = new { backgroundFiles = 1000, importedLinearCommits = 10000, launches = 3 },
                method = new
                {
                    windowPollMs = 20,
                    readinessPollMs = 100,
                    idleSettleMs = 3000,
                    idleIntervals = 10,
                    idleIntervalMs = 500,
                    cachePolicy = "OS and runtime caches are not reset; every sample uses a new process.",
                    readinessScope = "Launch to accessible diff observation includes Appium attachment and UIA query latency; not first-frame timing.",
                    resourcesScope = "Owned Treesi process only, including managed and native memory; excludes Git children, Appium and test host.",
                    framePacing = "Not measured. Interaction observations are not presentation timestamps."
                },
                provisionalTargets = new
                {
                    windowObservedMs = 2000,
                    reviewObservedMs = 5000,
                    workingSetBytes = 250L * 1024 * 1024,
                    privateCommitBytes = 300L * 1024 * 1024,
                    idleOneCoreCpuPercent = 2,
                    enforcement = "Observations only; no shared-runner performance gate."
                },
                observations
            };
            await File.WriteAllTextAsync(Path.Combine(directory, "native-process-performance.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        }
    }

    private static async Task<LaunchObservation> ObserveAsync(string executable, string samplePath, int sequence)
    {
        IsolatedExecution.Require("native-performance");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        start.ArgumentList.Add(samplePath);
        var elapsed = Stopwatch.StartNew();
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The owned application did not start.");
        WindowsDriver? driver = null;
        try
        {
            await WaitForWindowAsync(process);
            var windowObserved = elapsed.Elapsed.TotalMilliseconds;
            var handle = process.MainWindowHandle;
            var options = new AppiumOptions { PlatformName = "Windows", AutomationName = "Wincore" };
            options.AddAdditionalAppiumOption("appTopLevelWindow", "0x" + handle.ToInt64().ToString("x"));
            options.AddAdditionalAppiumOption("shouldCloseApp", false);
            var attach = Stopwatch.StartNew();
            driver = new WindowsDriver(new Uri("http://127.0.0.1:4723"), options, TimeSpan.FromSeconds(60));
            var attachMs = attach.Elapsed.TotalMilliseconds;
            WaitUntil(() => Element(driver, "FileTitle").Text == "sample.txt" && Element(driver, "DiffSearch").Enabled
                && Element(driver, "UnifiedDiffText").GetAttribute("Value").Contains("changed", StringComparison.Ordinal));
            var reviewObserved = elapsed.Elapsed.TotalMilliseconds;
            var theme = (Element(driver, "ThemeSelector").Text + " " + Element(driver, "ThemeSelector").GetAttribute("Value")).Trim();
            var dpi = GetDpiForWindow(handle);
            var working = await ObserveIdleAsync(process);
            var interactions = new List<Interaction>(); IdleObservation? graph = null;
            if (sequence == 3)
            {
                var action = Stopwatch.StartNew(); Element(driver, "GraphViewButton").Click();
                WaitUntil(() => Element(driver, "GraphReviewButton").Enabled && Element(driver, "GraphNotice").Text.StartsWith("200 commits", StringComparison.Ordinal));
                interactions.Add(new("graph-200", action.Elapsed.TotalMilliseconds));
                foreach (var count in new[] { 400, 600, 800, 1000 })
                {
                    action.Restart(); Element(driver, "MoreGraphButton").Click();
                    WaitUntil(() => Element(driver, "GraphNotice").Text.StartsWith(count + " commits", StringComparison.Ordinal) && Element(driver, "GraphReviewButton").Enabled);
                    interactions.Add(new("graph-" + count, action.Elapsed.TotalMilliseconds));
                }
                graph = await ObserveIdleAsync(process);
            }
            var maximumWorkingSet = Math.Max(working.Summary.MaximumWorkingSetBytes, graph?.Summary.MaximumWorkingSetBytes ?? 0);
            var maximumPrivate = Math.Max(working.Summary.MaximumPrivateCommitBytes, graph?.Summary.MaximumPrivateCommitBytes ?? 0);
            var maximumIdleCpu = Math.Max(working.Summary.OneCoreCpuPercent, graph?.Summary.OneCoreCpuPercent ?? 0);
            return new(sequence, process.Id, windowObserved, attachMs, reviewObserved, theme, dpi, working, interactions, graph,
                windowObserved <= 2000, reviewObserved <= 5000, maximumWorkingSet <= 250L * 1024 * 1024,
                maximumPrivate <= 300L * 1024 * 1024, maximumIdleCpu <= 2);
        }
        finally
        {
            try { driver?.Quit(); }
            finally
            {
                driver?.Dispose();
                if (!process.HasExited)
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(5000)) { process.Kill(entireProcessTree: true); process.WaitForExit(5000); }
                }
            }
        }
    }

    private static async Task<IdleObservation> ObserveIdleAsync(Process process, int intervals = 10, int intervalMs = 500)
    {
        await Task.Delay(3000);
        var elapsed = Stopwatch.StartNew(); var samples = new List<ProcessResourceSample>();
        var threadsBefore = ReadThreads(process, elapsed);
        for (var index = 0; index <= intervals; index++)
        {
            if (index != 0) await Task.Delay(intervalMs);
            process.Refresh();
            if (process.HasExited) throw new InvalidOperationException("The owned application exited during idle observation.");
            samples.Add(new(elapsed.Elapsed.TotalMilliseconds, process.TotalProcessorTime.TotalMilliseconds, process.WorkingSet64, process.PrivateMemorySize64, process.PeakWorkingSet64));
        }
        var threadsAfter = ReadThreads(process, elapsed);
        return new(ProcessResourceSummary.From(samples, Environment.ProcessorCount), samples,
            new(threadsBefore, threadsAfter, ThreadCpuWindow.From(threadsBefore.Samples, threadsAfter.Samples)));
    }

    private static async Task WaitForWindowAsync(Process process)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(20))
        {
            process.Refresh();
            if (process.HasExited) throw new InvalidOperationException("The owned application exited before exposing a window.");
            if (process.MainWindowHandle != IntPtr.Zero) return;
            await Task.Delay(20);
        }
        throw new TimeoutException("The owned application window was not observed.");
    }

    private static AppiumElement Element(WindowsDriver driver, string id) => driver.FindElement(new ByAccessibilityId(id));
    private static void WaitUntil(Func<bool> predicate)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(20))
        {
            try { if (predicate()) return; } catch (WebDriverException) { }
            Thread.Sleep(100);
        }
        throw new TimeoutException("The expected accessible review state was not observed.");
    }

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
}
