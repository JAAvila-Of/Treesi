using System.Text.Json;

namespace Treesi.Desktop.Workspaces;

public sealed class JsonWorkspaceHistoryStore(string? settingsPath = null) : IWorkspaceHistoryStore
{
    public const int MaximumBytes = 65536;
    private readonly string _path = settingsPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Treesi", "workspaces.json");

    public IReadOnlyList<WorkspaceEntry> Load()
    {
        try
        {
            using var stream = File.OpenRead(_path);
            if (stream.Length > MaximumBytes) return [];
            var bytes = new byte[MaximumBytes + 1];
            var length = 0;
            while (length < bytes.Length)
            {
                var read = stream.Read(bytes.AsSpan(length));
                if (read == 0) break;
                length += read;
            }
            if (length > MaximumBytes) return [];
            var document = JsonSerializer.Deserialize<Document>(bytes.AsSpan(0, length));
            return document is { Version: 1, Entries: not null } ? WorkspaceHistory.Normalize(document.Entries) : [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    public bool Save(IReadOnlyList<WorkspaceEntry> entries)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new Document(1, WorkspaceHistory.Normalize(entries)));
            if (bytes.Length > MaximumBytes) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record Document(int Version, IReadOnlyList<WorkspaceEntry> Entries);
}
