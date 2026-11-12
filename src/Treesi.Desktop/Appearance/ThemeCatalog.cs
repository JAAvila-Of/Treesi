using Avalonia.Styling;

namespace Treesi.Desktop.Appearance;

public sealed record ThemeDefinition(string Id, string Name, ThemeVariant Variant)
{
    public override string ToString() => Name;
}

public static class ThemeCatalog
{
    public static readonly ThemeDefinition Light = new("light", "Light", ThemeVariant.Light);
    public static readonly ThemeDefinition Dark = new("dark", "Dark", ThemeVariant.Dark);
    public static IReadOnlyList<ThemeDefinition> All { get; } = Array.AsReadOnly(new[] { Light, Dark });
    public static ThemeDefinition Resolve(string? id) => All.FirstOrDefault(theme => theme.Id == id) ?? Dark;
}

public interface IThemePreferenceStore
{
    string? Load();
    bool Save(string themeId);
}

public sealed class ThemeController
{
    private readonly Avalonia.Application _application;
    private readonly IThemePreferenceStore _store;
    public ThemeDefinition Current { get; private set; }
    public event Action<ThemeDefinition>? Changed;

    public ThemeController(Avalonia.Application application, IThemePreferenceStore store)
    {
        _application = application;
        _store = store;
        Current = ThemeCatalog.Resolve(store.Load());
        _application.RequestedThemeVariant = Current.Variant;
    }

    public bool Apply(ThemeDefinition theme)
    {
        if (!ThemeCatalog.All.Contains(theme)) throw new ArgumentException("The theme is not registered.", nameof(theme));
        if (theme == Current) return true;
        Current = theme;
        _application.RequestedThemeVariant = theme.Variant;
        Changed?.Invoke(theme);
        return _store.Save(theme.Id);
    }
}
