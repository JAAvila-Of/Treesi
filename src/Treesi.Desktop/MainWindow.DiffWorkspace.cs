using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Threading;
using AvaloniaEdit;
using Treesi.Domain;

namespace Treesi.Desktop;

public sealed partial class MainWindow
{
    private TextEditor _before = null!, _after = null!;
    private DiffDecoration _beforeDecoration = null!, _afterDecoration = null!;
    private TabStrip _diffMode = null!;
    private Control _splitView = null!;
    private Button _previousBlock = null!, _nextBlock = null!;
    private TextBlock _blockPosition = null!;
    private FileDiff? _currentDiff;
    private SplitDiff? _splitDiff;
    private readonly List<int> _unifiedBlocks = [];
    private IReadOnlyDictionary<int, DiffLine> _unifiedLines = new Dictionary<int, DiffLine>();
    private int _blockIndex = -1;
    private bool _synchronizingScroll;
    private TextEditor _activeSplitEditor = null!;
    private TextEditor ActiveEditor => _diffMode.SelectedIndex == 1 ? _activeSplitEditor : _diff;

    private void InitializeDiffWorkspace()
    {
        _before = Required<TextEditor>("BeforeEditor"); _after = Required<TextEditor>("AfterEditor");
        _activeSplitEditor = _after;
        _beforeDecoration = new DiffDecoration(_before, 1); _afterDecoration = new DiffDecoration(_after, 2);
        _diffMode = Required<TabStrip>("DiffModeSelector"); _splitView = Required<Control>("SplitView");
        _previousBlock = Required<Button>("PreviousBlockButton"); _nextBlock = Required<Button>("NextBlockButton");
        _blockPosition = Required<TextBlock>("BlockPosition");
        foreach (var editor in new[] { _before, _after })
        {
            editor.Options.EnableHyperlinks = false; editor.Options.EnableEmailHyperlinks = false;
            editor.Options.AllowScrollBelowDocument = false;
            editor.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Visible;
            editor.GotFocus += (_, _) => _activeSplitEditor = editor;
        }
        _before.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) => SynchronizeScroll(_before, _after));
        _after.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) => SynchronizeScroll(_after, _before));
        _diffMode.SelectionChanged += (_, _) => ApplyDiffMode();
        _previousBlock.Click += (_, _) => NavigateBlock(-1);
        _nextBlock.Click += (_, _) => NavigateBlock(1);
        ClearDiffWorkspace();
    }

    private void SynchronizeScroll(TextEditor source, TextEditor target)
    {
        if (_synchronizingScroll || !_splitView.IsVisible || source.FindDescendantOfType<ScrollViewer>() is not { } origin
            || target.FindDescendantOfType<ScrollViewer>() is not { } destination || Math.Abs(origin.Offset.Y - destination.Offset.Y) < 0.1) return;
        _synchronizingScroll = true;
        try
        {
            destination.Offset = destination.Offset.WithY(origin.Offset.Y);
        }
        finally { _synchronizingScroll = false; }
    }

    private void SetWorkspaceDiff(FileDiff result)
    {
        _currentDiff = result; _splitDiff = null; _blockIndex = -1;
        _unifiedBlocks.Clear();
        _unifiedLines = DiffSourceMap.Unified(result);
        using var reader = new StringReader(result.Patch);
        var row = 0; var hunk = 0;
        while (reader.ReadLine() is { } text)
        {
            row++;
            if (hunk < result.Hunks.Count && text == result.Hunks[hunk].Header)
            {
                if (result.Options?.FullContext != true) _unifiedBlocks.Add(row);
                var changing = false;
                foreach (var line in result.Hunks[hunk++].Lines)
                {
                    reader.ReadLine(); row++;
                    if (result.Options?.FullContext != true) continue;
                    if (line.Kind is DiffLineKind.Added or DiffLineKind.Removed)
                    {
                        if (!changing) _unifiedBlocks.Add(row);
                        changing = true;
                    }
                    else if (line.Kind != DiffLineKind.Metadata) changing = false;
                }
            }
        }
        _presentation = DiffPresentation.Create(result);
        _decoration.SetDiff(result, _presentation);
        if (_unifiedBlocks.Count == 0) _diffMode.SelectedIndex = 0;
        _diffMode.IsEnabled = _unifiedBlocks.Count > 0;
        ToolTip.SetTip(_diffMode, _unifiedBlocks.Count == 0 ? "Side-by-side layout requires text change blocks." : "Diff layout");
        _previousBlock.IsEnabled = _nextBlock.IsEnabled = _unifiedBlocks.Count > 0;
        _blockPosition.Text = $"{_unifiedBlocks.Count} change {(_unifiedBlocks.Count == 1 ? "block" : "blocks")}";
        var endpoints = result.Comparison switch
        {
            Comparison.Staged => ("HEAD", "INDEX"),
            Comparison.Head => ("HEAD", "WORKING TREE"),
            _ => ("INDEX", "WORKING TREE")
        };
        Required<TextBlock>("BeforeLabel").Text = endpoints.Item1;
        Required<TextBlock>("AfterLabel").Text = endpoints.Item2;
        ToolTip.SetTip(Required<TextBlock>("BeforeLabel"), null);
        ToolTip.SetTip(Required<TextBlock>("AfterLabel"), null);
        if (result.Revisions is { } revisions)
        {
            Required<TextBlock>("BeforeLabel").Text = revisions.IsRootComparison ? "EMPTY TREE" : $"{(revisions.Mode == RevisionComparisonMode.MergeBase ? "MERGE BASE" : "BASE")} {revisions.EffectiveBaseCommitId[..12]}";
            Required<TextBlock>("AfterLabel").Text = $"TARGET {revisions.TargetCommitId[..12]}";
            ToolTip.SetTip(Required<TextBlock>("BeforeLabel"), revisions.IsRootComparison ? "Empty tree before the initial commit." : $"{revisions.BaseName}: {revisions.BaseCommitId}. Effective base: {revisions.EffectiveBaseCommitId}");
            ToolTip.SetTip(Required<TextBlock>("AfterLabel"), $"{revisions.TargetName}: {revisions.TargetCommitId}");
        }
        ApplyDiffMode();
    }

    private void ApplyDiffMode()
    {
        if (_currentDiff is null) return;
        var split = _diffMode.SelectedIndex == 1;
        Required<TextBlock>("DiffLayoutLabel").Text = split ? "SPLIT" : "UNIFIED";
        if (split && _splitDiff is null)
        {
            _splitDiff = SplitDiff.Create(_currentDiff);
            _beforeDecoration.SetDocument(_splitDiff.Before, _presentation); _afterDecoration.SetDocument(_splitDiff.After, _presentation);
        }
        _diff.IsVisible = !split; _splitView.IsVisible = split;
        _motion.Reveal(Required<Control>("DiffSurface"));
        if (_blockIndex >= 0)
        {
            ScrollToBlock();
            Dispatcher.UIThread.Post(() =>
            {
                if (!_closed && _currentDiff is not null && _blockIndex >= 0) ScrollToBlock();
            }, DispatcherPriority.Loaded);
        }
    }

    private void NavigateBlock(int direction)
    {
        if (_unifiedBlocks.Count == 0) return;
        _blockIndex = _blockIndex < 0 ? (direction > 0 ? 0 : _unifiedBlocks.Count - 1)
            : (_blockIndex + direction + _unifiedBlocks.Count) % _unifiedBlocks.Count;
        ScrollToBlock();
    }

    private void ScrollToBlock()
    {
        if (_diffMode.SelectedIndex == 1 && _splitDiff is not null)
        {
            var before = _currentDiff?.Options?.FullContext == true ? _splitDiff.Before.ChangeLines! : _splitDiff.Before.HunkLines;
            var after = _currentDiff?.Options?.FullContext == true ? _splitDiff.After.ChangeLines! : _splitDiff.After.HunkLines;
            SelectBlock(_before, before[_blockIndex]);
            SelectBlock(_after, after[_blockIndex]);
        }
        else SelectBlock(_diff, _unifiedBlocks[_blockIndex]);
        _blockPosition.Text = $"Block {_blockIndex + 1} of {_unifiedBlocks.Count}";
        _status.Text = $"Change block {_blockIndex + 1} of {_unifiedBlocks.Count}";
    }

    private static void SelectBlock(TextEditor editor, int number)
    {
        var line = editor.Document.GetLineByNumber(number);
        editor.Select(line.Offset, line.Length);
        RevealLine(editor, number);
    }

    private static void RevealLine(TextEditor editor, int number)
    {
        if (editor.FindDescendantOfType<ScrollViewer>() is { } scroll)
        {
            var position = editor.TextArea.TextView.GetVisualPosition(new TextViewPosition(number, 1), AvaloniaEdit.Rendering.VisualYPosition.LineTop);
            scroll.Offset = scroll.Offset.WithY(Math.Max(0, position.Y - scroll.Viewport.Height / 3));
        }
    }

    private void ClearDiffWorkspace()
    {
        _presentation = null;
        _unifiedLines = new Dictionary<int, DiffLine>();
        _currentDiff = null; _splitDiff = null; _unifiedBlocks.Clear(); _blockIndex = -1;
        _splitView.IsVisible = false;
        _diffMode.IsEnabled = _previousBlock.IsEnabled = _nextBlock.IsEnabled = false;
        _blockPosition.Text = "No change blocks";
        _beforeDecoration.Clear(); _afterDecoration.Clear();
    }

    private static void RevealMatch(TextEditor editor, int offset, int length)
    {
        if (editor.FindDescendantOfType<ScrollViewer>() is not { } scroll) return;
        var start = editor.Document.GetLocation(offset);
        var end = editor.Document.GetLocation(offset + length);
        var view = editor.TextArea.TextView;
        var left = view.GetVisualPosition(new TextViewPosition(start.Line, start.Column), AvaloniaEdit.Rendering.VisualYPosition.LineTop).X;
        var right = view.GetVisualPosition(new TextViewPosition(end.Line, end.Column), AvaloniaEdit.Rendering.VisualYPosition.LineTop).X;
        if (left < scroll.Offset.X || right > scroll.Offset.X + scroll.Viewport.Width)
            scroll.Offset = scroll.Offset.WithX(Math.Max(0, left - scroll.Viewport.Width / 4));
    }
}
