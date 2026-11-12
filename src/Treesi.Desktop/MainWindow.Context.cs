using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Treesi.Domain;

namespace Treesi.Desktop;

public sealed partial class MainWindow
{
    private (DiffSourceAnchor Anchor, bool Before)? CaptureSourceAnchor()
    {
        if (_currentDiff is null) return null;
        var editor = ActiveEditor;
        var rows = _diffMode.SelectedIndex != 1 ? _unifiedLines : editor == _before ? _splitDiff!.Before.Lines : _splitDiff!.After.Lines;
        var location = editor.Document.GetLocation(editor.SelectionStart);
        var selected = editor.SelectedText;
        var view = editor.TextArea.TextView;
        if (selected.Length == 0 && view.VisualLinesValid && view.VisualLines.Count > 0)
        {
            var visible = view.VisualLines.FirstOrDefault(line => line.VisualTop + line.Height > view.VerticalOffset);
            if (visible is not null && (location.Line < visible.FirstDocumentLine.LineNumber || location.Line > view.VisualLines[^1].LastDocumentLine.LineNumber))
                location = editor.Document.GetLocation(visible.FirstDocumentLine.Offset);
        }
        if (selected.Contains('\n') || selected.Contains('\r')) selected = "";
        var anchor = DiffSourceMap.Capture(rows, location.Line, location.Column, selected);
        return anchor is null ? null : (anchor, editor == _before);
    }

    private void RestoreSourceAnchor((DiffSourceAnchor Anchor, bool Before) saved, FileDiff expected)
    {
        var editor = _diffMode.SelectedIndex != 1 ? _diff : saved.Before ? _before : _after;
        var rows = _diffMode.SelectedIndex != 1 ? _unifiedLines : saved.Before ? _splitDiff!.Before.Lines : _splitDiff!.After.Lines;
        if (DiffSourceMap.Locate(rows, saved.Anchor) is not int row) return;
        var line = editor.Document.GetLineByNumber(row);
        var offset = line.Offset + Math.Min(saved.Anchor.Column - 1, line.Length);
        var selection = saved.Anchor.SelectedText;
        var length = selection.Length <= line.EndOffset - offset && editor.Document.GetText(offset, selection.Length) == selection ? selection.Length : 0;
        editor.Select(offset, length);
        void Reveal() { if (!_closed && ReferenceEquals(_currentDiff, expected)) RevealLine(editor, row); }
        Reveal(); Dispatcher.UIThread.Post(Reveal, DispatcherPriority.Loaded);
    }

    private void RestoreComparisonOptions(DiffOptions previous, DiffOptions requested, string message)
    {
        if (_diffOptions == requested) { _diffOptions = previous; UpdateOptionsIndicator(); }
        UpdateComparisonDescription();
        _search.IsEnabled = _findNext.IsEnabled = _currentDiff is not null;
        _status.Text = message + " Previous comparison retained.";
    }

    private void UpdateComparisonDescription()
    {
        if (_files.SelectedItem is not ChangedFileItem item) return;
        var context = _diffOptions.FullContext ? "Full file context" : $"Context {_diffOptions.ContextLines}";
        _comparisonLabel.Text = $"{item.Path} · {context}";
        ToolTip.SetTip(_comparisonLabel, item.Path + " · " + context);
    }
}
