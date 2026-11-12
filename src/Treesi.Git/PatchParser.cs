using System.Text.RegularExpressions;
using Treesi.Domain;

namespace Treesi.Git;

public static partial class PatchParser
{
    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeader();

    public static IReadOnlyList<DiffHunk> Parse(string patch)
    {
        var hunks = new List<DiffHunk>();
        List<DiffLine>? lines = null;
        int oldLine = 0, newLine = 0;
        using var reader = new StringReader(patch);
        while (reader.ReadLine() is { } line)
        {
            var match = HunkHeader().Match(line);
            if (match.Success)
            {
                oldLine = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                newLine = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                lines = [];
                hunks.Add(new DiffHunk(line, lines));
            }
            else if (lines is not null)
            {
                lines.Add(line.FirstOrDefault() switch
                {
                    '+' => new(DiffLineKind.Added, null, newLine++, line[1..]),
                    '-' => new(DiffLineKind.Removed, oldLine++, null, line[1..]),
                    ' ' => new(DiffLineKind.Context, oldLine++, newLine++, line[1..]),
                    _ => new(DiffLineKind.Metadata, null, null, line)
                });
            }
        }
        return hunks;
    }
}
