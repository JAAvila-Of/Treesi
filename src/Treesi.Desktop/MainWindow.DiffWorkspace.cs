using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Threading;
using AvaloniaEdit;
using Treesi.Domain;

namespace Treesi.Desktop;

public sealed partial class MainWindow
{
    private TextEditor _before = null!, _after = null!;
    private DiffDecoration _beforeDecoration = null!, _afterDecoration = null!;
    private ComboBox _diffMode = null!;
    private Control _splitView = null!;
    private Button _previousBlock = null!, _nextBlock = null!;
    private TextBlock _blockPosition = null!;
    private FileDiff? _currentDiff;
    private SplitDiff? _splitDiff;
    private readonly List<int> _unifiedBlocks = [];
    private int _blockIndex = -1;
    private bool _synchronizingScroll;
    private TextEditor _activeSplitEditor = null!;
    private TextEditor ActiveEditor => _diffMode.SelectedIndex == 1 ? _activeSplitEditor : _diff;

    private void InitializeDiffWorkspace()
    {
        _before = Required<TextEditor>("BeforeEditor"); _after = Required<TextEditor>("AfterEditor");
        _activeSplitEditor = _after;
        _beforeDecoration = new DiffDecoration(_before, 1); _afterDecoration = new DiffDecoration(_after, 2);
        _diffMode = Required<ComboBox>("DiffModeSelector"); _splitView = Required<Control>("SplitView");
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
        using var reader = new StringReader(result.Patch);
        var row = 0; var hunk = 0;
        while (reader.ReadLine() is { } text)
        {
            row++;
            if (hunk < result.Hunks.Count && text == result.Hunks[hunk].Header)
            {
                _unifiedBlocks.Add(row);
                foreach (var unused in result.Hunks[hunk++].Lines) { reader.ReadLine(); row++; }
            }
        }
        _decoration.SetDiff(result);
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
            _beforeDecoration.SetDocument(_splitDiff.Before); _afterDecoration.SetDocument(_splitDiff.After);
        }
        _diff.IsVisible = !split; _splitView.IsVisible = split;
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
            SelectBlock(_before, _splitDiff.Before.HunkLines[_blockIndex]);
            SelectBlock(_after, _splitDiff.After.HunkLines[_blockIndex]);
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
        _currentDiff = null; _splitDiff = null; _unifiedBlocks.Clear(); _blockIndex = -1;
        _splitView.IsVisible = false;
        _diffMode.IsEnabled = _previousBlock.IsEnabled = _nextBlock.IsEnabled = false;
        _blockPosition.Text = "No change blocks";
        _beforeDecoration.Clear(); _afterDecoration.Clear();
    }
}
