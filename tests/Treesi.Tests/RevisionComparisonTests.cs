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
public sealed class RevisionComparisonTests
{
    private static IRevisionService Service() => new InstrumentedRevisionService(new RevisionService(new GitInspector()));
    private static void Commit(GitFixture fixture)
    {
        fixture.Git("add", "--", ".");
        fixture.Git("-c", "commit.gpgSign=false", "-c", "core.hooksPath=", "commit", "-m", "test: advance revision fixture");
    }

    [Test]
    public async Task Sha256RepositoriesKeepCompleteObjectIdsThroughResolutionAndDiff()
    {
        using var fixture = new GitFixture(sha256: true);
        fixture.Write("sample.txt", "historical sha256 change\n"); Commit(fixture);
        var service = Service(); var endpoints = await service.ResolveAsync(fixture.Root, new("HEAD~1", "HEAD"));
        endpoints.BaseCommitId.Length.Test().Be(64); endpoints.TargetCommitId.Length.Test().Be(64);
        var references = await service.ReferencesAsync(fixture.Root); references.Items.First().ObjectId.Length.Test().Be(64);
        var diff = await service.DiffAsync(endpoints, "sample.txt");
        diff.Revisions.Test().Be(endpoints); diff.Hunks.Count.Test().BePositive();
    }

    [Test]
    public async Task ProviderResponsesCannotSilentlyChangeResolvedScopeOrOptions()
    {
        var root = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "workspace", "unused");
        var endpoints = new RevisionEndpoints(root, "main", new string('a', 40), "feature", new string('b', 40), new string('a', 40), RevisionComparisonMode.Direct);
        var service = new RevisionService(new WrongScope(endpoints));
        foreach (Func<Task> call in new Func<Task>[] { () => service.ResolveAsync(root, new("main", "feature")), () => service.CompareAsync(endpoints), () => service.DiffAsync(endpoints, "sample.txt") })
        {
            var assertion = await call.Test().ThrowAsync<InspectionException>(); assertion.Which(error => error.Code.Test().Be("invalid_revision_comparison"));
        }
    }

    [Test]
    public async Task FileDiffUsesPinnedCommitsEvenAfterTheRefAndWorkingFileChange()
    {
        using var fixture = new GitFixture();
        var before = fixture.Git("rev-parse", "HEAD").Trim();
        fixture.Write("sample.txt", "first\nhistorical\nlast\n"); Commit(fixture);
        fixture.Git("tag", "-a", "-m", "local tag", "v1");
        var service = Service(); var endpoints = await service.ResolveAsync(fixture.Root, new(before, "v1"));
        var expected = fixture.Git("diff", "--no-ext-diff", "--no-textconv", "--no-color", "--no-renames", "--unified=3", before, endpoints.TargetCommitId, "--", "sample.txt");
        fixture.Write("sample.txt", "unrelated working data\n"); Commit(fixture);
        fixture.Git("tag", "-f", "v1"); File.Delete(Path.Combine(fixture.Root, "sample.txt"));
        fixture.Git("replace", endpoints.TargetCommitId, "HEAD");
        var index = SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture.Root, ".git", "index")));
        var result = await service.DiffAsync(endpoints, "sample.txt");
        result.Patch.Test().Be(expected); result.Comparison.Test().Be(Comparison.Revisions);
        result.Revisions.Test().Be(endpoints); result.Options.Test().Be(DiffOptions.Default);
        result.Patch.Contains("unrelated", StringComparison.Ordinal).Test().BeFalse();
        SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture.Root, ".git", "index"))).SequenceEqual(index).Test().BeTrue();
        var listing = await service.CompareAsync(endpoints);
        listing.Files.Single().Test().Be(new RevisionFileChange("sample.txt", "M"));
        File.Exists(Path.Combine(fixture.Root, "sample.txt")).Test().BeFalse();
    }

    [Test]
    public async Task DirectMergeBaseAndSwappedComparisonsHaveExplicitDifferentDirections()
    {
        using var fixture = new GitFixture(); var ancestor = fixture.Git("rev-parse", "HEAD").Trim();
        fixture.Git("branch", "feature"); fixture.Write("main.txt", "main change\n"); Commit(fixture);
        fixture.Git("checkout", "feature"); fixture.Write("feature.txt", "feature change\n"); Commit(fixture);
        var service = Service();
        var direct = await service.ResolveAsync(fixture.Root, new("main", "feature"));
        var common = await service.ResolveAsync(fixture.Root, new("main", "feature", RevisionComparisonMode.MergeBase));
        direct.EffectiveBaseCommitId.Test().Be(direct.BaseCommitId); common.EffectiveBaseCommitId.Test().Be(ancestor);
        var files = await service.CompareAsync(direct); files.Files.Count.Test().Be(2);
        var changes = await service.CompareAsync(common); changes.Files.Single().Test().Be(new RevisionFileChange("feature.txt", "A"));
        var swapped = await service.ResolveAsync(fixture.Root, new("feature", "main"));
        var reverse = await service.CompareAsync(swapped);
        reverse.Files.Single(item => item.Path == "feature.txt").Status.Test().Be("D");
        var deleted = await service.DiffAsync(swapped, "feature.txt", new(0));
        deleted.Hunks.Single().Lines.Single().Kind.Test().Be(DiffLineKind.Removed);
        var diff = await service.DiffAsync(common, "feature.txt", new(0));
        diff.Hunks.Single().Lines.Single().Kind.Test().Be(DiffLineKind.Added);
    }

    [Test]
    public async Task MissingOrNonCommitRevisionsAndUnrelatedHistoryHaveExplicitErrors()
    {
        using var fixture = new GitFixture(); var service = Service();
        foreach (var target in new[] { "missing-ref", "HEAD:sample.txt" })
        {
            Func<Task> action = () => service.ResolveAsync(fixture.Root, new("HEAD", target));
            await action.Test().ThrowAsync<InspectionException>();
        }
        var blob = fixture.Git("rev-parse", "HEAD:sample.txt").Trim(); fixture.Git("tag", "blob-tag", blob);
        Func<Task> notCommit = () => service.ResolveAsync(fixture.Root, new("HEAD", "blob-tag"));
        var nonCommit = await notCommit.Test().ThrowAsync<InspectionException>(); nonCommit.Which(error => error.Code.Test().Be("revision_not_found"));
        fixture.Git("checkout", "--orphan", "unrelated"); fixture.Write("sample.txt", "different root\n"); Commit(fixture);
        Func<Task> noBase = () => service.ResolveAsync(fixture.Root, new("main", "unrelated", RevisionComparisonMode.MergeBase));
        var missingBase = await noBase.Test().ThrowAsync<InspectionException>(); missingBase.Which(error => error.Code.Test().Be("no_merge_base"));
    }

    [Test]
    public async Task HistoricalDirectoriesAndMissingFilesAreNotReportedAsSuccessfulEmptyDiffs()
    {
        using var fixture = new GitFixture(); Directory.CreateDirectory(Path.Combine(fixture.Root, "src"));
        fixture.Write("src/[a].txt", "before\n"); Commit(fixture);
        fixture.Write("src/[a].txt", "after\n"); Commit(fixture);
        var service = Service(); var endpoints = await service.ResolveAsync(fixture.Root, new("HEAD~1", "HEAD"));
        var diff = await service.DiffAsync(endpoints, "src/[a].txt"); diff.Hunks.Count.Test().Be(1);
        foreach (var path in new[] { "src", "missing.txt", "../escape", ".", "src\\[a].txt" })
        {
            Func<Task> action = () => service.DiffAsync(endpoints, path);
            await action.Test().ThrowAsync<InspectionException>();
        }
    }

    [Test]
    public async Task MultipleMergeBasesAreRejectedInsteadOfChoosingOneSilently()
    {
        using var fixture = new GitFixture(); var root = fixture.Git("rev-parse", "HEAD").Trim(); var tree = fixture.Git("rev-parse", "HEAD^{tree}").Trim();
        var left = fixture.Git("-c", "commit.gpgSign=false", "commit-tree", tree, "-p", root, "-m", "left").Trim();
        var right = fixture.Git("-c", "commit.gpgSign=false", "commit-tree", tree, "-p", root, "-m", "right").Trim();
        var first = fixture.Git("-c", "commit.gpgSign=false", "commit-tree", tree, "-p", left, "-p", right, "-m", "first merge").Trim();
        var second = fixture.Git("-c", "commit.gpgSign=false", "commit-tree", tree, "-p", right, "-p", left, "-m", "second merge").Trim();
        Func<Task> compare = () => Service().ResolveAsync(fixture.Root, new(first, second, RevisionComparisonMode.MergeBase));
        var assertion = await compare.Test().ThrowAsync<InspectionException>(); assertion.Which(error => error.Code.Test().Be("ambiguous_merge_base"));
    }

    [Test]
    public async Task MissingHistoricalObjectsFailExplicitlyWithoutReturningPartialData()
    {
        using var fixture = new GitFixture(); fixture.Write("sample.txt", "historical blob\n"); Commit(fixture);
        var service = Service(); var endpoints = await service.ResolveAsync(fixture.Root, new("HEAD~1", "HEAD"));
        var blob = fixture.Git("rev-parse", "HEAD:sample.txt").Trim();
        var stored = Path.Combine(fixture.Root, ".git", "objects", blob[..2], blob[2..]);
        File.SetAttributes(stored, FileAttributes.Normal); File.Delete(stored);
        Func<Task> read = () => service.DiffAsync(endpoints, "sample.txt");
        var assertion = await read.Test().ThrowAsync<InspectionException>(); assertion.Which(error => error.Code.Test().Be("revision_unavailable"));
    }

    [Test]
    public async Task ResolvedValidationAndCancellationKeepInvalidRequestsOutOfTheProvider()
    {
        var reader = new Recorder(); var service = new RevisionService(reader);
        var root = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "workspace", "unused");
        var endpoints = new RevisionEndpoints(root, "main", new string('a', 40), "feature", new string('b', 40), new string('a', 40), RevisionComparisonMode.Direct);
        foreach (var invalid in new[] { endpoints with { BaseCommitId = "--exec" }, endpoints with { EffectiveBaseCommitId = new string('c', 40) }, endpoints with { Root = "relative" } })
        {
            Func<Task> call = () => service.CompareAsync(invalid); await call.Test().ThrowAsync<InspectionException>();
        }
        Func<Task> escape = () => service.DiffAsync(endpoints, "../escape"); await escape.Test().ThrowAsync<InspectionException>();
        reader.Calls.Test().Be(0);
        using var output = new StringWriter();
        (await CommandLine.RunAsync(["compare", "unused", "HEAD", "HEAD"], output, new InspectionService(new GitInspector()), new CancellationToken(true), service)).Test().Be(130);
        reader.Calls.Test().Be(0);
    }

    [Test]
    public async Task ReferencesIncludeLocalBranchesTagsAndAnExplicitTruncationFlag()
    {
        using var fixture = new GitFixture(); fixture.Git("tag", "v1");
        var service = Service(); var references = await service.ReferencesAsync(fixture.Root);
        references.Items.Any(item => item.Name == "HEAD").Test().BeTrue();
        references.Items.Any(item => item.Name == "refs/heads/main").Test().BeTrue();
        references.Items.Any(item => item.Name == "refs/tags/v1").Test().BeTrue(); references.HasMore.Test().BeFalse();
        var id = fixture.Git("rev-parse", "HEAD").Trim(); var directory = Path.Combine(fixture.Root, ".git", "refs", "tags");
        for (var index = 0; index < 205; index++) File.WriteAllText(Path.Combine(directory, $"sample-{index:D3}"), id + "\n");
        var limited = await service.ReferencesAsync(fixture.Root);
        limited.Items.Count.Test().Be(RevisionLimits.References); limited.HasMore.Test().BeTrue();
    }

    [Test]
    public async Task RevisionCliReportsResolvedEndpointsAndEffectiveOptions()
    {
        using var fixture = new GitFixture(); fixture.Write("sample.txt", "first\nhistorical\nlast\n"); Commit(fixture);
        var inspector = new InspectionService(new GitInspector()); var service = Service();
        using var output = new StringWriter();
        (await CommandLine.RunAsync(["compare", fixture.Root, "HEAD~1", "HEAD"], output, inspector, revisions: service)).Test().Be(0);
        using var json = JsonDocument.Parse(output.ToString());
        var endpoints = json.RootElement.GetProperty("data").GetProperty("endpoints");
        RevisionEndpoints.IsCommitId(endpoints.GetProperty("targetCommitId").GetString()).Test().BeTrue();
        using var diff = new StringWriter();
        (await CommandLine.RunAsync(["revision-diff", fixture.Root, "HEAD~1", "HEAD", "sample.txt", "--context", "0", "--mode", "direct"], diff, inspector, revisions: service)).Test().Be(0);
        using var result = JsonDocument.Parse(diff.ToString());
        var data = result.RootElement.GetProperty("data"); data.GetProperty("comparison").GetString().Test().Be("revisions");
        data.GetProperty("revisions").GetProperty("effectiveBaseCommitId").GetString().Test().Be(endpoints.GetProperty("baseCommitId").GetString());
        data.GetProperty("options").GetProperty("contextLines").GetInt32().Test().Be(0);
    }

    [TestCase("--exec")]
    [TestCase("HEAD..main")]
    [TestCase("HEAD@{1}")]
    [TestCase("HEAD~1000")]
    [TestCase("HEAD:sample.txt")]
    public async Task InvalidSelectionsNeverReachTheReader(string revision)
    {
        var reader = new Recorder(); var service = new RevisionService(reader);
        Func<Task> action = () => service.ResolveAsync("unused", new(revision, "HEAD"));
        var invalid = await action.Test().ThrowAsync<InspectionException>(); invalid.Which(error => error.Code.Test().Be("invalid_revision"));
        reader.Calls.Test().Be(0);
    }

    private sealed class WrongScope(RevisionEndpoints endpoints) : IRevisionReader
    {
        public Task<RevisionReferences> ReferencesAsync(string repository, CancellationToken cancellationToken = default) => Task.FromResult(new RevisionReferences([], false));
        public Task<RevisionEndpoints> ResolveAsync(string repository, RevisionSelection selection, CancellationToken cancellationToken = default) => Task.FromResult(endpoints with { BaseName = "different" });
        public Task<RevisionComparison> CompareAsync(RevisionEndpoints expected, CancellationToken cancellationToken = default) => Task.FromResult(new RevisionComparison(endpoints with { TargetCommitId = new string('c', 40) }, []));
        public Task<FileDiff> DiffAsync(RevisionEndpoints expected, string path, DiffOptions? options = null, CancellationToken cancellationToken = default) => Task.FromResult(new FileDiff(path, Comparison.Revisions, "", false, [], Options: new(0), Revisions: endpoints));
    }

    private sealed class Recorder : IRevisionReader
    {
        public int Calls { get; private set; }
        public Task<RevisionReferences> ReferencesAsync(string repository, CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
        public Task<RevisionEndpoints> ResolveAsync(string repository, RevisionSelection selection, CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
        public Task<RevisionComparison> CompareAsync(RevisionEndpoints endpoints, CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
        public Task<FileDiff> DiffAsync(RevisionEndpoints endpoints, string path, DiffOptions? options = null, CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
    }
}
