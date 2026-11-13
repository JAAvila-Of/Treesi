using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.TestSupport;

namespace Treesi.Tests;

[TestFixture]
public sealed class ProcessResourceSummaryTests
{
    [Test]
    public void CpuUsesMeasuredWallTimeAndReportsOneCoreAndMachineNormalization()
    {
        var result = ProcessResourceSummary.From([new(100, 200, 1000, 2000, 1400), new(600, 210, 1200, 2400, 1500), new(1100, 225, 1100, 2100, 1500)], 4);
        result.DurationMs.Test().Be(1000); result.CpuMs.Test().Be(25); result.OneCoreCpuPercent.Test().Be(2.5); result.MachineCpuPercent.Test().Be(.625);
        result.MinimumWorkingSetBytes.Test().Be(1000); result.MaximumWorkingSetBytes.Test().Be(1200);
        result.MaximumPrivateCommitBytes.Test().Be(2400); result.LifetimePeakWorkingSetBytes.Test().Be(1500);
    }

    [Test]
    public void InvalidAndReorderedSamplesAreRefused()
    {
        var valid = new ProcessResourceSample(1, 1, 1, 1, 1);
        foreach (var samples in new[] { Array.Empty<ProcessResourceSample>(), new[] { valid }, new[] { valid, valid }, new[] { valid, valid with { ElapsedMs = 2, CpuMs = 0 } }, new[] { valid, valid with { ElapsedMs = double.NaN } }, new[] { valid, valid with { ElapsedMs = 2, WorkingSetBytes = -1 } } })
        { Action calculate = () => ProcessResourceSummary.From(samples, 2); calculate.Test().Throw<ArgumentException>(); }
        Action invalidCpu = () => ProcessResourceSummary.From([valid, valid with { ElapsedMs = 2 }], 0); invalidCpu.Test().Throw<ArgumentException>();
    }
}
