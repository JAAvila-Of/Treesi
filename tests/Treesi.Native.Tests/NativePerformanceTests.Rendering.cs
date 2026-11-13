using System.Diagnostics;
using System.Text.Json;
using JAAvila.FluentOperations;
using NUnit.Framework;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;
using Treesi.TestSupport;

namespace Treesi.Native.Tests;

public sealed partial class NativePerformanceTests
{
    private sealed record RendererObservation(int Sequence, string Requested, int OwnedPid, uint Dpi, JsonElement Backend,
        IdleObservation SearchIdle, IdleObservation GraphIdle);

    [Test, Category("NativeRenderingComparison")]
    public async Task CompareExplicitRenderersWithAlternatingOrderAndUnchangedProductUi()
    {
        IsolatedExecution.Require("native-performance");
        var executable = Environment.GetEnvironmentVariable("TREESI_RENDERING_LAB");
        var directory = Environment.GetEnvironmentVariable("TREESI_NATIVE_RESULTS");
        if (executable is null || !Path.IsPathFullyQualified(executable) || !File.Exists(executable)
            || directory is null || !Path.IsPathFullyQualified(directory))
            throw new InvalidOperationException("The isolated rendering launcher and diagnostic directory must be configured.");
        Directory.CreateDirectory(directory);
        using var fixture = new SyntheticRepository(1000);
        fixture.AddLinearHistory(10000);
        var observations = new List<RendererObservation>(); string? failure = null;
        try
        {
            foreach (var renderer in new[] { "angle", "software", "software", "angle" })
                observations.Add(await ObserveRendererAsync(executable, fixture.SamplePath, directory, renderer, observations.Count + 1));
            observations.Count.Test().Be(4);
            observations.All(item => item.SearchIdle.Summary.DurationMs >= 55000 && item.GraphIdle.Summary.DurationMs >= 18000).Test().BeTrue();
            observations.Select(item => item.Dpi).Distinct().Count().Test().Be(1);
        }
        catch (Exception error) { failure = error.GetType().Name; throw; }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "renderer-comparison.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = "1",
                sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                completed = failure is null && observations.Count == 4,
                failure,
                observations,
                method = "One disposable VM, same product App and synthetic fixture, 1000 graph commits, new process per sample, ANGLE/software/software/ANGLE. Search focus 60s then graph focus 20s, each after 3s settling. No input or UIA queries during counters, no profiler or forced GC. Theme screenshots precede resource windows; every resource window uses dark theme. Caret and motion are unchanged.",
                scope = "Rendering experiment, not the production startup baseline. Shared-host variation remains; service identity is not presentation evidence."
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        }
    }

    private static async Task<RendererObservation> ObserveRendererAsync(string executable, string fixture, string directory, string renderer, int sequence)
    {
        var prefix = $"renderer-{sequence}-{renderer}";
        var diagnostic = Path.Combine(directory, prefix + ".json");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)!, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { renderer, diagnostic, fixture }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The isolated rendering launcher did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        WindowsDriver? driver = null;
        try
        {
            await WaitForWindowAsync(process);
            using var backend = JsonDocument.Parse(await File.ReadAllTextAsync(diagnostic));
            var actual = backend.RootElement;
            actual.GetProperty("ownedPid").GetInt32().Test().Be(process.Id);
            actual.GetProperty("createdUtcTicks").GetInt64().Test().Be(process.StartTime.ToUniversalTime().Ticks);
            actual.GetProperty("requested").GetString().Test().Be(renderer);
            var graphics = actual.GetProperty("platformGraphicsType").GetString();
            (renderer == "software" ? graphics is null : graphics?.Contains("D3D11Angle", StringComparison.Ordinal) == true).Test().BeTrue();
            var options = new AppiumOptions { PlatformName = "Windows", AutomationName = "Wincore" };
            options.AddAdditionalAppiumOption("appTopLevelWindow", "0x" + process.MainWindowHandle.ToInt64().ToString("x"));
            options.AddAdditionalAppiumOption("shouldCloseApp", false);
            options.AddAdditionalAppiumOption("newCommandTimeout", 300);
            driver = new WindowsDriver(new Uri("http://127.0.0.1:4723"), options, TimeSpan.FromSeconds(60));
            WaitUntil(() => Element(driver, "DiffSearch").Enabled && Element(driver, "FileTitle").Text == "sample.txt");
            foreach (var theme in new[] { "Light", "Dark" })
            {
                Element(driver, "ThemeSelector").Click();
                WaitUntil(() => driver.FindElements(OpenQA.Selenium.By.Name(theme)).Any(item => item.Displayed));
                driver.FindElements(OpenQA.Selenium.By.Name(theme)).First(item => item.Displayed).Click();
                WaitUntil(() => (Element(driver, "ThemeSelector").Text + Element(driver, "ThemeSelector").GetAttribute("Value")).Contains(theme, StringComparison.Ordinal));
                await Task.Delay(250);
                driver.GetScreenshot().SaveAsFile(Path.Combine(directory, prefix + "-" + theme.ToLowerInvariant() + ".png"));
            }
            LoadGraph1000(driver);
            FocusNative(driver, "GraphFindInput");
            Element(driver, "GraphFindInput").GetAttribute("HasKeyboardFocus").Equals("true", StringComparison.OrdinalIgnoreCase).Test().BeTrue();
            await Task.Delay(250);
            driver.GetScreenshot().SaveAsFile(Path.Combine(directory, prefix + "-graph.png"));
            var search = await ObserveIdleAsync(process, 60, 1000);
            Element(driver, "GraphCommits").Click();
            Element(driver, "GraphFindInput").GetAttribute("HasKeyboardFocus").Equals("false", StringComparison.OrdinalIgnoreCase).Test().BeTrue();
            var graph = await ObserveIdleAsync(process, 20, 1000);
            return new(sequence, renderer, process.Id, GetDpiForWindow(process.MainWindowHandle), actual.Clone(), search, graph);
        }
        finally
        {
            try { driver?.Quit(); }
            finally
            {
                driver?.Dispose();
                if (!process.HasExited) { process.CloseMainWindow(); if (!process.WaitForExit(5000)) { process.Kill(true); process.WaitForExit(5000); } }
                await File.WriteAllTextAsync(Path.Combine(directory, prefix + ".stdout.log"), await stdout);
                await File.WriteAllTextAsync(Path.Combine(directory, prefix + ".stderr.log"), await stderr);
            }
        }
    }
}
