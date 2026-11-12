using System.Text.Json;

namespace Treesi.Desktop.Appearance;

public sealed class JsonThemePreferenceStore(string? settingsPath = null) : IThemePreferenceStore
{
    private readonly string _path = settingsPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Treesi", "appearance.json");

    public string? Load()
    {
        try
        {
            using var stream = File.OpenRead(_path);
            if (stream.Length > 4096) return null;
            return JsonSerializer.Deserialize<Preference>(stream)?.Theme;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public bool Save(string themeId)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Preference(themeId)));
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

    private sealed record Preference(string Theme);
}
