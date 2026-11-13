using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Treesi.NativeProfiler;

internal sealed class ProfilePrivilege : IDisposable
{
    private IntPtr _token;
    private TokenPrivilege _previous;
    private bool _adjusted;

    public ProfilePrivilege()
    {
        try
        {
            if (!OpenProcessToken(new IntPtr(-1), 0x28, out _token) || !LookupPrivilegeValue(null, "SeSystemProfilePrivilege", out var luid))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            var requested = new TokenPrivilege { Count = 1, Luid = luid, Attributes = 2 };
            if (!AdjustTokenPrivileges(_token, false, ref requested, Marshal.SizeOf<TokenPrivilege>(), out _previous, out _) || Marshal.GetLastPInvokeError() != 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            _adjusted = true;
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        if (_token == IntPtr.Zero) return;
        try
        {
            if (_adjusted && !AdjustTokenPrivileges(_token, false, ref _previous, Marshal.SizeOf<TokenPrivilege>(), out _, out _))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        finally { CloseHandle(_token); _token = IntPtr.Zero; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct TokenPrivilege { public uint Count; public Luid Luid; public uint Attributes; }
    [DllImport("advapi32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool AdjustTokenPrivileges(IntPtr token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TokenPrivilege requested, int length, out TokenPrivilege previous, out int returnedLength);
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
