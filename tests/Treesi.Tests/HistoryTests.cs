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
public sealed class HistoryTests
{
    private static IHistoryService History() => new InstrumentedHistoryService(new HistoryService(new GitInspector()));
    private static IRevisionService Revisions() => new RevisionService(new GitInspector());
    private static string Commit(GitFixture fixture, string message = "test: advance history")
    {
        fixture.Git("add", "--", ".");
        fixture.Git("-c", "commit.gpgSign=false", "-c", "core.hooksPath=", "commit", "-m", message);
        return fixture.Git("rev-parse", "HEAD").Trim();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PagesStayPinnedWhenHeadMovesAndRootDiffNeverWrites(bool sha256)
    {
        using var fixture = new GitFixture(sha256);
        var initial = fixture.Git("rev-parse", "HEAD").Trim();
        for (var i = 0; i < 3; i++) { fixture.Write("sample.txt", $"history {i}\n"); Commit(fixture); }
        var history = History(); var first = await history.PageAsync(fixture.Root, new(Limit: 2));
        first.Items.Count.Test().Be(2); first.NextOffset.Test().Be(2);
        fixture.Write("sample.txt", "new head\n"); Commit(fixture);
        var second = await history.PageAsync(fixture.Root, new(first.StartCommitId, Offset: first.NextOffset!.Value, Limit: 2));
        second.Items.Last().CommitId.Test().Be(initial); second.NextOffset.Test().BeNull();
        first.Items.Select(item => item.CommitId).Intersect(second.Items.Select(item => item.CommitId)).Any().Test().BeFalse();
        var detail = await history.CommitAsync(fixture.Root, initial); detail.Parents.Count.Test().Be(0);
        var index = SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture.Root, ".git", "index")));
        var objects = Directory.GetFiles(Path.Combine(fixture.Root, ".git", "objects"), "*", SearchOption.AllDirectories).Length;
        var revisions = Revisions(); var endpoints = detail.Comparison(); endpoints.IsRootComparison.Test().BeTrue();
        var files = await revisions.CompareAsync(endpoints); files.Files.Single().Test().Be(new RevisionFileChange("sample.txt", "A"));
        var diff = await revisions.DiffAsync(endpoints, "sample.txt");
        diff.Hunks.SelectMany(hunk => hunk.Lines).Count(line => line.Kind == DiffLineKind.Added).Test().Be(3);
        diff.Revisions.Test().Be(endpoints); diff.Options.Test().Be(DiffOptions.Default);
        Directory.GetFiles(Path.Combine(fixture.Root, ".git", "objects"), "*", SearchOption.AllDirectories).Length.Test().Be(objects);
        SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture.Root, ".git", "index"))).SequenceEqual(index).Test().BeTrue();
    }

    [Test]
    public async Task LiteralFileHistoryIncludesDeletedPathsWithoutFollowingRenames()
    {
        using var fixture = new GitFixture();
        fixture.Write("literal[1].txt", "old\n"); var added = Commit(fixture);
        fixture.Write("literal1.txt", "other\n"); Commit(fixture);
        File.Delete(Path.Combine(fixture.Root, "literal[1].txt")); var deleted = Commit(fixture);
        var page = await History().PageAsync(fixture.Root, new(Path: "literal[1].txt"));
        page.Items.Select(item => item.CommitId).SequenceEqual([deleted, added]).Test().BeTrue();
        var absent = await History().PageAsync(fixture.Root, new(Path: "absent.txt")); absent.Items.Count.Test().Be(0);
    }

    [Test]
    public async Task MergeParentsAndFullMessagesAreAccurateAndEachParentHasItsOwnDiff()
    {
        using var fixture = new GitFixture(); fixture.Git("branch", "feature");
        fixture.Write("main.txt", "main\n"); var main = Commit(fixture);
        fixture.Git("checkout", "feature"); fixture.Write("feature.txt", "feature\n"); var feature = Commit(fixture);
        fixture.Git("checkout", "main");
        fixture.Git("-c", "commit.gpgSign=false", "-c", "core.hooksPath=", "merge", "--no-ff", "feature", "-m", "merge: combine\n\nUntrusted body: <script>example</script>");
        var detail = await History().CommitAsync(fixture.Root, "HEAD");
        detail.Parents.SequenceEqual([main, feature]).Test().BeTrue();
        detail.Message.Contains("<script>example</script>", StringComparison.Ordinal).Test().BeTrue();
        detail.Author.Contains("tests@example.invalid", StringComparison.Ordinal).Test().BeTrue();
        var first = await Revisions().CompareAsync(detail.Comparison(1)); first.Files.Single().Path.Test().Be("feature.txt");
        var second = await Revisions().CompareAsync(detail.Comparison(2)); second.Files.Single().Path.Test().Be("main.txt");
        Action invalid = () => detail.Comparison(3); invalid.Test().Throw<InspectionException>();
        var forged = detail.Comparison(1) with { IsRootComparison = true, BaseCommitId = detail.CommitId, EffectiveBaseCommitId = detail.CommitId };
        var failure = await new Func<Task>(() => Revisions().CompareAsync(forged)).Test().ThrowAsync<InspectionException>();
        failure.Which(error => error.Code.Test().Be("invalid_revision_comparison"));
    }

    [Test]
    public async Task ShallowBoundaryKeepsRealParentsAndFailsWithoutFetching()
    {
        using var fixture = new GitFixture(); var initial = fixture.Git("rev-parse", "HEAD").Trim();
        fixture.Write("sample.txt", "boundary\n"); var head = Commit(fixture);
        File.WriteAllText(Path.Combine(fixture.Root, ".git", "shallow"), head + "\n");
        var page = await History().PageAsync(fixture.Root, new()); page.Items.Count.Test().Be(1);
        var detail = await History().CommitAsync(fixture.Root, head); detail.Parents.Single().Test().Be(initial);
        detail.Comparison().IsRootComparison.Test().BeFalse();
        var objectPath = Path.Combine(fixture.Root, ".git", "objects", initial[..2], initial[2..]);
        File.SetAttributes(objectPath, FileAttributes.Normal); File.Delete(objectPath);
        await new Func<Task>(() => Revisions().CompareAsync(detail.Comparison())).Test().ThrowAsync<InspectionException>();
        File.Exists(objectPath).Test().BeFalse();
    }

    [Test]
    public async Task HistoryValidationRejectsInvalidPagesBeforeProviderAccess()
    {
        var provider = new UnexpectedReader(); var service = new HistoryService(provider);
        foreach (var query in new[] { new HistoryQuery(Offset: 1), new HistoryQuery(Limit: 51), new HistoryQuery(Offset: -1), new HistoryQuery(Path: "../escape"), new HistoryQuery("--all") })
            await new Func<Task>(() => service.PageAsync(Path.GetTempPath(), query)).Test().ThrowAsync<InspectionException>();
        provider.Calls.Test().Be(0);
    }

    [Test]
    public async Task CliExposesHistoryCommitRootDiffAndClosedFlags()
    {
        using var fixture = new GitFixture(); var history = History(); var revisions = Revisions();
        var inspector = new InspectionService(new GitInspector());
        foreach (var args in new[] { new[] { "history", fixture.Root, "--limit", "1" }, ["commit", fixture.Root, "HEAD"], ["commit-files", fixture.Root, "HEAD"], ["commit-diff", fixture.Root, "HEAD", "sample.txt", "--context", "0"] })
        {
            using var output = new StringWriter();
            (await CommandLine.RunAsync(args, output, inspector, revisions: revisions, history: history)).Test().Be(0);
            using var json = JsonDocument.Parse(output.ToString()); json.RootElement.GetProperty("ok").GetBoolean().Test().BeTrue();
        }
        foreach (var flags in new[] { new[] { "--all", "true" }, ["--limit", "1", "--limit", "2"], ["--offset", "-1"] })
        {
            using var output = new StringWriter();
            (await CommandLine.RunAsync(["history", fixture.Root, .. flags], output, inspector, history: history)).Test().Be(2);
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        (await CommandLine.RunAsync(["history", fixture.Root], new StringWriter(), inspector, cancelled.Token, revisions, history)).Test().Be(130);
    }

    private sealed class UnexpectedReader : IHistoryReader
    {
        public int Calls { get; private set; }
        public Task<HistoryPage> PageAsync(string repository, HistoryQuery query, CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
        public Task<CommitDetail> CommitAsync(string repository, string revision, CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
    }
}
