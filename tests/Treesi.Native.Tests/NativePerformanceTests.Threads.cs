using System.Diagnostics;
using System.Runtime.InteropServices;
using Treesi.TestSupport;

namespace Treesi.Native.Tests;

public sealed partial class NativePerformanceTests
{
    private sealed record ThreadSnapshot(double StartedMs, double CompletedMs, IReadOnlyList<ThreadCpuSample> Samples, IReadOnlyList<int> UnavailableThreadIds);
    private sealed record ThreadObservation(ThreadSnapshot Before, ThreadSnapshot After, ThreadCpuWindow Window);

    private static ThreadSnapshot ReadThreads(Process ownedProcess, Stopwatch elapsed)
    {
        var started = elapsed.Elapsed.TotalMilliseconds;
        ownedProcess.Refresh();
        var samples = new List<ThreadCpuSample>(); var unavailable = new List<int>();
        var threads = ownedProcess.Threads;
        try
        {
            if (threads.Count > 4096) throw new InvalidOperationException("The owned thread snapshot exceeded 4,096 threads.");
            foreach (ProcessThread thread in threads)
            {
                var id = thread.Id;
                var sample = ReadOwnedThread(ownedProcess.Id, id, elapsed);
                if (sample is null) unavailable.Add(id); else samples.Add(sample);
            }
        }
        finally { foreach (ProcessThread thread in threads) thread.Dispose(); }
        return new(started, elapsed.Elapsed.TotalMilliseconds, samples, unavailable);
    }

    private static ThreadCpuSample? ReadOwnedThread(int ownedPid, int id, Stopwatch elapsed)
    {
        var handle = OpenThread(0x0800, false, (uint)id);
        if (handle == IntPtr.Zero) return null;
        try
        {
            if (GetProcessIdOfThread(handle) != (uint)ownedPid || !GetThreadTimes(handle, out var created, out _, out var kernel, out var user)) return null;
            var observed = elapsed.Elapsed.TotalMilliseconds;
            return new(id, DateTime.FromFileTimeUtc(created).Ticks, observed, user / 10000d, kernel / 10000d, ThreadDescription(handle));
        }
        finally { CloseHandle(handle); }
    }

    private static string? ThreadDescription(IntPtr handle)
    {
        var text = IntPtr.Zero;
        try { return GetThreadDescription(handle, out text) >= 0 && text != IntPtr.Zero ? Marshal.PtrToStringUni(text) is { Length: > 0 } name ? name[..Math.Min(name.Length, 128)] : null : null; }
        finally { if (text != IntPtr.Zero) LocalFree(text); }
    }

    [DllImport("kernel32.dll")] private static extern IntPtr OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint threadId);
    [DllImport("kernel32.dll")] private static extern uint GetProcessIdOfThread(IntPtr thread);
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetThreadTimes(IntPtr thread, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll")] private static extern int GetThreadDescription(IntPtr thread, out IntPtr description);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
