using System.Text;
using Treesi.Domain;

namespace Treesi.Desktop;

public sealed record DiffDocument(string Text, IReadOnlyDictionary<int, DiffLine> Lines, IReadOnlyList<int> HunkLines, IReadOnlyList<int>? ChangeLines = null);
public sealed record SplitDiff(DiffDocument Before, DiffDocument After)
{
    public static SplitDiff Create(FileDiff diff)
    {
        var before = new StringBuilder(); var after = new StringBuilder();
        var oldLines = new Dictionary<int, DiffLine>(); var newLines = new Dictionary<int, DiffLine>();
        var headers = new List<int>();
        var changes = new List<int>();
        var row = 0;
        void Append(DiffLine? left, DiffLine? right, string? header = null)
        {
            row++;
            before.AppendLine(header ?? Display(left)); after.AppendLine(header ?? Display(right));
            if (left is not null) oldLines[row] = left;
            if (right is not null) newLines[row] = right;
        }
        foreach (var hunk in diff.Hunks)
        {
            headers.Add(row + 1); Append(null, null, hunk.Header);
            for (var index = 0; index < hunk.Lines.Count;)
            {
                var line = hunk.Lines[index];
                if (line.Kind is DiffLineKind.Added or DiffLineKind.Removed)
                {
                    changes.Add(row + 1);
                    var removed = new List<DiffLine>(); var added = new List<DiffLine>();
                    var previousKind = line.Kind;
                    while (index < hunk.Lines.Count && hunk.Lines[index].Kind is DiffLineKind.Added or DiffLineKind.Removed or DiffLineKind.Metadata)
                    {
                        line = hunk.Lines[index++];
                        if (line.Kind != DiffLineKind.Metadata) previousKind = line.Kind;
                        (previousKind == DiffLineKind.Removed ? removed : added).Add(line);
                    }
                    for (var i = 0; i < Math.Max(removed.Count, added.Count); i++)
                        Append(i < removed.Count ? removed[i] : null, i < added.Count ? added[i] : null);
                }
                else { Append(line, line); index++; }
            }
        }
        return new SplitDiff(new DiffDocument(before.ToString(), oldLines, headers, changes), new DiffDocument(after.ToString(), newLines, headers, changes));
    }

    private static string Display(DiffLine? line) => line is null ? "" : (line.Kind switch
    {
        DiffLineKind.Added => "+",
        DiffLineKind.Removed => "-",
        DiffLineKind.Context => " ",
        _ => ""
    }) + line.Text;
}
