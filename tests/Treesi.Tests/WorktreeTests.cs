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
public sealed class WorktreeTests
{
    private static IWorktreeService Service() => new InstrumentedWorktreeService(new WorktreeService(new GitInspector()));

    [TestCase(false)]
    [TestCase(true)]
    public async Task LinkedWorktreesKeepBranchesDetachedAndLockMetadataWithoutMutation(bool sha256)
    {
        using var fixture = new GitFixture(sha256);
        var linked = Path.Combine(fixture.Root, OperatingSystem.IsWindows() ? "linked space" : "linked\nspace");
        var detached = Path.Combine(fixture.Root, "detached");
        fixture.Git("-c", "core.hooksPath=", "worktree", "add", "-b", "feature/review", linked);
        fixture.Git("-c", "core.hooksPath=", "worktree", "add", "--detach", detached);
        fixture.Git("worktree", "lock", "--reason", "Portable\nlocation", linked);
        var indexPath = Path.Combine(fixture.Root, ".git", "index"); var index = SHA256.HashData(File.ReadAllBytes(indexPath));
        var before = fixture.Git("worktree", "list", "--porcelain", "-z");
        var result = await Service().ListAsync(linked);
        result.Items.Count.Test().Be(3);
        var branch = result.Items.Single(item => Path.GetFullPath(item.Root) == linked);
        branch.Branch.Test().Be("refs/heads/feature/review"); branch.IsLocked.Test().BeTrue(); branch.LockReason.Test().Be("Portable\nlocation");
        result.Items.Single(item => Path.GetFullPath(item.Root) == detached).IsDetached.Test().BeTrue();
        result.Items.All(item => item.CommitId!.Length == (sha256 ? 64 : 40)).Test().BeTrue();
        fixture.Git("worktree", "list", "--porcelain", "-z").Test().Be(before);
        SHA256.HashData(File.ReadAllBytes(indexPath)).SequenceEqual(index).Test().BeTrue();
    }

    [Test]
    public async Task UnbornAndPrunableRecordsStayExplicit()
    {
        using var fixture = new GitFixture(); var missing = Path.Combine(fixture.Root, "missing");
        fixture.Git("-c", "core.hooksPath=", "worktree", "add", "--detach", missing);
        File.WriteAllText(Path.Combine(fixture.Root, ".git", "worktrees", "missing", "gitdir"), Path.Combine(fixture.Root, "not-present", ".git") + "\n");
        var result = await Service().ListAsync(fixture.Root); result.Items.Single(item => item.IsPrunable).PruneReason.Test().NotBeNull();
        fixture.Git("checkout", "--orphan", "unborn"); fixture.Git("update-ref", "-d", "refs/heads/main");
        result = await Service().ListAsync(fixture.Root);
        var current = result.Items.Single(item => Path.GetFullPath(item.Root) == fixture.Root);
        current.CommitId.Test().BeNull(); current.Branch.Test().Be("refs/heads/unborn"); current.IsDetached.Test().BeFalse();
    }

    [Test]
    public async Task LinkedTreeCanReportItsBarePrimaryRepository()
    {
        using var fixture = new GitFixture(); var bare = Path.Combine(fixture.Root, "bare.git"); var linked = Path.Combine(fixture.Root, "bare-linked");
        fixture.Git("clone", "--bare", "--no-hardlinks", fixture.Root, bare);
        fixture.Git("--git-dir=" + bare, "-c", "core.hooksPath=", "worktree", "add", linked);
        var result = await Service().ListAsync(linked); result.Items.Count.Test().Be(2);
        result.Items.Single(item => item.IsBare).CommitId.Test().BeNull();
    }

    [Test]
    public async Task CliReportsCapabilityAndUntrustedWorktrees()
    {
        using var fixture = new GitFixture(); var git = new GitInspector();
        foreach (var command in new[] { new[] { "capabilities" }, new[] { "worktrees", fixture.Root } })
        {
            using var output = new StringWriter();
            (await CommandLine.RunAsync(command, output, new InspectionService(git), worktrees: Service())).Test().Be(0);
            using var json = JsonDocument.Parse(output.ToString()); json.RootElement.GetProperty("contentTrust").GetString().Test().Be("untrusted");
            var data = json.RootElement.GetProperty("data");
            if (command[0] == "capabilities") data.GetProperty("worktrees").GetProperty("maximumEntries").GetInt32().Test().Be(128);
            else data.GetProperty("items").GetArrayLength().Test().Be(1);
        }
    }

    [Test]
    public async Task ProviderCannotReturnDuplicatePathsOrInvalidHeads()
    {
        var root = Path.GetFullPath("."); var item = new RepositoryWorktree(root, new string('a', 40), "refs/heads/main");
        foreach (var items in new[] { new[] { item, item }, new[] { item with { CommitId = "--all" } }, new[] { item with { Root = "relative" } }, new[] { item with { IsDetached = true } }, Enumerable.Repeat(item, 129).ToArray() })
        {
            var service = new WorktreeService(new Reader(new(root, items)));
            await new Func<Task>(() => service.ListAsync(root)).Test().ThrowAsync<InspectionException>();
        }
    }
    private sealed class Reader(RepositoryWorktrees result) : IWorktreeReader
    {
        public Task<RepositoryWorktrees> ReadWorktreesAsync(string repository, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
