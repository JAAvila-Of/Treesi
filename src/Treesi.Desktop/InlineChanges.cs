using System.Globalization;

namespace Treesi.Desktop;

public readonly record struct TextSpan(int Start, int Length);
public sealed record InlineChanges(IReadOnlyList<TextSpan> Before, IReadOnlyList<TextSpan> After)
{
    public const int MaximumLineLength = 4096;
    public const int MaximumElements = 256;
    public const int CellBudget = 500000;
    public static InlineChanges Empty { get; } = new([], []);

    public static InlineChanges Compare(string before, string after, ref int remainingCells)
    {
        if (before == after || before.Length > MaximumLineLength || after.Length > MaximumLineLength) return Empty;
        var left = StringInfo.ParseCombiningCharacters(before); var right = StringInfo.ParseCombiningCharacters(after);
        var n = left.Length; var m = right.Length;
        bool Equal(int i, int j) => Element(before, left, i).SequenceEqual(Element(after, right, j));
        var unchangedLeft = new bool[n]; var unchangedRight = new bool[m];
        if (n <= MaximumElements && m <= MaximumElements && (long)(n + 1) * (m + 1) <= remainingCells)
        {
            remainingCells -= (n + 1) * (m + 1);
            var lengths = new int[n + 1, m + 1];
            for (var i = n - 1; i >= 0; i--)
                for (var j = m - 1; j >= 0; j--)
                    lengths[i, j] = Equal(i, j) ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            for (int i = 0, j = 0; i < n && j < m;)
            {
                if (Equal(i, j)) { unchangedLeft[i++] = true; unchangedRight[j++] = true; }
                else if (lengths[i + 1, j] >= lengths[i, j + 1]) i++;
                else j++;
            }
        }
        else
        {
            var prefix = 0;
            while (prefix < n && prefix < m && Equal(prefix, prefix)) { unchangedLeft[prefix] = unchangedRight[prefix] = true; prefix++; }
            for (int i = n - 1, j = m - 1; i >= prefix && j >= prefix && Equal(i, j); i--, j--) { unchangedLeft[i] = unchangedRight[j] = true; }
        }
        // Entirely unrelated lines already have their full added/removed background.
        if (!unchangedLeft.Any(value => value)) return Empty;
        return new(Changed(before, left, unchangedLeft), Changed(after, right, unchangedRight));
    }

    private static ReadOnlySpan<char> Element(string text, int[] offsets, int index) => text.AsSpan(offsets[index], (index + 1 < offsets.Length ? offsets[index + 1] : text.Length) - offsets[index]);
    private static IReadOnlyList<TextSpan> Changed(string text, int[] offsets, bool[] unchanged)
    {
        var spans = new List<TextSpan>();
        for (var i = 0; i < offsets.Length;)
        {
            if (unchanged[i]) { i++; continue; }
            var start = offsets[i++];
            while (i < offsets.Length && !unchanged[i]) i++;
            spans.Add(new(start, (i < offsets.Length ? offsets[i] : text.Length) - start));
        }
        return spans;
    }
}
