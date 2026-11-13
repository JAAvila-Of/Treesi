using System.Text.Json;

namespace Treesi.Desktop.Appearance;

public sealed class JsonGraphicsPreferenceStore(string? settingsPath = null) : IGraphicsPreferenceStore
{
    private readonly string _path = settingsPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Treesi", "graphics.json");

    public GraphicsMode Load()
    {
        try
        {
            using var stream = File.OpenRead(_path);
            if (stream.Length > 4096) return GraphicsMode.Automatic;
            return JsonSerializer.Deserialize<Preference>(stream)?.Mode == "software" ? GraphicsMode.Software : GraphicsMode.Automatic;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return GraphicsMode.Automatic; }
    }

    public bool Save(GraphicsMode mode)
    {
        if (!Enum.IsDefined(mode)) return false;
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Preference(mode == GraphicsMode.Software ? "software" : "automatic")));
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

    private sealed record Preference(string Mode);
}
