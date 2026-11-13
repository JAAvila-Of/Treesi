using Avalonia.Controls;
using Treesi.Application;
using Treesi.Domain;

namespace Treesi.Desktop;

public sealed partial class MainWindow
{
    private readonly IHistoryService? _history;
    private HistoryPage? _historyPage;
    private CommitDetail? _historyCommit;
    private int _historyParent = 1;
    private bool _updatingHistory;
    private CancellationTokenSource? _listingHistory, _choosingCommit;

    private void InitializeHistory()
    {
        Required<Button>("HistoryButton").Click += (_, _) =>
        {
            if (_graphVisible) ShowComparison();
            ShowHistorySidebar(true);
            if (_historyPage is null) PendingInspection = LoadHistoryAsync(0);
            else Required<ListBox>("HistoryCommits").Focus();
        };
        Required<Button>("HistoryFilesButton").Click += (_, _) => { ShowHistorySidebar(false); _files.Focus(); };
        Required<Button>("ReloadHistoryButton").Click += (_, _) => PendingInspection = LoadHistoryAsync(0);
        Required<Button>("NextHistoryButton").Click += (_, _) => { if (_historyPage?.NextOffset is int offset) PendingInspection = LoadHistoryAsync(offset); };
        Required<Button>("PreviousHistoryButton").Click += (_, _) => { if (_historyPage is { Offset: > 0 } page) PendingInspection = LoadHistoryAsync(Math.Max(0, page.Offset - page.Limit), true); };
        Required<ComboBox>("HistoryScopeSelector").SelectionChanged += (_, _) => { if (!_updatingHistory && _snapshot is not null) PendingInspection = LoadHistoryAsync(0); };
        Required<ListBox>("HistoryCommits").SelectionChanged += (_, _) => { if (!_updatingHistory) PendingInspection = InspectHistorySelectionAsync(); };
        Required<Button>("CloseCommitDetailsButton").Click += (_, _) => CloseCommitDetails();
        Required<Button>("ApplyCommitParentButton").Click += (_, _) => PendingInspection = ApplyCommitParentAsync();
        Required<Button>("CommitWorkingButton").Click += (_, _) => { if (_snapshot is not null) PendingInspection = OpenRepositoryAsync(_snapshot.Root); };
    }

    private async Task LoadHistoryAsync(int offset, bool pinnedFirstPage = false)
    {
        if (_snapshot is null || _history is null) return;
        _listingHistory?.Cancel(); _listingHistory?.Dispose();
        var cancellation = _listingHistory = new CancellationTokenSource();
        var root = _snapshot.Root;
        var scope = Required<ComboBox>("HistoryScopeSelector").SelectedIndex;
        var path = offset > 0 || pinnedFirstPage ? _historyPage!.Path : scope == 1 ? (_files.SelectedItem as ChangedFileItem)?.Path : null;
        if (scope == 1 && path is null && offset == 0 && !pinnedFirstPage) { Required<TextBlock>("HistoryNotice").Text = "Select a file before opening its history."; return; }
        var start = offset > 0 || pinnedFirstPage ? _historyPage!.StartCommitId : Required<TextBox>("HistoryStartInput").Text ?? "HEAD";
        SetHistoryPaging(false); Required<TextBlock>("HistoryNotice").Text = "Reading local history…";
        try
        {
            var page = await _history.PageAsync(root, new(start, path, offset), cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested || _listingHistory != cancellation || _snapshot?.Root != root) return;
            _historyPage = page;
            _updatingHistory = true;
            try
            {
                var rows = page.Items.Select(item => new HistoryCommitItem(item)).ToArray();
                Required<ListBox>("HistoryCommits").ItemsSource = rows;
                Required<ListBox>("HistoryCommits").SelectedItem = rows.FirstOrDefault(item => item.Commit.CommitId == _historyCommit?.CommitId);
            }
            finally { _updatingHistory = false; }
            Required<TextBlock>("HistoryNotice").Text = page.Items.Count == 0 ? "No local commits in this scope." :
                $"{page.Offset + 1}–{page.Offset + page.Items.Count} · {page.StartCommitId[..8]}" + (page.Path is null ? " · repository" : $" · {page.Path}") +
                (page.NextOffset is null && page.Offset + page.Items.Count >= HistoryQuery.MaximumOffset ? " · browsing limit reached" : "");
        }
        catch (OperationCanceledException) { if (_listingHistory == cancellation && !_closed) Required<TextBlock>("HistoryNotice").Text = "History cancelled; the previous page remains available."; }
        catch (Exception error)
        {
            if (!cancellation.IsCancellationRequested && !_closed && _listingHistory == cancellation)
                Required<TextBlock>("HistoryNotice").Text = error is InspectionException known ? known.Message : "Local history could not be read.";
        }
        finally { if (_listingHistory == cancellation && !_closed) SetHistoryPaging(true); }
    }

    private void SetHistoryPaging(bool enabled)
    {
        Required<Button>("NextHistoryButton").IsEnabled = enabled && _historyPage?.NextOffset is not null;
        Required<Button>("PreviousHistoryButton").IsEnabled = enabled && _historyPage is { Offset: > 0 };
        Required<Button>("ReloadHistoryButton").IsEnabled = enabled;
    }

    private async Task InspectHistorySelectionAsync()
    {
        if (_snapshot is null || _history is null || Required<ListBox>("HistoryCommits").SelectedItem is not HistoryCommitItem { Commit: var selected }) return;
        _choosingCommit?.Cancel(); _choosingCommit?.Dispose();
        var cancellation = _choosingCommit = new CancellationTokenSource(); var root = _snapshot.Root;
        // Stop the preceding comparison before reading another commit's metadata.
        _comparingRevisions?.Cancel();
        try
        {
            var detail = await _history.CommitAsync(root, selected.CommitId, cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested || _choosingCommit != cancellation || _snapshot?.Root != root) return;
            var endpoints = detail.Comparison();
            await CompareRevisionsAsync(new(endpoints.BaseName, endpoints.TargetName), endpoints, detail, preferredPath: _historyPage?.Path);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!cancellation.IsCancellationRequested && !_closed && _choosingCommit == cancellation)
                Required<TextBlock>("HistoryNotice").Text = error is InspectionException known ? known.Message : "The selected commit could not be read.";
        }
    }

    private void OpenCommitDetails()
    {
        if (_historyCommit is not { } commit) return;
        CloseRevisionPicker(false, false); CloseComparisonOptions(false);
        Required<TextBlock>("CommitIdentity").Text = commit.CommitId;
        Required<TextBlock>("CommitAuthor").Text = "Author: " + HistoryCommitItem.Preview(commit.Author, 256) + "\nCommitter: " + HistoryCommitItem.Preview(commit.Committer, 256);
        Required<TextBox>("CommitMessage").Text = commit.Message.Length <= 16000 ? commit.Message : commit.Message[..16000] + "\n\n[Message preview limited to 16,000 characters. Use the commit CLI command for the full local message.]";
        var parents = Required<ComboBox>("CommitParentSelector");
        parents.ItemsSource = commit.Parents.Count == 0 ? ["Empty tree (initial commit)"] : commit.Parents.Select((id, index) => $"Parent {index + 1} · {id[..12]}").ToArray();
        parents.SelectedIndex = _historyParent - 1;
        Required<Control>("CommitDetailsPanel").IsVisible = true; parents.Focus();
        _motion.Reveal(Required<Control>("CommitDetailsPanel"));
    }

    private async Task ApplyCommitParentAsync()
    {
        if (_historyCommit is not { } commit) return;
        var parent = Required<ComboBox>("CommitParentSelector").SelectedIndex + 1;
        var endpoints = commit.Comparison(parent);
        CloseCommitDetails();
        await CompareRevisionsAsync(new(endpoints.BaseName, endpoints.TargetName), endpoints, commit, parent, _historyPage?.Path);
    }

    private void CloseCommitDetails()
    {
        Required<Control>("CommitDetailsPanel").IsVisible = false;
        Required<Button>("RevisionEndpointsButton").Focus();
    }

    private void ResetHistory()
    {
        _listingHistory?.Cancel(); _choosingCommit?.Cancel();
        _historyPage = null; _historyCommit = null; _historyParent = 1;
        _updatingHistory = true;
        try { Required<ListBox>("HistoryCommits").ItemsSource = null; Required<ComboBox>("HistoryScopeSelector").SelectedIndex = 0; }
        finally { _updatingHistory = false; }
        ShowHistorySidebar(false); Required<Control>("CommitDetailsPanel").IsVisible = false;
        Required<TextBox>("HistoryStartInput").Text = "HEAD"; SetHistoryPaging(true);
    }

    private void ShowHistorySidebar(bool visible)
    {
        Required<Control>("HistorySidebar").IsVisible = visible;
        Required<Control>("ChangesNavigationHeader").IsVisible = !visible;
        _filter.IsVisible = _files.IsVisible = !visible;
    }
}
