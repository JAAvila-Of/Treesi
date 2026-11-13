using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.TestSupport;

namespace Treesi.Tests;

[TestFixture]
public sealed class PresentCaptureSummaryTests
{
    private const string Header = "Application,ProcessID,SwapChainAddress,Dropped,MsBetweenPresents,MsBetweenDisplayChange,PresentMode\n";
    [Test]
    public void IntervalsStayWithinTheirSwapChainAndPreserveDropsAndMissingDisplayEvidence()
    {
        var csv = Header + "\"Treesi, test.exe\",42,0xaa,0,0,0,Composed\nTreesi.exe,42,0xbb,1,8,NA,Unknown\nTreesi.exe,42,0xaa,0,16,16,Composed\nTreesi.exe,42,0xaa,0,1000,1000,Composed\n";
        var result = PresentCaptureSummary.FromCsv(csv, 42);
        result.Status.Test().Be("display-events-captured"); result.Rows.Test().Be(4); result.SwapChains.Count.Test().Be(2);
        var first = result.SwapChains.Single(item => item.SwapChain == "0xaa");
        first.DisplayedRows.Test().Be(3); first.PresentIntervals!.Samples.Test().Be(2); first.PresentIntervals.MaximumMs.Test().Be(1000);
        first.DisplayChangeIntervals!.MaximumMs.Test().Be(1000);
        var second = result.SwapChains.Single(item => item.SwapChain == "0xbb"); second.DroppedRows.Test().Be(1); second.DisplayChangeIntervals.Test().BeNull();
    }

    [Test]
    public void EmptyAndUndisplayedCapturesCannotClaimDisplayIntervals()
    {
        PresentCaptureSummary.FromCsv("", 42).Status.Test().Be("no-events");
        PresentCaptureSummary.FromCsv(Header, 42).Status.Test().Be("no-events");
        PresentCaptureSummary.FromCsv(Header + "Treesi.exe,42,0xaa,1,16,NA,Unknown\n", 42).Status.Test().Be("presents-without-display-intervals");
    }

    [Test]
    public void ForeignProcessesAndMalformedValuesAreRefused()
    {
        foreach (var row in new[] { "Treesi.exe,43,0xaa,0,16,16,Composed", "Treesi.exe,42,0xaa,0,NaN,16,Composed", "Treesi.exe,42,0xaa,0,-1,16,Composed", "Treesi.exe,42,0xaa,2,16,16,Composed", "incomplete" })
        { Action parse = () => PresentCaptureSummary.FromCsv(Header + row + "\n", 42); parse.Test().Throw<FormatException>(); }
        Action missing = () => PresentCaptureSummary.FromCsv("ProcessID\n42\n", 42); missing.Test().Throw<FormatException>();
    }

    [Test]
    public async Task EightWorktreeFixtureHasIndependentProbeFiles()
    {
        using var fixture = new SyntheticRepository(2);
        var roots = Enumerable.Range(1, 7).Select(fixture.AddLinkedWorktree).ToArray();
        foreach (var index in Enumerable.Range(1, 7)) File.ReadAllText(Path.Combine(roots[index - 1], "sample.txt")).Test().Contain("linked change " + index);
        var worktrees = await new Treesi.Application.WorktreeService(new Treesi.Git.GitInspector()).ListAsync(fixture.Root);
        worktrees.Items.Count.Test().Be(8); worktrees.Items.Select(item => item.Root).Distinct().Count().Test().Be(8);
        File.ReadAllText(fixture.SamplePath).Test().Contain("changed");
    }
}
