using Win32Exception = System.ComponentModel.Win32Exception;
using System.Diagnostics;
using System.Runtime.InteropServices;
using JAAvila.FluentOperations;
using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium.Windows;
using Treesi.TestSupport;

namespace Treesi.Native.Tests;

public sealed partial class NativePerformanceTests
{
    [Test, Category("NativeUiDrop")]
    public async Task FileDropCrossesOwnedNativeProcessesWithoutChangingTheOriginal()
    {
        IsolatedExecution.Require("native-ui");
        if (IntPtr.Size != 8) throw new PlatformNotSupportedException("The isolated mouse input ABI requires a 64-bit test host.");
        var executable = Environment.GetEnvironmentVariable("TREESI_NATIVE_APP")!;
        var launcher = Environment.GetEnvironmentVariable("TREESI_NATIVE_DRAG_SOURCE")!;
        var directory = Environment.GetEnvironmentVariable("TREESI_NATIVE_RESULTS")!;
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable) || !Path.IsPathFullyQualified(launcher) || !File.Exists(launcher) || !Path.IsPathFullyQualified(directory))
            throw new InvalidOperationException("The owned application and drag-source launcher must be configured.");
        using var fixture = new SyntheticRepository();
        var path = Path.Combine(fixture.Root, "new.txt");
        var original = await File.ReadAllTextAsync(path); var index = await File.ReadAllBytesAsync(Path.Combine(fixture.Root, ".git", "index"));
        using var target = StartDropProcess(executable, fixture.SamplePath);
        Process? source = null; WindowsDriver? targetDriver = null; WindowsDriver? sourceDriver = null;
        var pressed = false;
        try
        {
            await WaitForWindowAsync(target);
            targetDriver = AttachGraphicsWindow(target.MainWindowHandle);
            WaitUntil(() => Element(targetDriver, "FileTitle").Text == "sample.txt");
            source = StartDropProcess(launcher, "drag-source", path);
            await WaitForWindowAsync(source);
            sourceDriver = AttachGraphicsWindow(source.MainWindowHandle);
            WaitUntil(() => Element(sourceDriver, "DragFixtureFile").Enabled);
            PlaceOwnedDropWindow(target, 300, 0);
            PlaceOwnedDropWindow(source, 40, 40);
            var origin = DropScreenPoint(source.MainWindowHandle, 125, 95);
            var destination = DropScreenPoint(target.MainWindowHandle, 440, 290);
            // Input is confined to this explicitly admitted VM and the two verified owned windows.
            if (!SetCursorPos(origin.X, origin.Y)) throw new Win32Exception(Marshal.GetLastWin32Error());
            DropMouse(2); pressed = true;
            await Task.Delay(200);
            for (var step = 1; step <= 16; step++)
            {
                if (!SetCursorPos(origin.X + (destination.X - origin.X) * step / 16, origin.Y + (destination.Y - origin.Y) * step / 16))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                await Task.Delay(30);
            }
            await Task.Delay(300);
            DropMouse(4); pressed = false;
            WaitUntil(() => Element(targetDriver, "FileTitle").Text == "new.txt");
            WaitUntil(() => Element(sourceDriver, "DragSourceStatus").Text == "Review requested");
            (await File.ReadAllTextAsync(path)).Test().Be(original);
            (await File.ReadAllBytesAsync(Path.Combine(fixture.Root, ".git", "index"))).SequenceEqual(index).Test().BeTrue();
        }
        catch
        {
            if (pressed) { DropMouse(4); pressed = false; }
            foreach (var (driver, name) in new[] { (targetDriver, "target"), (sourceDriver, "source") })
            {
                if (driver is null) continue;
                try
                {
                    await File.WriteAllTextAsync(Path.Combine(directory, "native-drop-" + name + ".xml"), driver.PageSource);
                    driver.GetScreenshot().SaveAsFile(Path.Combine(directory, "native-drop-" + name + ".png"));
                }
                catch (WebDriverException) { }
            }
            throw;
        }
        finally
        {
            try { if (pressed) DropMouse(4); }
            finally
            {
                try { sourceDriver?.Quit(); }
                finally
                {
                    sourceDriver?.Dispose();
                    try { targetDriver?.Quit(); }
                    finally
                    {
                        targetDriver?.Dispose(); StopDropProcess(source); StopDropProcess(target); source?.Dispose();
                    }
                }
            }
        }
    }

    private static Process StartDropProcess(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("The owned native process did not start.");
    }
    private static void StopDropProcess(Process? process)
    {
        if (process is null || process.HasExited) return;
        process.CloseMainWindow(); if (!process.WaitForExit(5000)) { process.Kill(true); process.WaitForExit(5000); }
    }
    private static void PlaceOwnedDropWindow(Process process, int x, int y)
    {
        var handle = process.MainWindowHandle;
        GetWindowThreadProcessId(handle, out var pid);
        if (pid != process.Id || !SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 1)) throw new InvalidOperationException("The owned window could not be positioned.");
    }
    private static DropPoint DropScreenPoint(IntPtr handle, int x, int y)
    {
        var point = new DropPoint { X = x, Y = y };
        if (!ClientToScreen(handle, ref point)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return point;
    }
    private static void DropMouse(uint flags)
    {
        var input = new[] { new DropInput { Mouse = new DropMouseInput { Flags = flags } } };
        if (SendInput(1, input, Marshal.SizeOf<DropInput>()) != 1) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    [StructLayout(LayoutKind.Sequential)] private struct DropPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct DropMouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct DropInput { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public DropMouseInput Mouse; }
    [DllImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(IntPtr handle, ref DropPoint point);
    [DllImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, DropInput[] inputs, int size);
}
