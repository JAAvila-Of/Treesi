using System.Text.Json;

namespace Treesi.Desktop.Appearance;

public interface IMotionPreferenceStore
{
    bool Load();
    bool Save(bool reduced);
}

public sealed class JsonMotionPreferenceStore(string? settingsPath = null) : IMotionPreferenceStore
{
    private readonly string _path = settingsPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Treesi", "motion.json");

    public bool Load()
    {
        try
        {
            using var stream = File.OpenRead(_path);
            return stream.Length <= 4096 && JsonSerializer.Deserialize<Preference>(stream)?.Reduced == true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    public bool Save(bool reduced)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Preference(reduced)));
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

    private sealed record Preference(bool Reduced);
}
