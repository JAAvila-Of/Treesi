namespace Treesi.Desktop.Appearance;

public enum GraphicsMode { Automatic, Software }

public interface IGraphicsPreferenceStore
{
    GraphicsMode Load();
    bool Save(GraphicsMode mode);
}

public sealed class GraphicsPreferences
{
    private readonly IGraphicsPreferenceStore _store;
    public GraphicsMode Startup { get; }
    public GraphicsMode Saved { get; private set; }
    public bool RequiresRestart => Saved != Startup;

    public GraphicsPreferences(IGraphicsPreferenceStore store)
    {
        _store = store;
        var mode = store.Load();
        Startup = Saved = Enum.IsDefined(mode) ? mode : GraphicsMode.Automatic;
    }

    public bool UseSoftware(Func<bool?> detectHardware) => Startup == GraphicsMode.Software || detectHardware() == false;

    public bool Save(GraphicsMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!_store.Save(mode)) return false;
        Saved = mode;
        return true;
    }
}
