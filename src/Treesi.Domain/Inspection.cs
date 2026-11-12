namespace Treesi.Domain;

public enum Comparison { WorkingTree, Staged, Head }
public enum DiffLineKind { Context, Added, Removed, Metadata }

public sealed record FileChange(string Path, string? OriginalPath, string IndexStatus, string WorkingTreeStatus)
{
    public bool IsUntracked => IndexStatus == "?";
    public override string ToString() => $"{IndexStatus}{WorkingTreeStatus}   {Path}";
}
public sealed record RepositorySnapshot(string Root, string? SelectedPath, IReadOnlyList<FileChange> Files);
public sealed record DiffLine(DiffLineKind Kind, int? OldLine, int? NewLine, string Text);
public sealed record DiffHunk(string Header, IReadOnlyList<DiffLine> Lines);
public sealed record FileDiff(string Path, Comparison Comparison, string Patch, bool IsBinary,
    IReadOnlyList<DiffHunk> Hunks, string ContentTrust = "untrusted");

public sealed class InspectionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
