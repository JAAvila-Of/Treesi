using Treesi.Domain;

namespace Treesi.Application;

public interface IWorktreeReader
{
    Task<RepositoryWorktrees> ReadWorktreesAsync(string repository, CancellationToken cancellationToken = default);
}
public interface IWorktreeService
{
    Task<RepositoryWorktrees> ListAsync(string repository, CancellationToken cancellationToken = default);
}
public sealed class WorktreeService(IWorktreeReader reader) : IWorktreeService
{
    public async Task<RepositoryWorktrees> ListAsync(string repository, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repository) || repository.Contains('\0')) throw new InspectionException("invalid_path", "Choose a local repository path.");
        var result = await reader.ReadWorktreesAsync(repository, cancellationToken);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (result.ContentTrust != "untrusted" || !ValidPath(result.Root) || result.Items.Count is < 1 or > RepositoryWorktrees.MaximumEntries
            || result.Items.Any(item => !ValidPath(item.Root) || item.CommitId is not null && !RevisionEndpoints.IsCommitId(item.CommitId)
                || item.IsBare && (item.CommitId is not null || item.Branch is not null || item.IsDetached)
                || !item.IsBare && (item.IsDetached ? item.Branch is not null || item.CommitId is null : item.Branch is null)
                || item.Branch is not null && (!item.Branch.StartsWith("refs/heads/", StringComparison.Ordinal) || item.Branch.Length is <= 11 or > 1024 || item.Branch.Any(char.IsControl))
                || !item.IsLocked && item.LockReason is not null || !item.IsPrunable && item.PruneReason is not null
                || item.LockReason?.Length > 4096 || item.PruneReason?.Length > 4096)
            || result.Items.Select(item => Path.GetFullPath(item.Root)).Distinct(comparer).Count() != result.Items.Count
            || !result.Items.Any(item => !item.IsBare && comparer.Equals(Path.GetFullPath(item.Root), Path.GetFullPath(result.Root))))
            throw new InspectionException("invalid_worktrees", "The provider returned unsupported or inconsistent worktree metadata.");
        return result;
    }

    private static bool ValidPath(string path) => path.Length <= RepositoryWorktrees.MaximumPathLength && !path.Contains('\0') && Path.IsPathFullyQualified(path);
}
public sealed class InstrumentedWorktreeService(IWorktreeService inner) : IWorktreeService
{
    public Task<RepositoryWorktrees> ListAsync(string repository, CancellationToken cancellationToken = default) =>
        InstrumentedInspectionService.MeasureAsync("worktrees", () => inner.ListAsync(repository, cancellationToken));
}
