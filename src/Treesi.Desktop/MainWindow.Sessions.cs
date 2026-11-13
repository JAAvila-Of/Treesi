using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Treesi.Desktop.Workspaces;
using Treesi.Domain;

namespace Treesi.Desktop;

public sealed partial class MainWindow
{
    private const int MaximumRepositoryTabs = 8;
    private static readonly StringComparer SessionRootComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly ObservableCollection<RepositoryTab> _repositoryTabs = [];
    private RepositoryTab? _activeRepositoryTab;
    private bool _updatingTabs;
    private long _sessionVersion;

    private void InitializeSessions()
    {
        var tabs = Required<TabStrip>("RepositoryTabs");
        tabs.ItemsSource = _repositoryTabs;
        tabs.SelectionChanged += (_, _) =>
        {
            if (!_updatingTabs && tabs.SelectedItem is RepositoryTab tab) ActivateRepositoryTab(tab);
        };
        Required<Button>("NewRepositoryTabButton").Click += (_, _) => ShowRecentWorkspaces();
        tabs.AddHandler(PointerPressedEvent, (_, args) =>
        {
            if (Required<Control>("HomeSurface").IsVisible && args.Source is Control source
                && source.FindAncestorOfType<TabStripItem>() is { IsSelected: true } && _activeRepositoryTab is { } active)
                ActivateRepositoryTab(active);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        tabs.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key == Key.Enter && Required<Control>("HomeSurface").IsVisible && _activeRepositoryTab is { } active)
            { ActivateRepositoryTab(active); args.Handled = true; }
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, (_, args) => HandleSessionNavigation(args), RoutingStrategies.Tunnel);
    }

    private static string SessionRoot(string root) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

    private RepositoryTab? FindRepositoryTab(string root) => _repositoryTabs.FirstOrDefault(tab => SessionRootComparer.Equals(tab.Root, SessionRoot(root)));

    private void RegisterRepositoryTab(RepositorySnapshot snapshot)
    {
        var tab = FindRepositoryTab(snapshot.Root);
        _updatingTabs = true;
        try
        {
            if (tab is null) { tab = new(SessionRoot(snapshot.Root)); _repositoryTabs.Add(tab); }
            _activeRepositoryTab = tab;
            tab.State = null;
            Required<TabStrip>("RepositoryTabs").SelectedItem = tab;
            Required<Control>("RepositoryTabBar").IsVisible = true;
        }
        finally { _updatingTabs = false; }
        RevealRepositoryTab(tab);
    }

    private void SaveRepositorySession()
    {
        if (_activeRepositoryTab is null || _snapshot is null) return;
        var paused = Required<ProgressBar>("BusyIndicator").IsVisible && _currentDiff is null;
        _activeRepositoryTab.State = new()
        {
            Snapshot = _snapshot,
            Files = _allFiles,
            SelectedPath = (_files.SelectedItem as ChangedFileItem)?.Path,
            Comparison = _comparison.SelectedIndex,
            Layout = _diffMode.SelectedIndex,
            Options = _currentDiff?.Options ?? _diffOptions,
            ViewOptions = _viewOptions,
            Diff = _currentDiff,
            Revisions = _revisionComparison,
            WorkingSelectedPath = _workingSelectedPath,
            History = _historyPage,
            Commit = _historyCommit,
            Parent = _historyParent,
            HistoryScope = Required<ComboBox>("HistoryScopeSelector").SelectedIndex,
            HistoryStart = Required<TextBox>("HistoryStartInput").Text,
            HistorySelection = (Required<ListBox>("HistoryCommits").SelectedItem as HistoryCommitItem)?.Commit.CommitId,
            HistoryNotice = Required<Button>("ReloadHistoryButton").IsEnabled ? Required<TextBlock>("HistoryNotice").Text : "History paused; use Go to read local history again.",
            HistoryVisible = Required<Control>("HistorySidebar").IsVisible,
            Filter = _filter.Text,
            Search = _search.Text,
            FilesVisible = _filesPanelVisible,
            FocusMode = _focusMode,
            BeforeActive = _activeSplitEditor == _before,
            Block = _blockIndex,
            Unified = CaptureEditor(_diff),
            Before = CaptureEditor(_before),
            After = CaptureEditor(_after),
            Notice = new(paused ? "Inspection paused." : _emptyTitle.Text,
                paused ? "This tab's pending inspection was cancelled. Refresh to read the repository again." : _emptyDescription.Text,
                _fileTitle.Text, _added.Text, _removed.Text)
        };
    }

    private static EditorPosition CaptureEditor(TextEditor editor) => new(editor.SelectionStart, editor.SelectionLength, editor.HorizontalOffset, editor.VerticalOffset);

    private void InvalidateSessionRequests()
    {
        CloseWorkspacePicker(false);
        _sessionVersion++;
        Cancel();
        foreach (var cancellation in new[] { _opening, _loading, _comparingRevisions, _listingReferences, _listingHistory, _choosingCommit }) cancellation?.Dispose();
        _opening = _loading = _comparingRevisions = _listingReferences = _listingHistory = _choosingCommit = null;
        _isOpeningWorkspace = _isComparingRevisions = false;
        PendingInspection = Task.CompletedTask;
        _motion.Stop();
        CloseRevisionPicker(false, false); CloseComparisonOptions(false);
        Required<Control>("CommitDetailsPanel").IsVisible = false;
        Required<Control>("OpenRecoveryNotice").IsVisible = false;
        _failedWorkspace = null;
        SetBusy(false);
    }

    private void ActivateRepositoryTab(RepositoryTab tab)
    {
        if (_closed || !_repositoryTabs.Contains(tab)) return;
        if (tab == _activeRepositoryTab)
        {
            CloseWorkspacePicker(false);
            if (_snapshot is not null) ShowComparison();
            RevealRepositoryTab(tab);
            return;
        }
        if (tab.State is not { } saved) return;
        SaveCurrentWorkspace(); SaveRepositorySession();
        InvalidateSessionRequests();
        _activeRepositoryTab = tab;
        RestoreRepositorySession(saved);
        RevealRepositoryTab(tab);
    }

    private void RestoreRepositorySession(RepositorySessionState saved)
    {
        _updatingFiles = _updatingHistory = true;
        try
        {
            ClearDiffWorkspace(); _decoration.Clear();
            _snapshot = saved.Snapshot; _allFiles = saved.Files;
            _revisionComparison = saved.Revisions; _workingSelectedPath = saved.WorkingSelectedPath;
            _diffOptions = saved.Options; _viewOptions = saved.ViewOptions;
            _historyPage = saved.History; _historyCommit = saved.Commit; _historyParent = saved.Parent;
            _filter.Text = saved.Filter; _search.Text = saved.Search;
            _comparison.SelectedIndex = saved.Comparison; _diffMode.SelectedIndex = saved.Layout;
            var files = _allFiles.Where(file => file.Path.Contains(saved.Filter ?? "", StringComparison.OrdinalIgnoreCase)).Select(file => new ChangedFileItem(file, saved.Revisions is not null)).ToArray();
            _files.ItemsSource = files; _files.SelectedItem = files.FirstOrDefault(file => file.Path == saved.SelectedPath);
            var history = Required<ListBox>("HistoryCommits");
            var rows = saved.History?.Items.Select(item => new HistoryCommitItem(item)).ToArray() ?? [];
            history.ItemsSource = rows; history.SelectedItem = rows.FirstOrDefault(item => item.Commit.CommitId == saved.HistorySelection);
            Required<ComboBox>("HistoryScopeSelector").SelectedIndex = saved.HistoryScope;
            Required<TextBox>("HistoryStartInput").Text = saved.HistoryStart;
            Required<TextBlock>("HistoryNotice").Text = saved.HistoryNotice;
            SetHistoryPaging(true); ShowHistorySidebar(saved.HistoryVisible);
            _filesPanelVisible = saved.FilesVisible; _focusMode = saved.FocusMode; ApplyPresentation();
            _repositoryName.Text = Path.GetFileName(Path.TrimEndingDirectorySeparator(saved.Snapshot.Root));
            _repositoryLabel.Text = saved.Snapshot.Root; ToolTip.SetTip(_repositoryLabel, saved.Snapshot.Root);
            _fileCount.Text = (saved.Revisions?.Files.Count ?? saved.Snapshot.Files.Count).ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (var decoration in new[] { _decoration, _beforeDecoration, _afterDecoration }) decoration.SetViewOptions(_viewOptions);
            UpdateOptionsIndicator(); UpdateRevisionChrome(); UpdateComparisonDescription();
            if (saved.Diff is { } diff)
            {
                SetWorkspaceDiff(diff); _emptyState.IsVisible = false; _diffLabel.IsVisible = true;
                _search.IsEnabled = _findNext.IsEnabled = true;
                _blockIndex = saved.Block;
                if (_blockIndex >= 0) _blockPosition.Text = $"Block {_blockIndex + 1} of {_unifiedBlocks.Count}";
            }
            else ShowState(saved.Notice.Title ?? "No comparison loaded.", saved.Notice.Description ?? "Select a file or refresh.");
            _fileTitle.Text = saved.Notice.FileTitle;
            _added.Text = saved.Notice.Added; _removed.Text = saved.Notice.Removed;
            _status.Text = "Saved tab view · Refresh to read current repository state";
            _refresh.IsEnabled = true; SetRevisionInputAvailability(true);
            _activeSplitEditor = saved.BeforeActive ? _before : _after;
            ShowComparison();
            RestoreEditorPositions(saved);
            var active = _activeRepositoryTab;
            var version = _sessionVersion;
            Dispatcher.UIThread.Post(() =>
            {
                if (!_closed && version == _sessionVersion && ReferenceEquals(_activeRepositoryTab, active) && ReferenceEquals(_currentDiff, saved.Diff)) RestoreEditorPositions(saved);
            }, DispatcherPriority.Loaded);
        }
        finally { _updatingFiles = _updatingHistory = false; }
    }

    private void RestoreEditorPositions(RepositorySessionState saved)
    {
        _synchronizingScroll = true;
        try
        {
            foreach (var (editor, position) in new[] { (_diff, saved.Unified), (_before, saved.Before), (_after, saved.After) })
            {
                var start = Math.Min(position.Start, editor.Document.TextLength);
                editor.Select(start, Math.Min(position.Length, editor.Document.TextLength - start));
                if (editor.FindDescendantOfType<ScrollViewer>() is { } scroll) scroll.Offset = new Vector(position.Horizontal, position.Vertical);
            }
        }
        finally { _synchronizingScroll = false; }
    }

    private void CloseRepositoryTab(object? sender, RoutedEventArgs args)
    {
        args.Handled = true;
        if (sender is not Button { Tag: RepositoryTab tab }) return;
        CloseRepositorySession(tab);
    }

    private void CloseRepositorySession(RepositoryTab tab)
    {
        var index = _repositoryTabs.IndexOf(tab);
        if (index < 0) return;
        if (tab == _activeRepositoryTab)
        {
            SaveCurrentWorkspace();
            InvalidateSessionRequests();
            _activeRepositoryTab = null;
        }
        _updatingTabs = true;
        try { _repositoryTabs.Remove(tab); }
        finally { _updatingTabs = false; }
        tab.State = null;
        if (_activeRepositoryTab is not null) { SelectRepositoryTab(_activeRepositoryTab); return; }
        if (_repositoryTabs.Count > 0)
        {
            var next = _repositoryTabs[Math.Min(index, _repositoryTabs.Count - 1)];
            _snapshot = null;
            SelectRepositoryTab(next); ActivateRepositoryTab(next);
        }
        else
        {
            _snapshot = null; _allFiles = []; _files.ItemsSource = null;
            ClearRevisionComparison(); ShowState("No repository open.", "Open a local repository or choose a recent shortcut.");
            _repositoryName.Text = "No repository open"; _repositoryLabel.Text = "A local view of your changes";
            _refresh.IsEnabled = _filter.IsEnabled = _comparison.IsEnabled = false;
            Required<Button>("CompareRevisionsButton").IsEnabled = Required<Button>("HistoryButton").IsEnabled = false;
            Required<Control>("RepositoryTabBar").IsVisible = true;
            ShowRecentWorkspaces();
        }
    }

    private void SelectRepositoryTab(RepositoryTab tab)
    {
        _updatingTabs = true;
        try { Required<TabStrip>("RepositoryTabs").SelectedItem = tab; }
        finally { _updatingTabs = false; }
    }

    private void RevealRepositoryTab(RepositoryTab tab)
    {
        var version = _sessionVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (_closed || version != _sessionVersion || _activeRepositoryTab != tab) return;
            Required<TabStrip>("RepositoryTabs").ContainerFromIndex(_repositoryTabs.IndexOf(tab))?.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    private bool HandleSessionNavigation(KeyEventArgs args)
    {
        if (ActiveReviewPanel is not null || (args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0) return false;
        if (args.Key == Key.Tab && _repositoryTabs.Count > 1)
        {
            var step = (args.KeyModifiers & KeyModifiers.Shift) == 0 ? 1 : -1;
            var index = Math.Max(0, _repositoryTabs.IndexOf(_activeRepositoryTab!));
            var next = _repositoryTabs[(index + step + _repositoryTabs.Count) % _repositoryTabs.Count];
            SelectRepositoryTab(next); ActivateRepositoryTab(next); args.Handled = true; return true;
        }
        if (args.Key == Key.T) { ShowRecentWorkspaces(); args.Handled = true; return true; }
        if (args.Key == Key.W && _activeRepositoryTab is { } active) { CloseRepositorySession(active); args.Handled = true; return true; }
        return false;
    }
}
