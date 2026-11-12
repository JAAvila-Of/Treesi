using System.Security.Cryptography;
using System.Text.Json;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Application;
using Treesi.Cli;
using Treesi.Domain;
using Treesi.Git;

namespace Treesi.Tests;

[TestFixture]
public sealed class FullContextTests
{
    private static void Commit(GitFixture fixture)
    {
        fixture.Git("add", "--", "sample.txt");
        fixture.Git("-c", "commit.gpgSign=false", "-c", "core.hooksPath=", "commit", "-m", "test: prepare full context");
    }
    private static string[] Lines() => Enumerable.Range(1, 220).Select(row => $"source line {row:D3}").ToArray();

    [TestCase(Comparison.WorkingTree, 1)]
    [TestCase(Comparison.Staged, 1)]
    [TestCase(Comparison.Head, 2)]
    public async Task FullContextIncludesFileEdgesForEveryLiveComparisonWithoutIndexWrites(Comparison comparison, int added)
    {
        using var fixture = new GitFixture(); var lines = Lines();
        fixture.Write("sample.txt", string.Join('\n', lines) + "\n"); Commit(fixture);
        lines[79] = "staged change"; fixture.Write("sample.txt", string.Join('\n', lines) + "\n"); fixture.Git("add", "sample.txt");
        lines[159] = "working change"; fixture.Write("sample.txt", string.Join('\n', lines) + "\n");
        var hash = SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture.Root, ".git", "index")));
        var service = new InspectionService(new GitInspector()); var options = new DiffOptions(FullContext: true);
        var diff = await service.DiffAsync(fixture.Root, "sample.txt", comparison, options: options);
        diff.Options.Test().Be(options); diff.Hunks.Count.Test().Be(1);
        diff.Hunks.Single().Lines.First().Text.Test().Be("source line 001"); diff.Hunks.Single().Lines.Last().Text.Test().Be("source line 220");
        diff.Hunks.Single().Lines.Count(line => line.Kind == DiffLineKind.Added).Test().Be(added);
        diff.Hunks.Single().Lines.Last().NewLine.Test().Be(220);
        SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture.Root, ".git", "index"))).SequenceEqual(hash).Test().BeTrue();
        var narrow = await service.DiffAsync(fixture.Root, "sample.txt", comparison);
        narrow.Patch.Contains("source line 001", StringComparison.Ordinal).Test().BeFalse();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task HistoricalFullContextUsesPinnedObjectsAndSupportsInitialCommits(bool sha256)
    {
        using var fixture = new GitFixture(sha256); var history = new HistoryService(new GitInspector());
        var initial = await history.CommitAsync(fixture.Root, "HEAD"); var options = new DiffOptions(FullContext: true);
        var revisions = new RevisionService(new GitInspector());
        var initialDiff = await revisions.DiffAsync(initial.Comparison(), "sample.txt", options);
        initialDiff.Options.Test().Be(options); initialDiff.Hunks.Single().Lines.Count.Test().Be(3);
        var lines = Lines(); fixture.Write("sample.txt", string.Join('\n', lines) + "\n"); Commit(fixture);
        lines[79] = "historical change"; fixture.Write("sample.txt", string.Join('\n', lines) + "\n"); Commit(fixture);
        var endpoints = await revisions.ResolveAsync(fixture.Root, new("HEAD~1", "HEAD")); File.Delete(Path.Combine(fixture.Root, "sample.txt"));
        var diff = await revisions.DiffAsync(endpoints, "sample.txt", options);
        diff.Revisions.Test().Be(endpoints); diff.Hunks.Single().Lines.First().Text.Test().Be("source line 001");
        diff.Hunks.Single().Lines.Last().Text.Test().Be("source line 220");
        File.Exists(Path.Combine(fixture.Root, "sample.txt")).Test().BeFalse();
    }

    [Test]
    public async Task FullContextStillObeysTheGitOutputLimit()
    {
        using var fixture = new GitFixture();
        var lines = Enumerable.Range(0, 18000).Select(index => index + new string('a', 150)).ToArray();
        fixture.Write("sample.txt", string.Join('\n', lines) + "\n"); Commit(fixture);
        lines[9000] = "changed"; fixture.Write("sample.txt", string.Join('\n', lines) + "\n");
        var inspector = new GitInspector();
        (await inspector.DiffAsync(fixture.Root, "sample.txt", Comparison.WorkingTree)).Hunks.Count.Test().Be(1);
        var failure = await new Func<Task>(() => inspector.DiffAsync(fixture.Root, "sample.txt", Comparison.WorkingTree, options: new(FullContext: true))).Test().ThrowAsync<InspectionException>();
        failure.Which(error => error.Code.Test().Be("output_limit"));
    }

    [Test]
    public async Task AllDiffCliCommandsAcceptFullContextAndAdvertiseIt()
    {
        using var fixture = new GitFixture(); fixture.Write("sample.txt", "changed\n"); Commit(fixture); fixture.Write("sample.txt", "working\n");
        var inspector = new InspectionService(new GitInspector()); var revisions = new RevisionService(new GitInspector()); var history = new HistoryService(new GitInspector());
        foreach (var args in new[] { new[] { "diff", fixture.Root, "sample.txt", "--context", "full" }, ["revision-diff", fixture.Root, "HEAD~1", "HEAD", "sample.txt", "--context", "full"], ["commit-diff", fixture.Root, "HEAD", "sample.txt", "--context", "full"] })
        {
            using var output = new StringWriter(); (await CommandLine.RunAsync(args, output, inspector, revisions: revisions, history: history)).Test().Be(0);
            using var json = JsonDocument.Parse(output.ToString()); json.RootElement.GetProperty("data").GetProperty("options").GetProperty("fullContext").GetBoolean().Test().BeTrue();
        }
        using var capabilities = new StringWriter(); await CommandLine.RunAsync(["capabilities"], capabilities, inspector);
        using var document = JsonDocument.Parse(capabilities.ToString()); document.RootElement.GetProperty("data").GetProperty("diffOptions").GetProperty("context").GetProperty("full").GetBoolean().Test().BeTrue();
    }
}
