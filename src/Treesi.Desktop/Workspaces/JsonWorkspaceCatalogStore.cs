using System.Text.Json;
using Treesi.Application;
using Treesi.Domain;

namespace Treesi.Desktop.Workspaces;

public sealed class JsonWorkspaceCatalogStore(string? settingsPath = null) : IWorkspaceCatalogStore
{
    public const int MaximumBytes = 262144;
    private readonly string _path = settingsPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Treesi", "workspace-groups.json");

    public WorkspaceCatalogLoad Load()
    {
        try
        {
            using var stream = File.OpenRead(_path);
            if (stream.Length > MaximumBytes) return Unavailable();
            var bytes = new byte[MaximumBytes + 1];
            var length = 0;
            while (length < bytes.Length)
            {
                var read = stream.Read(bytes.AsSpan(length));
                if (read == 0) break;
                length += read;
            }
            if (length > MaximumBytes) return Unavailable();
            var stored = JsonSerializer.Deserialize<StoredCatalog>(bytes.AsSpan(0, length));
            if (stored is not { Version: 1, Catalog: not null }) return Unavailable();
            return new(WorkspaceCatalog.Normalize(stored.Catalog));
        }
        catch (FileNotFoundException) { return new(WorkspaceCatalogDocument.Empty); }
        catch (DirectoryNotFoundException) { return new(WorkspaceCatalogDocument.Empty); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InspectionException) { return Unavailable(); }
    }

    public bool Save(WorkspaceCatalogDocument document)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new StoredCatalog(1, WorkspaceCatalog.Normalize(document)));
            if (bytes.Length > MaximumBytes) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InspectionException) { return false; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static WorkspaceCatalogLoad Unavailable() => new(WorkspaceCatalogDocument.Empty, false,
        "Workspace settings could not be loaded. Groups are session-only; the existing settings file will not be changed.");

    private sealed record StoredCatalog(int Version, WorkspaceCatalogDocument Catalog);
}
