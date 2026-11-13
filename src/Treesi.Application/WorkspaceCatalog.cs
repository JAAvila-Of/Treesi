using Treesi.Domain;

namespace Treesi.Application;

public sealed record WorkspaceCatalogDocument(IReadOnlyList<LocalWorkspace> Workspaces, Guid? SelectedWorkspaceId = null)
{
    public static WorkspaceCatalogDocument Empty { get; } = new([]);
}

public sealed record WorkspaceCatalogLoad(WorkspaceCatalogDocument Document, bool CanSave = true, string? Notice = null);

public interface IWorkspaceCatalogStore
{
    WorkspaceCatalogLoad Load();
    bool Save(WorkspaceCatalogDocument document);
}

public sealed class WorkspaceCatalog
{
    public const int MaximumWorkspaces = 16;
    public const int MaximumRepositories = 32;
    public const int MaximumNameLength = 64;
    public const int MaximumRootLength = 4096;
    public static StringComparer RootComparer { get; } = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly IWorkspaceCatalogStore? _store;
    private readonly bool _canSave;
    public WorkspaceCatalogDocument Document { get; private set; }
    public bool SessionOnly { get; private set; }
    public string? LoadNotice { get; }

    public WorkspaceCatalog(IWorkspaceCatalogStore? store = null)
    {
        _store = store;
        var loaded = store?.Load() ?? new(WorkspaceCatalogDocument.Empty);
        Document = Normalize(loaded.Document);
        _canSave = loaded.CanSave;
        LoadNotice = loaded.Notice;
        SessionOnly = store is null || !loaded.CanSave;
    }

    public LocalWorkspace Create(string name)
    {
        if (Document.Workspaces.Count == MaximumWorkspaces) throw Error("Sixteen workspaces are already defined. Remove a group before creating another.");
        name = ValidName(name);
        UniqueName(name);
        var workspace = new LocalWorkspace(Guid.NewGuid(), name, []);
        Commit(new([.. Document.Workspaces, workspace], workspace.Id));
        return Document.Workspaces.Single(item => item.Id == workspace.Id);
    }

    public void Select(Guid? id)
    {
        if (id is { } selected) Find(selected);
        if (Document.SelectedWorkspaceId != id) Commit(Document with { SelectedWorkspaceId = id });
    }

    public void Rename(Guid id, string name)
    {
        Find(id); name = ValidName(name); UniqueName(name, id);
        Commit(Document with { Workspaces = Document.Workspaces.Select(item => item.Id == id ? item with { Name = name } : item).ToArray() });
    }

    public void Remove(Guid id)
    {
        Find(id);
        Commit(new(Document.Workspaces.Where(item => item.Id != id).ToArray(), Document.SelectedWorkspaceId == id ? null : Document.SelectedWorkspaceId));
    }

    public bool AddRepository(Guid id, string root)
    {
        var workspace = Find(id); root = NormalizeRoot(root);
        if (workspace.RepositoryRoots.Contains(root, RootComparer)) return false;
        if (workspace.RepositoryRoots.Count == MaximumRepositories) throw Error("This workspace already has 32 repositories. Remove a shortcut before adding another.");
        Replace(workspace with { RepositoryRoots = [.. workspace.RepositoryRoots, root] });
        return true;
    }

    public void RemoveRepository(Guid id, string root)
    {
        var workspace = Find(id); root = NormalizeRoot(root);
        Replace(workspace with { RepositoryRoots = workspace.RepositoryRoots.Where(item => !RootComparer.Equals(item, root)).ToArray() });
    }

    public void ReplaceRepository(Guid id, string previousRoot, string newRoot)
    {
        var workspace = Find(id); previousRoot = NormalizeRoot(previousRoot); newRoot = NormalizeRoot(newRoot);
        if (!workspace.RepositoryRoots.Contains(previousRoot, RootComparer)) throw Error("The selected shortcut is no longer in this workspace.");
        if (!RootComparer.Equals(previousRoot, newRoot) && workspace.RepositoryRoots.Contains(newRoot, RootComparer)) throw Error("The replacement repository is already in this workspace.");
        Replace(workspace with { RepositoryRoots = workspace.RepositoryRoots.Select(item => RootComparer.Equals(item, previousRoot) ? newRoot : item).ToArray() });
    }

    private LocalWorkspace Find(Guid id) => Document.Workspaces.FirstOrDefault(item => item.Id == id) ?? throw Error("The selected workspace is no longer available.");
    private void Replace(LocalWorkspace workspace) => Commit(Document with { Workspaces = Document.Workspaces.Select(item => item.Id == workspace.Id ? workspace : item).ToArray() });
    private void UniqueName(string name, Guid? except = null)
    {
        if (Document.Workspaces.Any(item => item.Id != except && StringComparer.OrdinalIgnoreCase.Equals(item.Name, name))) throw Error("A workspace with this name already exists.");
    }

    private void Commit(WorkspaceCatalogDocument document)
    {
        Document = Normalize(document);
        SessionOnly = !_canSave || _store?.Save(Document) != true;
    }

    public static WorkspaceCatalogDocument Normalize(WorkspaceCatalogDocument document)
    {
        if (document.Workspaces is null || document.Workspaces.Count > MaximumWorkspaces) throw Error("The workspace catalog exceeds its group limit.");
        var groups = new List<LocalWorkspace>();
        foreach (var workspace in document.Workspaces)
        {
            if (workspace is null || workspace.Id == Guid.Empty || groups.Any(item => item.Id == workspace.Id)) throw Error("The workspace catalog contains an invalid identity.");
            var name = ValidName(workspace.Name);
            if (groups.Any(item => StringComparer.OrdinalIgnoreCase.Equals(item.Name, name))) throw Error("The workspace catalog contains duplicate names.");
            if (workspace.RepositoryRoots is null || workspace.RepositoryRoots.Count > MaximumRepositories) throw Error("A workspace exceeds its repository limit.");
            var roots = workspace.RepositoryRoots.Select(NormalizeRoot).ToArray();
            if (roots.Distinct(RootComparer).Count() != roots.Length) throw Error("A workspace contains duplicate roots.");
            groups.Add(new(workspace.Id, name, Array.AsReadOnly(roots)));
        }
        if (document.SelectedWorkspaceId is { } selected && !groups.Any(item => item.Id == selected)) throw Error("The selected workspace is not in the catalog.");
        return new(groups.AsReadOnly(), document.SelectedWorkspaceId);
    }

    public static string NormalizeRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || root.Length > MaximumRootLength || root.Any(char.IsControl) || !Path.IsPathFullyQualified(root)) throw Error("Choose an absolute local repository path without control characters.");
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { throw Error("The repository path is not supported."); }
    }

    private static string ValidName(string name)
    {
        if (name is null || name.Any(char.IsControl) || name.Trim() is not { Length: > 0 and <= MaximumNameLength } trimmed) throw Error("Choose a workspace name from 1 to 64 characters without control characters.");
        if (StringComparer.OrdinalIgnoreCase.Equals(trimmed, "Recent repositories")) throw Error("Choose a name other than Recent repositories.");
        return trimmed;
    }

    private static InspectionException Error(string message) => new("invalid_workspace", message);
}
