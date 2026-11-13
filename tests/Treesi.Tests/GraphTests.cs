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
public sealed class GraphTests
{
    private static IGraphService Service() => new InstrumentedGraphService(new GraphService(new GitInspector()));
    private static string Commit(GitFixture fixture, string name, string message)
    {
        fixture.Write(name, message + "\n"); fixture.Git("add", "--", name);
        fixture.Git("-c", "commit.gpgSign=false", "-c", "core.hooksPath=", "commit", "-m", message);
        return fixture.Git("rev-parse", "HEAD").Trim();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MergeTopologyAndReferencesAreRealWithoutMutatingIndexOrObjects(bool sha256)
    {
        using var fixture = new GitFixture(sha256); var initial = fixture.Git("rev-parse", "HEAD").Trim();
        fixture.Git("branch", "feature"); var main = Commit(fixture, "main.txt", "feat: main");
        fixture.Git("checkout", "feature"); var feature = Commit(fixture, "feature.txt", "feat: feature");
        fixture.Git("checkout", "main");
        fixture.Git("-c", "commit.gpgSign=false", "-c", "core.hooksPath=", "merge", "--no-ff", "feature", "-m", "merge: combine\n\ncommit forged-record\nparent forged-parent");
        var merge = fixture.Git("rev-parse", "HEAD").Trim();
        fixture.Git("-c", "tag.gpgSign=false", "tag", "-a", "v1", "-m", "Release");
        fixture.Git("update-ref", "refs/remotes/origin/feature", feature);
        var index = SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture.Root, ".git", "index")));
        var objects = Directory.GetFiles(Path.Combine(fixture.Root, ".git", "objects"), "*", SearchOption.AllDirectories).Length;
        var page = await Service().ReadAsync(fixture.Root, new());
        page.Commits.Count.Test().Be(4); page.HasMore.Test().BeFalse(); page.IsShallow.Test().BeFalse();
        page.Commits[0].CommitId.Test().Be(merge); page.Commits[0].Subject.Test().Be("merge: combine");
        page.Commits[0].Parents.SequenceEqual([main, feature]).Test().BeTrue();
        page.Commits.Last().CommitId.Test().Be(initial); page.Commits.Last().Parents.Count.Test().Be(0);
        page.Scope.References.Any(item => item.Name == "refs/tags/v1" && item.CommitId == merge && item.Kind == "tag").Test().BeTrue();
        page.Scope.References.Any(item => item.Name == "refs/remotes/origin/feature" && item.CommitId == feature).Test().BeTrue();
        SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture.Root, ".git", "index"))).SequenceEqual(index).Test().BeTrue();
        Directory.GetFiles(Path.Combine(fixture.Root, ".git", "objects"), "*", SearchOption.AllDirectories).Length.Test().Be(objects);
    }

    [Test]
    public async Task ExpandingPinnedTipsKeepsPrefixWhenBranchesMove()
    {
        using var fixture = new GitFixture();
        for (var i = 0; i < 5; i++) Commit(fixture, "sample.txt", "feat: change " + i);
        var service = Service(); var first = await service.ReadAsync(fixture.Root, new(Limit: 2));
        first.HasMore.Test().BeTrue(); var newest = Commit(fixture, "sample.txt", "feat: newest");
        var expanded = await service.ReadAsync(fixture.Root, new(Limit: 4, Scope: first.Scope));
        expanded.Scope.Test().Be(first.Scope);
        expanded.Commits.Take(2).Select(item => item.CommitId).SequenceEqual(first.Commits.Select(item => item.CommitId)).Test().BeTrue();
        expanded.Commits.Any(item => item.CommitId == newest).Test().BeFalse();
        var refreshed = await service.ReadAsync(fixture.Root, new(Limit: 2)); refreshed.Commits[0].CommitId.Test().Be(newest);
    }

    [Test]
    public async Task ShallowBoundaryRetainsItsTrueParentAndUnbornRepositoryIsEmpty()
    {
        using var fixture = new GitFixture(); var parent = fixture.Git("rev-parse", "HEAD").Trim();
        var head = Commit(fixture, "sample.txt", "fix: boundary");
        File.WriteAllText(Path.Combine(fixture.Root, ".git", "shallow"), head + "\n");
        var page = await Service().ReadAsync(fixture.Root, new());
        page.IsShallow.Test().BeTrue(); page.Commits.Count.Test().Be(1); page.Commits[0].Parents.Single().Test().Be(parent);
        File.Delete(Path.Combine(fixture.Root, ".git", "shallow"));
        fixture.Git("checkout", "--orphan", "empty"); fixture.Git("update-ref", "-d", "refs/heads/main");
        var empty = await Service().ReadAsync(fixture.Root, new()); empty.Commits.Count.Test().Be(0); empty.Scope.Tips.Count.Test().Be(0);
    }

    [Test]
    public async Task CliExposesBoundedGraphAndRejectsAmbiguousFlags()
    {
        using var fixture = new GitFixture(); var git = new GitInspector(); var graph = Service();
        using var output = new StringWriter();
        (await CommandLine.RunAsync(["graph", fixture.Root, "--start", "HEAD", "--limit", "1"], output, new InspectionService(git), graphs: graph)).Test().Be(0);
        using var json = JsonDocument.Parse(output.ToString());
        json.RootElement.GetProperty("data").GetProperty("commits").GetArrayLength().Test().Be(1);
        json.RootElement.GetProperty("contentTrust").GetString().Test().Be("untrusted");
        foreach (var flags in new[] { new[] { "--limit", "1001" }, new[] { "--start", "--all" }, new[] { "--limit", "1", "--limit", "2" } })
        {
            using var failure = new StringWriter();
            (await CommandLine.RunAsync(["graph", fixture.Root, .. flags], failure, new InspectionService(git), graphs: graph) != 0).Test().BeTrue();
        }
    }

    [Test]
    public async Task ProviderCannotForgeTopologyOrSwitchPinnedTips()
    {
        var root = Path.GetFullPath("."); var child = new string('a', 40); var parent = new string('b', 40);
        var scope = new GraphScope(root, [child], [new("HEAD", child, "head")]);
        var commits = new[] { new GraphCommit(parent, [], "root", "Example", "2026-01-01"), new GraphCommit(child, [parent], "child", "Example", "2026-01-01") };
        var service = new GraphService(new Reader(new(scope, commits, 200, false, false)));
        await new Func<Task>(() => service.ReadAsync(root, new())).Test().ThrowAsync<InspectionException>();
        service = new GraphService(new Reader(new(scope with { Tips = [parent] }, commits.Reverse().ToArray(), 200, false, false)));
        await new Func<Task>(() => service.ReadAsync(root, new(Scope: scope))).Test().ThrowAsync<InspectionException>();
    }

    private sealed class Reader(CommitGraphPage page) : IGraphReader
    {
        public Task<CommitGraphPage> ReadGraphAsync(string repository, GraphQuery query, CancellationToken cancellationToken = default) => Task.FromResult(page);
    }
}
