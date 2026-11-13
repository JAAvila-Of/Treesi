using Microsoft.Diagnostics.Symbols;

namespace Treesi.NativeProfiler;

internal sealed record ModuleRange(ulong Start, ulong End, string Name, string Path, string? Version);

internal sealed class NativeSymbols : IDisposable
{
    private readonly SymbolReader _reader;
    private readonly Dictionary<string, NativeSymbolModule?> _modules = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selected;
    public NativeSymbols(string cache, IEnumerable<string> selected)
    {
        Directory.CreateDirectory(cache);
        _selected = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _reader = new SymbolReader(TextWriter.Null, $"srv*{cache}*https://msdl.microsoft.com/download/symbols")
        { Options = SymbolReaderOptions.NoNGenSymbolCreation, ServerTimeout = TimeSpan.FromSeconds(5), SecurityCheck = _ => false };
    }

    public string Describe(ulong address, IReadOnlyList<ModuleRange> modules)
    {
        var module = modules.FirstOrDefault(item => address >= item.Start && address < item.End);
        if (module is null) return $"unmapped@0x{address:x}";
        var relative = (uint)(address - module.Start);
        var fallback = $"{module.Name}+0x{relative:x}";
        if (!_selected.Contains(module.Name)) return fallback;
        if (!_modules.TryGetValue(module.Path, out var symbols))
        {
            try { var path = _reader.FindSymbolFilePathForModule(module.Path); symbols = path is null ? null : _reader.OpenNativeSymbolFile(path); }
            catch (Exception error) when (error is not OutOfMemoryException) { symbols = null; }
            _modules[module.Path] = symbols;
        }
        if (symbols is null) return fallback;
        uint start = 0;
        var name = symbols.FindNameForRva(relative, ref start);
        if (!string.IsNullOrEmpty(name) && (start > relative || name.StartsWith("??", StringComparison.Ordinal))) return $"{module.Name}!{name[..Math.Min(name.Length, 512)]} [rva=0x{relative:x}]";
        return string.IsNullOrEmpty(name) ? fallback : $"{module.Name}!{name[..Math.Min(name.Length, 512)]}+0x{relative - start:x}";
    }

    public void Dispose() => _reader.Dispose();
}
