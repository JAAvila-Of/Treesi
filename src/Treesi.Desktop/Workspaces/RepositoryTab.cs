using Treesi.Domain;

namespace Treesi.Desktop.Workspaces;

public sealed class RepositoryTab(string root)
{
    public string Root { get; } = root;
    public string Name => Path.GetFileName(Path.TrimEndingDirectorySeparator(Root)) is { Length: > 0 } name ? name : Root;
    public string CloseLabel => $"Close repository tab: {Name}";
    internal RepositorySessionState? State { get; set; }
}

internal sealed record RepositorySessionState
{
    public required RepositorySnapshot Snapshot { get; init; }
    public required IReadOnlyList<FileChange> Files { get; init; }
    public string? SelectedPath { get; init; }
    public int Comparison { get; init; }
    public int Layout { get; init; }
    public required DiffOptions Options { get; init; }
    public required DiffViewOptions ViewOptions { get; init; }
    public FileDiff? Diff { get; init; }
    public RevisionComparison? Revisions { get; init; }
    public string? WorkingSelectedPath { get; init; }
    public HistoryPage? History { get; init; }
    public CommitDetail? Commit { get; init; }
    public int Parent { get; init; }
    public int HistoryScope { get; init; }
    public string? HistoryStart { get; init; }
    public string? HistorySelection { get; init; }
    public string? HistoryNotice { get; init; }
    public bool HistoryVisible { get; init; }
    public string? Filter { get; init; }
    public string? Search { get; init; }
    public bool FilesVisible { get; init; }
    public bool FocusMode { get; init; }
    public bool BeforeActive { get; init; }
    public int Block { get; init; }
    public required EditorPosition Unified { get; init; }
    public required EditorPosition Before { get; init; }
    public required EditorPosition After { get; init; }
    public required SessionNotice Notice { get; init; }
    public GraphSession? Graph { get; init; }
}

internal sealed record EditorPosition(int Start, int Length, double Horizontal, double Vertical);
internal sealed record SessionNotice(string? Title, string? Description, string? FileTitle, string? Added, string? Removed);
