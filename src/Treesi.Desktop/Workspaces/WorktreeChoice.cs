using Treesi.Domain;

namespace Treesi.Desktop.Workspaces;

public sealed record WorktreeChoice(RepositoryWorktree Worktree, bool IsCurrent, bool IsOpen)
{
    public string Name => Clean(Path.GetFileName(Path.TrimEndingDirectorySeparator(Worktree.Root)) is { Length: > 0 } name ? name : Worktree.Root);
    public string Location => Clean(Worktree.Root);
    public string Branch => Worktree.IsBare ? "Bare repository" : Worktree.IsDetached ? "Detached HEAD" : Worktree.Branch is { } name ? Clean(name[11..]) : "Unborn branch";
    public string State => string.Join(" · ", new[] { IsCurrent ? "Current" : IsOpen ? "Open tab" : "Registered location · not inspected", Worktree.IsLocked ? "Locked" : null, Worktree.IsPrunable ? "Prunable · unavailable" : null, Worktree.CommitId is null && !Worktree.IsBare ? "Unborn" : null }.Where(item => item is not null));
    public bool CanOpen => !Worktree.IsBare && !Worktree.IsPrunable;
    public string Details => string.Join('\n', new[] { Worktree.CommitId, Worktree.LockReason is { Length: > 0 } locked ? "Lock: " + Clean(locked) : null, Worktree.PruneReason is { Length: > 0 } prune ? "Prunable: " + Clean(prune) : null }.Where(item => item is not null));
    private static string Clean(string value) => HistoryCommitItem.Preview(string.Concat(value.Select(character => char.IsControl(character) ? ' ' : character)), 4096);
}
