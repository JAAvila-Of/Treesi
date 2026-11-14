using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.TestSupport;

namespace Treesi.Native.Tests;

[TestFixture, NonParallelizable, Category("LinuxNative"), SupportedOSPlatform("linux")]
public sealed class NativeLinuxLaunchTests
{
    [OneTimeSetUp]
    public void RequireOwnedVirtualDisplay() => IsolatedExecution.Require("linux-native-ui");

    [Test]
    public async Task RegisteredDesktopEntryLaunchesTheRealOwnedNativeWindow()
    {
        IsolatedExecution.Require("linux-native-ui");
        var temporary = Environment.GetEnvironmentVariable("RUNNER_TEMP")!;
        var executable = LinuxVirtualDisplay.RequireChild(temporary, Environment.GetEnvironmentVariable("TREESI_NATIVE_APP")!);
        var directory = Path.GetDirectoryName(executable)!;
        var results = LinuxVirtualDisplay.RequireChild(temporary, Environment.GetEnvironmentVariable("TREESI_NATIVE_RESULTS")!);
        var helper = Path.Combine(directory, "Treesi.Integration");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "package-manifest.json")));
        manifest.RootElement.GetProperty("sourceCommit").GetString().Test().Be(Environment.GetEnvironmentVariable("GITHUB_SHA"));
        var registration = await Run(helper, "register"); registration.Code.Test().Be(0);
        using var response = JsonDocument.Parse(registration.Output);
        response.RootElement.GetProperty("ok").GetBoolean().Test().BeTrue();
        var entry = LinuxVirtualDisplay.RequireChild(temporary, response.RootElement.GetProperty("data").GetProperty("entryPath").GetString()!);
        (await Run("desktop-file-validate", entry)).Code.Test().Be(0);
        var descriptor = DesktopInfo(entry);
        if (descriptor == IntPtr.Zero) throw new InvalidOperationException("GIO refused the registered entry.");
        var childPid = 0;
        Process? child = null;
        var owned = false;
        using var stdout = File.Create(Path.Combine(results, "desktop.stdout.log"));
        using var stderr = File.Create(Path.Combine(results, "desktop.stderr.log"));
        LaunchCallback callback = (_, pid, _) => childPid = childPid == 0 ? pid : -1;
        try
        {
            // DO_NOT_REAP_CHILD preserves the direct parent and cleanup responsibility.
            var launched = Launch(descriptor, IntPtr.Zero, IntPtr.Zero, 2, IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero,
                -1, stdout.SafeFileHandle.DangerousGetHandle().ToInt32(), stderr.SafeFileHandle.DangerousGetHandle().ToInt32(), out var error);
            GC.KeepAlive(callback);
            if (launched == 0)
            {
                var message = error == IntPtr.Zero ? "GIO launch failed." : Marshal.PtrToStringUTF8(Marshal.PtrToStructure<LaunchError>(error).Message)!;
                if (error != IntPtr.Zero) FreeError(error);
                throw new InvalidOperationException(message);
            }
            (childPid > 0).Test().BeTrue();
            child = Process.GetProcessById(childPid);
            owned = Parent(childPid) == Environment.ProcessId;
            owned.Test().BeTrue();
            await Until(() => Task.FromResult(File.ResolveLinkTarget($"/proc/{childPid}/exe", true)?.FullName == executable));
            Parent(childPid).Test().Be(Environment.ProcessId);
            var window = "";
            await Until(async () =>
            {
                var search = await Run("xdotool", "search", "--all", "--onlyvisible", "--pid", childPid.ToString(CultureInfo.InvariantCulture), "--name", "Treesi");
                window = search.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                return search.Code == 0 && ulong.TryParse(window, out _);
            });
            (await Run("xdotool", "getwindowpid", window)).Output.Trim().Test().Be(childPid.ToString(CultureInfo.InvariantCulture));
            var title = (await Run("xdotool", "getwindowname", window)).Output.Trim();
            title.Test().Contain("Treesi");
            var geometry = (await Run("xdotool", "getwindowgeometry", "--shell", window)).Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('=', 2)).Where(parts => parts.Length == 2).ToDictionary(parts => parts[0], parts => parts[1]);
            (int.Parse(geometry["WIDTH"], CultureInfo.InvariantCulture) >= 900).Test().BeTrue();
            (int.Parse(geometry["HEIGHT"], CultureInfo.InvariantCulture) >= 560).Test().BeTrue();
            // Allow initial native layout/render completion; this delay is not a performance observation.
            await Task.Delay(2000);
            var screenshot = Path.Combine(results, "native-linux-home.png");
            (await Run("import", "-silent", "-window", window, screenshot)).Code.Test().Be(0);
            var bytes = await File.ReadAllBytesAsync(screenshot);
            bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }).Test().BeTrue();
            (bytes.Length > 10000).Test().BeTrue();
            child.HasExited.Test().BeFalse();
            await File.WriteAllTextAsync(Path.Combine(results, "linux-native-launch.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = "1",
                sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                launch = "GIO registered desktop entry",
                processId = childPid,
                parentProcessId = Parent(childPid),
                windowId = window,
                title,
                geometry,
                display = Environment.GetEnvironmentVariable("DISPLAY"),
                screenshot = "native-linux-home.png",
                scope = "Real portable apphost, native X11 window on owned Xvfb/Openbox in disposable hosted Linux. No mouse/keyboard input. Home launch only; not GNOME/KDE menu, accessibility, diff correctness, GPU or latency certification."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            if (child is not null && !child.HasExited && owned && Parent(childPid) == Environment.ProcessId) child.Kill(true);
            if (childPid > 0 && ParentOrMissing(childPid) == Environment.ProcessId)
                for (var attempt = 0; attempt < 100; attempt++)
                {
                    if (WaitPid(childPid, out _, 1) != 0) break;
                    await Task.Delay(50);
                }
            child?.Dispose();
            Unref(descriptor);
            (await Run(helper, "unregister")).Code.Test().Be(0);
        }
    }

    private static int Parent(int pid) => int.Parse(File.ReadLines($"/proc/{pid}/status").Single(line => line.StartsWith("PPid:", StringComparison.Ordinal)).Split(':')[1].Trim(), CultureInfo.InvariantCulture);
    private static int ParentOrMissing(int pid) { try { return Parent(pid); } catch (FileNotFoundException) { return -1; } catch (DirectoryNotFoundException) { return -1; } }

    private static async Task Until(Func<Task<bool>> condition)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(30)) { if (await condition()) return; await Task.Delay(100); }
        throw new TimeoutException("The owned native window did not become ready.");
    }

    private static async Task<(int Code, string Output)> Run(string executable, params string[] arguments)
    {
        IsolatedExecution.Require("linux-native-ui");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The lab tool failed to start.");
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        finally { if (!process.HasExited) process.Kill(true); }
        var text = await output; var error = await errors;
        if (process.ExitCode != 0) TestContext.Progress.WriteLine($"Lab tool {Path.GetFileName(executable)} exited {process.ExitCode}: {error}");
        return (process.ExitCode, text);
    }

    [StructLayout(LayoutKind.Sequential)] private struct LaunchError { public uint Domain; public int Code; public IntPtr Message; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void LaunchCallback(IntPtr info, int pid, IntPtr data);
    [DllImport("libgio-2.0.so.0", EntryPoint = "g_desktop_app_info_new_from_filename")] private static extern IntPtr DesktopInfo([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport("libgio-2.0.so.0", EntryPoint = "g_desktop_app_info_launch_uris_as_manager_with_fds")]
    private static extern int Launch(IntPtr info, IntPtr uris, IntPtr context, int flags, IntPtr setup, IntPtr setupData, LaunchCallback callback, IntPtr callbackData, int stdin, int stdout, int stderr, out IntPtr error);
    [DllImport("libgobject-2.0.so.0", EntryPoint = "g_object_unref")] private static extern void Unref(IntPtr value);
    [DllImport("libglib-2.0.so.0", EntryPoint = "g_error_free")] private static extern void FreeError(IntPtr error);
    [DllImport("libc", EntryPoint = "waitpid")] private static extern int WaitPid(int pid, out int status, int options);
}
