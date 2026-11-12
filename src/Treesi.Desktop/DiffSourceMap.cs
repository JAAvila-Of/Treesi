using Treesi.Domain;

namespace Treesi.Desktop;

public sealed record DiffSourceAnchor(int? OldLine, int? NewLine, DiffLineKind Kind, int Column, string SelectedText);

public static class DiffSourceMap
{
    public static IReadOnlyDictionary<int, DiffLine> Unified(FileDiff diff)
    {
        var rows = new Dictionary<int, DiffLine>(); var row = 0; var hunk = 0;
        using var reader = new StringReader(diff.Patch);
        while (reader.ReadLine() is { } text)
        {
            row++;
            if (hunk >= diff.Hunks.Count || text != diff.Hunks[hunk].Header) continue;
            foreach (var line in diff.Hunks[hunk++].Lines)
            {
                if (reader.ReadLine() is null) break;
                rows[++row] = line;
            }
        }
        return rows;
    }

    public static DiffSourceAnchor? Capture(IReadOnlyDictionary<int, DiffLine> rows, int row, int column, string selectedText)
    {
        KeyValuePair<int, DiffLine>? nearest = null;
        foreach (var candidate in rows)
        {
            if (candidate.Value.OldLine is null && candidate.Value.NewLine is null) continue;
            if (nearest is null || Math.Abs(candidate.Key - row) < Math.Abs(nearest.Value.Key - row)) nearest = candidate;
        }
        if (nearest is not { Value: var line }) return null;
        return new(line.OldLine, line.NewLine, line.Kind, Math.Max(1, column), selectedText);
    }

    public static int? Locate(IReadOnlyDictionary<int, DiffLine> rows, DiffSourceAnchor anchor)
    {
        int? fallback = null;
        foreach (var (row, line) in rows)
        {
            var sameSource = anchor.NewLine is not null ? line.NewLine == anchor.NewLine : line.OldLine == anchor.OldLine;
            if (!sameSource) continue;
            if (line.Kind == anchor.Kind) return row;
            fallback ??= row;
        }
        return fallback;
    }
}
