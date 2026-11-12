using Treesi.Domain;

namespace Treesi.Desktop;

public sealed record ChangedFileItem(FileChange Change, bool IsRevision = false)
{
    public string Path => Change.Path;
    public string Name => System.IO.Path.GetFileName(Change.Path);
    public string Directory => System.IO.Path.GetDirectoryName(Change.Path)?.Replace('\\', '/') is { Length: > 0 } directory ? directory : "Repository root";
    public bool IsAdded => Change.IsUntracked || Change.IndexStatus is "A" || Change.WorkingTreeStatus is "A";
    public bool IsRemoved => Change.IndexStatus is "D" || Change.WorkingTreeStatus is "D";
    public string Status => IsAdded ? "+" : Change.IndexStatus is "U" || Change.WorkingTreeStatus is "U" ? "!" :
        IsRemoved ? "−" : Change.OriginalPath is not null || Change.IndexStatus is "R" || Change.WorkingTreeStatus is "R" ? "R" : "M";
    public string State => IsRevision ? Change.WorkingTreeStatus switch { "A" => "Added between commits", "D" => "Deleted between commits", "T" => "Type changed between commits", _ => "Modified between commits" } : Change.IsUntracked ? "Untracked" : Change.IndexStatus != "." && Change.WorkingTreeStatus != "." ? "Staged + working" :
        Change.IndexStatus != "." ? "Staged" : Change.WorkingTreeStatus != "." ? "Working tree" : "Unchanged";
}
