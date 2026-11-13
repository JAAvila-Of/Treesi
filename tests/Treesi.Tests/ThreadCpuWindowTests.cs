using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.TestSupport;

namespace Treesi.Tests;

[TestFixture]
public sealed class ThreadCpuWindowTests
{
    [Test]
    public void MatchedThreadsUseTheirOwnMeasuredDurationAndKeepUserAndKernelSeparate()
    {
        var window = ThreadCpuWindow.From([new(1, 10, 100, 10, 20), new(2, 20, 110, 0, 0)],
            [new(2, 20, 1110, 0, 0), new(1, 10, 1100, 25, 30, "render")]);
        window.Matched.Count.Test().Be(2); var active = window.Matched[0]; active.ThreadId.Test().Be(1);
        active.DurationMs.Test().Be(1000); active.UserCpuMs.Test().Be(15); active.KernelCpuMs.Test().Be(10);
        active.OneCoreCpuPercent.Test().Be(2.5); active.Description.Test().Be("render");
        window.StartedThreadIds.Count.Test().Be(0); window.EndedThreadIds.Count.Test().Be(0);
    }

    [Test]
    public void ReusedIdentifiersAreNotSubtractedAndThreadChurnRemainsVisible()
    {
        var window = ThreadCpuWindow.From([new(1, 10, 100, 100, 100), new(2, 20, 100, 0, 0), new(4, 40, 100, 0, 0)],
            [new(1, 11, 1100, 0, 0), new(3, 30, 1100, 0, 0), new(4, 40, 1100, 10, 0)]);
        window.Matched.Count.Test().Be(1); window.Matched[0].ThreadId.Test().Be(4);
        string.Join(',', window.StartedThreadIds).Test().Be("1,3"); string.Join(',', window.EndedThreadIds).Test().Be("1,2");
    }

    [Test]
    public void InvalidSnapshotsAndRegressingCountersAreRejected()
    {
        var valid = new ThreadCpuSample(1, 1, 1, 1, 1);
        foreach (var invalid in new[] { valid with { ThreadId = 0 }, valid with { CreatedUtcTicks = 0 }, valid with { ElapsedMs = double.NaN }, valid with { UserCpuMs = -1 }, valid with { KernelCpuMs = double.PositiveInfinity } })
        { Action calculate = () => ThreadCpuWindow.From([invalid], []); calculate.Test().Throw<ArgumentException>(); }
        foreach (var end in new[] { valid, valid with { ElapsedMs = 2, UserCpuMs = 0 }, valid with { ElapsedMs = 2, KernelCpuMs = 0 } })
        { Action calculate = () => ThreadCpuWindow.From([valid], [end]); calculate.Test().Throw<ArgumentException>(); }
        Action duplicates = () => ThreadCpuWindow.From([valid, valid], []); duplicates.Test().Throw<ArgumentException>();
        Action oversized = () => ThreadCpuWindow.From(Enumerable.Range(1, 4097).Select(id => valid with { ThreadId = id }).ToArray(), []); oversized.Test().Throw<ArgumentException>();
    }

    [Test]
    public void EmptyCoverageDoesNotInventThreadCpu()
    {
        var window = ThreadCpuWindow.From([], []);
        window.Matched.Count.Test().Be(0); window.StartedThreadIds.Count.Test().Be(0); window.EndedThreadIds.Count.Test().Be(0);
    }
}
