using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Treesi.Desktop.Appearance;

[SupportedOSPlatform("windows")]
internal static class WindowsGraphicsProbe
{
    public static bool? HasHardwareAdapter()
    {
        var factory = IntPtr.Zero;
        try
        {
            var id = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
            if (CreateDXGIFactory1(ref id, out factory) < 0 || factory == IntPtr.Zero) return null;
            // IDXGIFactory1.EnumAdapters1 and IDXGIAdapter1.GetDesc1 retain their documented COM ABI slots.
            var enumerate = Method<EnumAdapters>(factory, 12);
            for (uint index = 0; index < 32; index++)
            {
                var result = enumerate(factory, index, out var adapter);
                if (result == unchecked((int)0x887A0002)) return false;
                if (result < 0 || adapter == IntPtr.Zero) return null;
                try
                {
                    if (Method<GetDescription>(adapter, 10)(adapter, out var description) < 0) return null;
                    if ((description.Flags & 2) == 0 && CanCreateHardwareDevice(adapter)) return true;
                }
                finally { Marshal.Release(adapter); }
            }
            return null;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or COMException) { return null; }
        finally { if (factory != IntPtr.Zero) Marshal.Release(factory); }
    }

    private static T Method<T>(IntPtr instance, int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
    private static bool CanCreateHardwareDevice(IntPtr adapter)
    {
        var device = IntPtr.Zero; var context = IntPtr.Zero;
        try { return D3D11CreateDevice(adapter, 0, IntPtr.Zero, 0, IntPtr.Zero, 0, 7, out device, out _, out context) >= 0 && device != IntPtr.Zero; }
        finally { if (context != IntPtr.Zero) Marshal.Release(context); if (device != IntPtr.Zero) Marshal.Release(device); }
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters(IntPtr factory, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDescription(IntPtr adapter, out AdapterDescription description);
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubsystemId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public Luid AdapterLuid;
        public uint Flags;
    }
    [DllImport("dxgi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CreateDXGIFactory1(ref Guid id, out IntPtr factory);
    [DllImport("d3d11.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels, uint featureLevelCount, uint sdkVersion, out IntPtr device, out uint selectedFeatureLevel, out IntPtr context);
}
