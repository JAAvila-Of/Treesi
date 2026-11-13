using System.Security.Cryptography;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Application;
using Treesi.Domain;
using Treesi.Git;
using Treesi.TestSupport;

namespace Treesi.Tests;

[TestFixture]
public sealed class PerformanceFixtureTests
{
    [TestCase(200)]
    [TestCase(1000)]
    public async Task ImportedHistoryIsRealAndPreservesTheWorkingReview(int commits)
    {
        using var fixture = new SyntheticRepository(3);
        var indexPath = Path.Combine(fixture.Root, ".git", "index");
        var index = SHA256.HashData(File.ReadAllBytes(indexPath)); var working = File.ReadAllText(fixture.SamplePath);
        var tip = fixture.AddLinearHistory(commits); var git = new GitInspector();
        var graph = await new GraphService(git).ReadAsync(fixture.Root, new("refs/heads/benchmark-history", Limit: 1000));
        graph.Commits.Count.Test().Be(Math.Min(1000, commits + 1)); graph.Commits[0].CommitId.Test().Be(tip);
        graph.Commits[0].Subject.Test().Be($"perf: synthetic revision {commits}"); graph.HasMore.Test().Be(commits >= 1000);
        graph.Commits[0].Parents.Count.Test().Be(1);
        var history = new HistoryService(git); var detail = await history.CommitAsync(fixture.Root, tip);
        var diff = await new RevisionService(git).DiffAsync(detail.Comparison(), "sample.txt");
        diff.Hunks.SelectMany(hunk => hunk.Lines).Any(line => line.Kind == DiffLineKind.Added && line.Text == $"revision {commits}").Test().BeTrue();
        File.ReadAllText(fixture.SamplePath).Test().Be(working);
        SHA256.HashData(File.ReadAllBytes(indexPath)).SequenceEqual(index).Test().BeTrue();
        var current = await new InspectionService(git).DiffAsync(fixture.Root, "sample.txt", Comparison.WorkingTree);
        current.Hunks.Count.Test().Be(1);
    }

    [Test]
    public void PercentilesUseNearestRankAndDoNotMutateTheInput()
    {
        var samples = Enumerable.Range(1, 30).Reverse().Select(value => (double)value).ToArray();
        var summary = LatencySummary.From(samples);
        summary.Samples.Test().Be(30); summary.MinimumMs.Test().Be(1); summary.MeanMs.Test().Be(15.5);
        summary.MedianMs.Test().Be(15.5); summary.P95Ms.Test().Be(29); summary.MaximumMs.Test().Be(30); samples[0].Test().Be(30);
        LatencySummary.From([10d]).MedianMs.Test().Be(10);
        foreach (var invalid in new[] { Array.Empty<double>(), new[] { -1d }, new[] { double.NaN }, new[] { double.PositiveInfinity }, new double[10001] })
        { Action calculate = () => LatencySummary.From(invalid); calculate.Test().Throw<ArgumentException>(); }
    }
}
