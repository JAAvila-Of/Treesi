using System.Diagnostics;
using System.Runtime.InteropServices;
using JAAvila.FluentOperations;
using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;
using Treesi.TestSupport;

namespace Treesi.Native.Tests;

public sealed partial class NativePerformanceTests
{
    [Test, Category("NativeUiPreference")]
    public async Task GraphicsPreferenceSurvivesRealProductRestartsAndCanBeReverted()
    {
        IsolatedExecution.Require("native-ui");
        var executable = Environment.GetEnvironmentVariable("TREESI_NATIVE_APP")!;
        var directory = Environment.GetEnvironmentVariable("TREESI_NATIVE_RESULTS")!;
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable) || !Path.IsPathFullyQualified(directory))
            throw new InvalidOperationException("The isolated application and diagnostic paths must be configured.");
        var settingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Treesi");
        var path = Path.Combine(settingsDirectory, "graphics.json");
        var original = File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
        using var fixture = new SyntheticRepository(1000);
        try
        {
            Directory.CreateDirectory(settingsDirectory); await File.WriteAllTextAsync(path, "{\"Mode\":\"automatic\"}");
            foreach (var (sequence, active, next, theme) in new[]
            {
                (1, "Automatic", "Software compatibility", "Light"),
                (2, "Software compatibility", "Automatic (recommended)", "Dark"),
                (3, "Automatic", "", "Dark")
            })
            {
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
                start.ArgumentList.Add(fixture.SamplePath);
                using var process = Process.Start(start) ?? throw new InvalidOperationException("The owned product did not start.");
                WindowsDriver? owner = null; WindowsDriver? dialog = null;
                try
                {
                    await WaitForWindowAsync(process); var handle = process.MainWindowHandle;
                    owner = AttachGraphicsWindow(handle);
                    WaitUntil(() => Element(owner, "FileTitle").Text == "sample.txt" && Element(owner, "SettingsButton").Displayed);
                    Element(owner, "ThemeSelector").Click();
                    WaitUntil(() => owner.FindElements(By.Name(theme)).Any(item => item.Displayed));
                    owner.FindElements(By.Name(theme)).First(item => item.Displayed).Click();
                    Element(owner, "SettingsButton").Click();
                    var popup = IntPtr.Zero;
                    WaitUntil(() => { popup = GetWindow(handle, 6); return popup != IntPtr.Zero && popup != handle; });
                    GetWindowThreadProcessId(popup, out var popupPid);
                    ((int)popupPid).Test().Be(process.Id);
                    dialog = AttachGraphicsWindow(popup);
                    Element(dialog, "GraphicsCurrentMode").Text.Test().Be("Current session: " + active);
                    await Task.Delay(250);
                    dialog.GetScreenshot().SaveAsFile(Path.Combine(directory, "graphics-settings-" + sequence + "-" + theme.ToLowerInvariant() + ".png"));
                    if (next.Length == 0) Element(dialog, "CancelGraphicsSettingsButton").Click();
                    else
                    {
                        Element(dialog, "GraphicsModeSelector").Click();
                        WaitUntil(() => dialog.FindElements(By.Name(next)).Any(item => item.Displayed));
                        dialog.FindElements(By.Name(next)).First(item => item.Displayed).Click();
                        Element(dialog, "SaveGraphicsSettingsButton").Click();
                        WaitUntil(() => Element(owner, "OperationStatus").Text.Contains("Restart Treesi", StringComparison.Ordinal));
                    }
                    WaitUntil(() => Element(owner, "SettingsButton").GetAttribute("HasKeyboardFocus").Equals("true", StringComparison.OrdinalIgnoreCase));
                    Element(owner, "FileTitle").Text.Test().Be("sample.txt");
                }
                finally
                {
                    try { dialog?.Quit(); }
                    finally
                    {
                        dialog?.Dispose();
                        try { owner?.Quit(); }
                        finally
                        {
                            owner?.Dispose();
                            if (!process.HasExited) { process.CloseMainWindow(); if (!process.WaitForExit(5000)) { process.Kill(true); process.WaitForExit(5000); } }
                        }
                    }
                }
            }
        }
        finally { if (original is null) File.Delete(path); else await File.WriteAllBytesAsync(path, original); }
    }

    private static WindowsDriver AttachGraphicsWindow(IntPtr handle)
    {
        var options = new AppiumOptions { PlatformName = "Windows", AutomationName = "Wincore" };
        options.AddAdditionalAppiumOption("appTopLevelWindow", "0x" + handle.ToInt64().ToString("x"));
        options.AddAdditionalAppiumOption("shouldCloseApp", false);
        return new WindowsDriver(new Uri("http://127.0.0.1:4723"), options, TimeSpan.FromSeconds(60));
    }

    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr handle, uint command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint pid);
}
