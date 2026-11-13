using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Treesi.NativeProfiler;

internal static class ClassicStackCapture
{
    public static void Enable(string ownedSession)
    {
        if (!ownedSession.StartsWith("treesi-native-probe-", StringComparison.Ordinal)) throw new ArgumentException("Only the owned probe session may be configured.");
        var properties = Marshal.AllocHGlobal(4096);
        try
        {
            Marshal.Copy(new byte[4096], 0, properties, 4096);
            Marshal.WriteInt32(properties, 0, 4096);
            Marshal.WriteInt32(properties, 116, 120);
            Marshal.WriteInt32(properties, 112, 120 + (ownedSession.Length + 1) * 2);
            var result = ControlTrace(0, ownedSession, properties, 0);
            if (result != 0) throw new Win32Exception((int)result);
            var handle = (ulong)Marshal.ReadInt64(properties, 8);
            var profile = new ClassicEventId { EventGuid = new("ce1dbfb4-137e-4da6-87b0-3f59aa102cbc"), Type = 46, Reserved = new byte[7] };
            result = TraceSetInformation(handle, 3, ref profile, (uint)Marshal.SizeOf<ClassicEventId>());
            if (result != 0) throw new Win32Exception((int)result);
        }
        finally { Marshal.FreeHGlobal(properties); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct ClassicEventId { public Guid EventGuid; public byte Type; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)] public byte[] Reserved; }
    [DllImport("advapi32.dll", EntryPoint = "ControlTraceW", CharSet = CharSet.Unicode)] private static extern uint ControlTrace(ulong handle, string name, IntPtr properties, uint control);
    [DllImport("advapi32.dll")] private static extern uint TraceSetInformation(ulong handle, int informationClass, ref ClassicEventId information, uint length);
}
