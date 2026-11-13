using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using JAAvila.FluentOperations;
using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;
using Treesi.TestSupport;

namespace Treesi.Native.Tests;

public sealed partial class NativePerformanceTests
{
    private sealed record SessionPhase(string Name, int Tabs, IdleObservation Resources);
    private sealed record PresentProbe(string Status, int? ExitCode, PresentCaptureSummary? Capture);

    [Test]
    public async Task RecordEightTabRetentionAndProbeOwnedPresentationEvents()
    {
        IsolatedExecution.Require("native-performance");
        var executable = Environment.GetEnvironmentVariable("TREESI_NATIVE_APP");
        var directory = Environment.GetEnvironmentVariable("TREESI_NATIVE_RESULTS");
        if (executable is null || !Path.IsPathFullyQualified(executable) || !File.Exists(executable) || directory is null || !Path.IsPathFullyQualified(directory))
            throw new InvalidOperationException("The isolated application and results paths must be configured.");
        using var fixture = new SyntheticRepository(1000);
        fixture.AddLinearHistory(10000);
        var roots = new[] { fixture.Root }.Concat(Enumerable.Range(1, 7).Select(fixture.AddLinkedWorktree)).ToArray();
        var phases = new List<SessionPhase>(); var visited = new HashSet<string>(StringComparer.Ordinal);
        var managedProfiles = new List<ManagedProfile>();
        PresentProbe? presentProbe = null; string? failure = null;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        start.ArgumentList.Add(fixture.SamplePath);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The owned application did not start.");
        WindowsDriver? driver = null;
        try
        {
            await WaitForWindowAsync(process);
            var options = new AppiumOptions { PlatformName = "Windows", AutomationName = "Wincore" };
            options.AddAdditionalAppiumOption("appTopLevelWindow", "0x" + process.MainWindowHandle.ToInt64().ToString("x"));
            options.AddAdditionalAppiumOption("shouldCloseApp", false);
            options.AddAdditionalAppiumOption("newCommandTimeout", 300);
            driver = new WindowsDriver(new Uri("http://127.0.0.1:4723"), options, TimeSpan.FromSeconds(60));
            WaitUntil(() => Element(driver, "DiffSearch").Enabled && Element(driver, "FileTitle").Text == "sample.txt");
            LoadGraph1000(driver);
            phases.Add(new("one-tab-graph", 1, await ObserveIdleAsync(process)));
            for (var index = 1; index < roots.Length; index++)
            {
                var root = roots[index];
                Element(driver, "WorkspacePickerButton").Click(); Element(driver, "DiscoverWorktreesButton").Click();
                WaitUntil(() => Element(driver, "WorktreeNotice").Text.StartsWith("8 registered locations", StringComparison.Ordinal));
                FocusNative(driver, "RefreshWorktreesButton");
                SendNativeKeys(driver, new[] { 0x09, 0x09, 0x24 }.Concat(Enumerable.Repeat(0x28, index)).ToArray());
                Element(driver, "OpenWorktreeButton").Click();
                WaitUntil(() => Element(driver, "RepositoryName").Text == Path.GetFileName(root) && Element(driver, "DiffSearch").Displayed && Element(driver, "DiffSearch").Enabled);
                LoadGraph1000(driver);
            }
            phases.Add(new("eight-tabs-graphs", 8, await ObserveIdleAsync(process)));
            for (var cycle = 0; cycle < 2; cycle++)
                for (var index = 0; index < 8; index++)
                {
                    var expected = Path.GetFileName(roots[index]);
                    NativeShortcut(driver, 0x09);
                    WaitUntil(() => Element(driver, "RepositoryName").Text == expected && Element(driver, "GraphReviewButton").Displayed && !Element(driver, "MoreGraphButton").Enabled);
                    visited.Add(expected);
                }
            visited.Count.Test().Be(8);
            presentProbe = await ProbePresentsAsync(driver, process.Id, directory);
            phases.Add(new("eight-tabs-after-cycles-long-idle", 8, await ObserveIdleAsync(process, 60, 1000)));
            for (var index = 7; index > 0; index--)
            {
                NativeShortcut(driver, 0x57);
                var expected = Path.GetFileName(roots[index - 1]);
                WaitUntil(() => Element(driver, "RepositoryName").Text == expected && Element(driver, "GraphReviewButton").Displayed);
            }
            FocusNative(driver, "GraphFindInput");
            phases.Add(new("one-tab-after-closing-seven-search-focus-long-idle", 1, await ObserveIdleAsync(process, 60, 1000)));
            Element(driver, "GraphCommits").Click();
            phases.Add(new("one-tab-after-closing-seven-graph-focus-long-idle", 1, await ObserveIdleAsync(process, 60, 1000)));
            phases.Count.Test().Be(5);
            phases.Skip(2).All(item => item.Resources.Summary.DurationMs >= 55000).Test().BeTrue();
            FocusNative(driver, "GraphFindInput");
            await Task.Delay(3000);
            managedProfiles.Add(await CaptureManagedProfileAsync(process.Id, directory, "search-focus"));
            Element(driver, "GraphCommits").Click();
            await Task.Delay(3000);
            managedProfiles.Add(await CaptureManagedProfileAsync(process.Id, directory, "graph-focus"));
            managedProfiles.Count.Test().Be(2);
        }
        catch (Exception error) { failure = error.GetType().Name; throw; }
        finally
        {
            try { driver?.Quit(); }
            finally
            {
                driver?.Dispose();
                if (!process.HasExited) { process.CloseMainWindow(); if (!process.WaitForExit(5000)) { process.Kill(true); process.WaitForExit(5000); } }
                Directory.CreateDirectory(directory);
                var report = new
                {
                    schemaVersion = "3",
                    sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                    measuredAtUtc = DateTimeOffset.UtcNow,
                    completed = failure is null && phases.Count == 5 && managedProfiles.Count == 2,
                    failure,
                    fixture = new { backgroundFilesPerTree = 1000, importedLinearCommits = 10000, worktrees = 8, graphCommitsPerTab = 1000 },
                    tabCycles = 2,
                    postCloseFocusComparison = "Search input retains focus after closing tabs; the final phase moves focus to the graph in the same process. No caret, animation or runtime settings are changed.",
                    distinctTabsObserved = visited.Count,
                    method = "Owned process counters; no UIA/input during idle; no forced GC. Resident/private changes do not alone establish object retention or leaks.",
                    provisionalTargets = new { workingSetBytes = 512L * 1024 * 1024, privateCommitBytes = 600L * 1024 * 1024, idleOneCoreCpuPercent = 2, enforcement = "Observations, not shared-runner gates." },
                    phases = phases.Select(item => new
                    {
                        item.Name,
                        item.Tabs,
                        item.Resources,
                        workingSetWithinTarget = item.Resources.Summary.MaximumWorkingSetBytes <= 512L * 1024 * 1024,
                        privateCommitWithinTarget = item.Resources.Summary.MaximumPrivateCommitBytes <= 600L * 1024 * 1024,
                        idleCpuWithinTarget = item.Resources.Summary.OneCoreCpuPercent <= 2
                    }),
                    presentProbe,
                    managedProfiles,
                    managedProfileScope = "Two 30-second EventPipe stack samples after all unprofiled resource phases. Sampled managed thread time includes waits and native boundaries; it is not CPU percentage, a native stack profile or presentation timing. Profiler overhead is excluded from resource phases.",
                    presentationScope = "Per-PID ETW probe during paced keyboard selection/scrolling. Present/display intervals include input gaps and are not fixed-rate FPS, render cost or input-to-photon latency. Missing events remain unavailable evidence."
                };
                await File.WriteAllTextAsync(Path.Combine(directory, "native-session-performance.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            }
        }
    }

    private static void LoadGraph1000(WindowsDriver driver)
    {
        Element(driver, "GraphViewButton").Click();
        WaitUntil(() => Element(driver, "GraphNotice").Text.StartsWith("200 commits", StringComparison.Ordinal) && Element(driver, "GraphReviewButton").Enabled);
        foreach (var count in new[] { 400, 600, 800, 1000 })
        {
            Element(driver, "MoreGraphButton").Click();
            WaitUntil(() => Element(driver, "GraphNotice").Text.StartsWith(count + " commits", StringComparison.Ordinal));
        }
    }

    private static async Task<PresentProbe> ProbePresentsAsync(WindowsDriver driver, int ownedPid, string directory)
    {
        var executable = Environment.GetEnvironmentVariable("TREESI_PRESENTMON_EXE");
        var expectedHash = Environment.GetEnvironmentVariable("TREESI_PRESENTMON_SHA256");
        if (executable is null || !Path.IsPathFullyQualified(executable) || !File.Exists(executable) || expectedHash is null || expectedHash.Length != 64
            || !Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executable))).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The pinned PresentMon executable must be configured and verified.");
        var csv = Path.Combine(directory, "owned-presents.csv");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "--process_id", ownedPid.ToString(System.Globalization.CultureInfo.InvariantCulture), "--session_name", "treesi-" + Guid.NewGuid().ToString("N"), "--output_file", csv,
                     "--v1_metrics", "--no_track_input", "--no_console_stats", "--timed", "20", "--terminate_after_timed" }) start.ArgumentList.Add(argument);
        using var collector = Process.Start(start) ?? throw new InvalidOperationException("The owned PresentMon collector did not start.");
        var stdout = collector.StandardOutput.ReadToEndAsync(); var stderr = collector.StandardError.ReadToEndAsync();
        try
        {
            await Task.Delay(1000);
            Element(driver, "GraphCommits").Click();
            var actions = new List<Dictionary<string, object>>();
            for (var index = 0; index < 180; index++) { actions.Add(new() { ["virtualKeyCode"] = 0x28 }); actions.Add(new() { ["pause"] = 50 }); }
            driver.ExecuteScript("windows: keys", new Dictionary<string, object> { ["actions"] = actions });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            await collector.WaitForExitAsync(timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(directory, "presentmon.stdout.log"), await stdout);
            await File.WriteAllTextAsync(Path.Combine(directory, "presentmon.stderr.log"), await stderr);
            if (collector.ExitCode != 0) return new("tool-failed", collector.ExitCode, null);
            if (!File.Exists(csv)) return new("no-events", 0, new("no-events", 0, []));
            if (new FileInfo(csv).Length > 16 * 1024 * 1024) throw new FormatException("The presentation capture exceeded its file bound.");
            var capture = PresentCaptureSummary.FromCsv(await File.ReadAllTextAsync(csv), ownedPid);
            return new(capture.Status, 0, capture);
        }
        finally { if (!collector.HasExited) { collector.Kill(true); collector.WaitForExit(5000); } }
    }

    private static void FocusNative(WindowsDriver driver, string id) =>
        driver.ExecuteScript("windows: setFocus", new Dictionary<string, object> { ["elementId"] = Element(driver, id).Id });

    private static void SendNativeKeys(WindowsDriver driver, params int[] keys) =>
        driver.ExecuteScript("windows: keys", new Dictionary<string, object> { ["actions"] = keys.Select(key => new Dictionary<string, object> { ["virtualKeyCode"] = key }).ToArray() });

    private static void NativeShortcut(WindowsDriver driver, int key)
    {
        FocusNative(driver, "GraphFindInput");
        try
        {
            driver.ExecuteScript("windows: keys", new Dictionary<string, object>
            {
                ["actions"] = new[] {
                new Dictionary<string, object> { ["virtualKeyCode"] = 0x11, ["down"] = true },
                new Dictionary<string, object> { ["virtualKeyCode"] = key },
                new Dictionary<string, object> { ["virtualKeyCode"] = 0x11, ["down"] = false } }
            });
        }
        finally { driver.ExecuteScript("windows: keys", new Dictionary<string, object> { ["actions"] = new[] { new Dictionary<string, object> { ["virtualKeyCode"] = 0x11, ["down"] = false } } }); }
    }
}
