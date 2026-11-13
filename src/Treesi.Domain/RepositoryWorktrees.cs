namespace Treesi.Domain;

public sealed record RepositoryWorktree(string Root, string? CommitId, string? Branch, bool IsBare = false,
    bool IsDetached = false, bool IsLocked = false, string? LockReason = null, bool IsPrunable = false, string? PruneReason = null);
public sealed record RepositoryWorktrees(string Root, IReadOnlyList<RepositoryWorktree> Items, string ContentTrust = "untrusted")
{
    public const int MaximumEntries = 128;
    public const int MaximumPathLength = 4096;
}
