using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Treesi.Domain;

namespace Treesi.Desktop;

public sealed class DiffDecoration : DocumentColorizingTransformer, IBackgroundRenderer, IDisposable
{
    private readonly TextEditor _editor;
    private readonly LineNumbers _numbers;
    private readonly Dictionary<int, DiffLine> _lines = [];
    private DiffPresentation? _presentation;
    private DiffViewOptions _viewOptions = new();
    private int _digits = 4;
    private readonly int _numberColumn;
    public KnownLayer Layer => KnownLayer.Background;

    public DiffDecoration(TextEditor editor, int numberColumn = 0)
    {
        _editor = editor;
        _numberColumn = numberColumn;
        _numbers = new LineNumbers(this);
        editor.TextArea.TextView.LineTransformers.Add(this);
        editor.TextArea.TextView.BackgroundRenderers.Add(this);
        editor.TextArea.LeftMargins.Add(_numbers);
    }

    public void SetDiff(FileDiff diff, DiffPresentation? presentation = null)
    {
        _presentation = presentation ?? DiffPresentation.Create(diff);
        _lines.Clear();
        using var reader = new StringReader(diff.Patch);
        var documentLine = 0;
        var hunkIndex = 0;
        while (reader.ReadLine() is { } text)
        {
            documentLine++;
            if (hunkIndex >= diff.Hunks.Count || text != diff.Hunks[hunkIndex].Header) continue;
            var hunk = diff.Hunks[hunkIndex++];
            foreach (var line in hunk.Lines)
            {
                if (reader.ReadLine() is null) break;
                _lines[++documentLine] = line;
            }
        }
        _editor.Text = diff.Patch;
        _digits = Math.Max(4, _lines.Values.SelectMany(line => new[] { line.OldLine ?? 0, line.NewLine ?? 0 }).DefaultIfEmpty(0).Max().ToString(CultureInfo.InvariantCulture).Length);
        _numbers.InvalidateMeasure();
        _editor.ScrollToHome();
        Refresh();
    }

    public void Clear() { _lines.Clear(); _presentation = null; _editor.Text = ""; Refresh(); }
    public void SetDocument(DiffDocument document, DiffPresentation? presentation = null)
    {
        _presentation = presentation;
        _lines.Clear();
        foreach (var entry in document.Lines) _lines[entry.Key] = entry.Value;
        _editor.Text = document.Text;
        _digits = Math.Max(4, _lines.Values.SelectMany(line => new[] { line.OldLine ?? 0, line.NewLine ?? 0 }).DefaultIfEmpty(0).Max().ToString(CultureInfo.InvariantCulture).Length);
        _numbers.InvalidateMeasure();
        _editor.ScrollToHome();
        Refresh();
    }
    public void Refresh() { _editor.TextArea.TextView.Redraw(); _numbers.InvalidateVisual(); }
    public void SetViewOptions(DiffViewOptions options) { _viewOptions = options; Refresh(); }
    private IBrush Brush(string name) => _editor.FindResource(_editor.ActualThemeVariant, name) as IBrush ?? _editor.Foreground ?? Brushes.Gray;

    protected override void ColorizeLine(DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line);
        var key = _lines.TryGetValue(line.LineNumber, out var entry) ? entry.Kind switch
        {
            DiffLineKind.Added => "Diff.AddText",
            DiffLineKind.Removed => "Diff.RemoveText",
            DiffLineKind.Metadata => "Text.Subtle",
            _ => "Text.Primary"
        } : text.StartsWith("@@", StringComparison.Ordinal) ? "Diff.HunkText" : "Text.Subtle";
        if (line.Length > 0) ChangeLinePart(line.Offset, line.EndOffset, element => element.TextRunProperties.SetForegroundBrush(Brush(key)));
        if (entry is null || entry.Kind == DiffLineKind.Metadata || _presentation?.ForLine(entry) is not { } style) return;
        var start = line.Offset + 1;
        if (_viewOptions.CodeColors && _presentation.Language != FragmentLanguage.Plain)
        {
            if (start < line.EndOffset) ChangeLinePart(start, line.EndOffset, element => element.TextRunProperties.SetForegroundBrush(Brush("Text.Primary")));
            foreach (var token in style.Tokens)
                Part(token.Start, token.Length, () => Brush("Code." + token.Kind), false);
        }
        if (_viewOptions.InlineChanges)
            foreach (var span in style.Changes)
                Part(span.Start, span.Length, () => Brush(entry.Kind == DiffLineKind.Added ? "Diff.AddInline" : "Diff.RemoveInline"), true);
        void Part(int offset, int length, Func<IBrush> brush, bool background)
        {
            if (length <= 0 || offset < 0 || start + offset + length > line.EndOffset) return;
            ChangeLinePart(start + offset, start + offset + length, element =>
            {
                if (background) element.TextRunProperties.SetBackgroundBrush(brush());
                else element.TextRunProperties.SetForegroundBrush(brush());
            });
        }
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid) return;
        foreach (var visual in textView.VisualLines)
        {
            var line = visual.FirstDocumentLine;
            var text = textView.Document.GetText(line);
            var key = _lines.TryGetValue(line.LineNumber, out var entry) ? entry.Kind switch
            {
                DiffLineKind.Added => "Diff.AddBackground",
                DiffLineKind.Removed => "Diff.RemoveBackground",
                _ => null
            } : text.StartsWith("@@", StringComparison.Ordinal) ? "Diff.HunkBackground" : null;
            if (key is not null) drawingContext.FillRectangle(Brush(key), new Rect(0, visual.VisualTop - textView.VerticalOffset, textView.Bounds.Width, visual.Height));
        }
    }

    public void Dispose()
    {
        _editor.TextArea.TextView.LineTransformers.Remove(this);
        _editor.TextArea.TextView.BackgroundRenderers.Remove(this);
        _editor.TextArea.LeftMargins.Remove(_numbers);
    }

    private sealed class LineNumbers(DiffDecoration owner) : AbstractMargin
    {
        protected override Size MeasureOverride(Size availableSize) => new(owner._numberColumn == 0 ? Math.Max(88, 24 + (owner._digits * 2 + 1) * 6.7) : 20 + owner._digits * 6.7, 0);
        protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
        {
            if (oldTextView is not null) oldTextView.VisualLinesChanged -= Changed;
            base.OnTextViewChanged(oldTextView, newTextView);
            if (newTextView is not null) newTextView.VisualLinesChanged += Changed;
        }
        private void Changed(object? sender, EventArgs args) => InvalidateVisual();

        public override void Render(DrawingContext context)
        {
            context.FillRectangle(owner.Brush("Diff.Gutter"), new Rect(Bounds.Size));
            context.FillRectangle(owner.Brush("Border.Default"), new Rect(Bounds.Width - 1, 0, 1, Bounds.Height));
            if (TextView is null || !TextView.VisualLinesValid) return;
            foreach (var visual in TextView.VisualLines)
            {
                if (!owner._lines.TryGetValue(visual.FirstDocumentLine.LineNumber, out var line)) continue;
                var oldNumber = line.OldLine?.ToString(CultureInfo.InvariantCulture) ?? "";
                var newNumber = line.NewLine?.ToString(CultureInfo.InvariantCulture) ?? "";
                var label = owner._numberColumn switch { 1 => oldNumber.PadLeft(owner._digits), 2 => newNumber.PadLeft(owner._digits), _ => $"{oldNumber.PadLeft(owner._digits)} {newNumber.PadLeft(owner._digits)}" };
                var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(owner._editor.FontFamily), 11, owner.Brush("Text.Subtle"));
                context.DrawText(text, new Point(8, visual.VisualTop - TextView.VerticalOffset + 1));
            }
        }
    }
}
