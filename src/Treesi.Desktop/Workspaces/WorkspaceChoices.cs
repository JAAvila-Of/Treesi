namespace Treesi.Desktop.Workspaces;

public sealed record WorkspaceGroupChoice(Guid? Id, string Name, int Count);

public sealed record WorkspaceRepositoryChoice(string Root, bool IsOpen)
{
    public string Name => Path.GetFileName(Path.TrimEndingDirectorySeparator(Root)) is { Length: > 0 } name ? name : Root;
    public string State => IsOpen ? "Open tab" : "Local shortcut · not inspected";
}
