using Treesi.Domain;

namespace Treesi.Desktop;

public sealed record DiffViewOptions(bool CodeColors = true, bool InlineChanges = true);
public sealed record DiffLinePresentation(IReadOnlyList<CodeToken> Tokens, IReadOnlyList<TextSpan> Changes);

public sealed class DiffPresentation
{
    public const int CharacterBudget = 200000;
    private readonly Dictionary<DiffLine, DiffLinePresentation> _lines = new(ReferenceEqualityComparer.Instance);
    public FragmentLanguage Language { get; private init; }
    public DiffLinePresentation? ForLine(DiffLine line) => _lines.GetValueOrDefault(line);

    public static DiffPresentation Create(FileDiff diff)
    {
        var presentation = new DiffPresentation { Language = FragmentLexer.Detect(diff.Path) };
        var cells = InlineChanges.CellBudget; var characters = CharacterBudget;
        foreach (var hunk in diff.Hunks)
        {
            var before = new FragmentLexer(presentation.Language); var after = new FragmentLexer(presentation.Language);
            foreach (var line in hunk.Lines)
            {
                if (line.Kind == DiffLineKind.Metadata) continue;
                if (line.Text.Length > characters) { characters = 0; continue; }
                characters -= line.Text.Length;
                IReadOnlyList<CodeToken> tokens = [];
                if (line.Kind is DiffLineKind.Removed or DiffLineKind.Context) tokens = before.Scan(line.Text);
                if (line.Kind is DiffLineKind.Added or DiffLineKind.Context) tokens = after.Scan(line.Text);
                presentation._lines[line] = new(tokens, []);
            }
            for (var index = 0; index < hunk.Lines.Count;)
            {
                if (hunk.Lines[index].Kind is not (DiffLineKind.Added or DiffLineKind.Removed)) { index++; continue; }
                var removed = new List<DiffLine>(); var added = new List<DiffLine>();
                while (index < hunk.Lines.Count && hunk.Lines[index].Kind is DiffLineKind.Added or DiffLineKind.Removed or DiffLineKind.Metadata)
                {
                    var line = hunk.Lines[index++];
                    if (line.Kind == DiffLineKind.Removed) removed.Add(line);
                    else if (line.Kind == DiffLineKind.Added) added.Add(line);
                }
                for (var row = 0; row < Math.Min(removed.Count, added.Count); row++)
                {
                    var change = InlineChanges.Compare(removed[row].Text, added[row].Text, ref cells);
                    if (presentation._lines.TryGetValue(removed[row], out var oldLine)) presentation._lines[removed[row]] = oldLine with { Changes = change.Before };
                    if (presentation._lines.TryGetValue(added[row], out var newLine)) presentation._lines[added[row]] = newLine with { Changes = change.After };
                }
            }
        }
        return presentation;
    }
}
