using System.Reflection;
using System.Runtime.InteropServices;
using JAAvila.FluentOperations;
using NUnit.Framework;

namespace Treesi.Desktop.Tests;

[TestFixture]
public sealed class WindowsGraphicsProbeTests
{
    [Test]
    public void AdapterDescriptorMatchesTheWindowsDxgiAbiWithoutCallingNativeApis()
    {
        var probe = typeof(MainWindow).Assembly.GetType("Treesi.Desktop.Appearance.WindowsGraphicsProbe")!;
        var description = probe.GetNestedType("AdapterDescription", BindingFlags.NonPublic)!;
        Marshal.OffsetOf(description, "Description").ToInt32().Test().Be(0);
        Marshal.OffsetOf(description, "VendorId").ToInt32().Test().Be(256);
        Marshal.OffsetOf(description, "DedicatedVideoMemory").ToInt32().Test().Be(272);
        Marshal.OffsetOf(description, "Flags").ToInt32().Test().Be(IntPtr.Size == 8 ? 304 : 292);
        Marshal.SizeOf(description).Test().Be(IntPtr.Size == 8 ? 312 : 296);
    }
}
