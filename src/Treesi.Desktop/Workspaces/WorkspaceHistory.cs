using System.Text.Json.Serialization;
using Treesi.Domain;

namespace Treesi.Desktop.Workspaces;

public sealed record WorkspaceEntry(string Root, string? SelectedPath = null,
    Comparison Comparison = Comparison.WorkingTree, bool Split = false, bool FilesVisible = true)
{
    [JsonIgnore]
    public string Name => Path.GetFileName(Path.TrimEndingDirectorySeparator(Root)) is { Length: > 0 } name ? name : Root;
    [JsonIgnore]
    public string View => $"{Comparison switch { Comparison.Staged => "Index vs HEAD", Comparison.Head => "Working tree vs HEAD", _ => "Working tree vs Index" }} · {(Split ? "Side by side" : "Unified")}";
}

public interface IWorkspaceHistoryStore
{
    IReadOnlyList<WorkspaceEntry> Load();
    bool Save(IReadOnlyList<WorkspaceEntry> entries);
}

public sealed class WorkspaceHistory(IWorkspaceHistoryStore? store)
{
    public const int Limit = 8;
    private static readonly StringComparer RootComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private IReadOnlyList<WorkspaceEntry> _entries = Normalize(store?.Load() ?? []);
    public IReadOnlyList<WorkspaceEntry> Entries => _entries;

    public bool Remember(WorkspaceEntry entry)
    {
        _entries = Normalize([entry, .. _entries.Where(item => !RootComparer.Equals(item.Root, entry.Root))]);
        return Persist();
    }

    public bool Update(WorkspaceEntry entry)
    {
        if (!_entries.Any(item => RootComparer.Equals(item.Root, entry.Root))) return true;
        _entries = Normalize(_entries.Select(item => RootComparer.Equals(item.Root, entry.Root) ? entry : item));
        return Persist();
    }

    public bool Forget(string root)
    {
        _entries = _entries.Where(item => !RootComparer.Equals(item.Root, root)).ToArray();
        return Persist();
    }

    public bool Clear() { _entries = []; return Persist(); }
    private bool Persist() => store?.Save(_entries) ?? true;

    public static IReadOnlyList<WorkspaceEntry> Normalize(IEnumerable<WorkspaceEntry> entries)
    {
        var result = new List<WorkspaceEntry>();
        foreach (var entry in entries)
        {
            if (entry is null || !ValidText(entry.Root) || !Path.IsPathFullyQualified(entry.Root) || !Enum.IsDefined(entry.Comparison)) continue;
            if (entry.SelectedPath is { } selected && (!ValidText(selected) || Path.IsPathRooted(selected)
                || selected.Split('/', '\\').Any(part => part is ".." or ""))) continue;
            try
            {
                var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(entry.Root));
                if (!result.Any(item => RootComparer.Equals(item.Root, root))) result.Add(entry with { Root = root });
                if (result.Count == Limit) break;
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        return result.ToArray();
    }

    private static bool ValidText(string? value) => value is { Length: > 0 and <= 4096 } && value.IndexOfAny(['\0', '\r', '\n']) < 0;
}
