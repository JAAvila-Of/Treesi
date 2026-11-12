using Avalonia.Automation;
using Avalonia.Controls;
using Treesi.Application;
using Treesi.Domain;

namespace Treesi.Desktop;

public sealed partial class MainWindow
{
    private readonly IRevisionService? _revisions;
    private RevisionComparison? _revisionComparison;
    private CancellationTokenSource? _comparingRevisions, _listingReferences;
    private string? _workingSelectedPath;
    private bool _isComparingRevisions;

    private void InitializeRevisions()
    {
        Required<Button>("CompareRevisionsButton").Click += (_, _) => OpenRevisionPicker();
        Required<Button>("RevisionEndpointsButton").Click += (_, _) => { if (_historyCommit is null) OpenRevisionPicker(); else OpenCommitDetails(); };
        Required<Button>("CancelRevisionButton").Click += (_, _) => CloseRevisionPicker();
        Required<Button>("BackToRevisionDraftButton").Click += (_, _) => ShowReferencePicker(false);
        Required<Button>("BrowseRevisionReferencesButton").Click += (_, _) => PendingInspection = LoadReferencesAsync();
        Required<Button>("ApplyRevisionButton").Click += (_, _) => PendingInspection = ApplyRevisionDraftAsync();
        Required<Button>("ReturnToWorkingButton").Click += (_, _) => { if (_snapshot is not null) PendingInspection = OpenRepositoryAsync(_snapshot.Root); };
        Required<Button>("SwapRevisionsButton").Click += (_, _) =>
        {
            var from = Required<TextBox>("BaseRevisionInput"); var to = Required<TextBox>("TargetRevisionInput");
            (from.Text, to.Text) = (to.Text, from.Text);
        };
        Required<Button>("UseReferenceAsBaseButton").Click += (_, _) => UseReference("BaseRevisionInput");
        Required<Button>("UseReferenceAsTargetButton").Click += (_, _) => UseReference("TargetRevisionInput");
        Required<ListBox>("RevisionReferencesList").SelectionChanged += (_, _) => UpdateReferenceActions();
    }

    private void OpenRevisionPicker()
    {
        if (_snapshot is null || _revisions is null) return;
        Required<Control>("CommitDetailsPanel").IsVisible = false;
        CloseComparisonOptions(false);
        var current = _revisionComparison?.Endpoints;
        Required<TextBox>("BaseRevisionInput").Text = current?.BaseName ?? "HEAD~1";
        Required<TextBox>("TargetRevisionInput").Text = current?.TargetName ?? "HEAD";
        Required<ComboBox>("RevisionModeSelector").SelectedIndex = (int)(current?.Mode ?? RevisionComparisonMode.Direct);
        Required<Control>("RevisionError").IsVisible = false;
        Required<Control>("ReturnToWorkingButton").IsVisible = current is not null;
        ShowReferencePicker(false);
        Required<Control>("RevisionPanel").IsVisible = true;
        var input = Required<TextBox>("BaseRevisionInput"); input.Focus(); input.SelectAll();
        _motion.Reveal(Required<Control>("RevisionPanel"));
    }

    private void CloseRevisionPicker(bool cancel = true, bool focus = true)
    {
        if (cancel) _comparingRevisions?.Cancel();
        _listingReferences?.Cancel();
        Required<Control>("RevisionPanel").IsVisible = false;
        if (focus)
        {
            if (_revisionComparison is not null) Required<Button>("RevisionEndpointsButton").Focus();
            else Required<Button>("CompareRevisionsButton").Focus();
        }
    }

    private void ShowReferencePicker(bool show)
    {
        Required<Grid>("RevisionPanelLayout").Height = show || Required<Control>("RevisionError").IsVisible ? 400 : 320;
        Required<Control>("RevisionDraft").IsVisible = !show;
        Required<Control>("RevisionDraftActions").IsVisible = !show;
        Required<Control>("RevisionReferencePicker").IsVisible = show;
        Required<Control>("RevisionReferenceActions").IsVisible = show;
        if (!show) _listingReferences?.Cancel();
    }

    private void UseReference(string input)
    {
        if (Required<ListBox>("RevisionReferencesList").SelectedItem is not RevisionReference selected) return;
        Required<TextBox>(input).Text = selected.Name;
        ShowReferencePicker(false); Required<TextBox>(input).Focus();
    }

    private void UpdateReferenceActions()
    {
        var selected = Required<ListBox>("RevisionReferencesList").SelectedItem is RevisionReference;
        Required<Button>("UseReferenceAsBaseButton").IsEnabled = selected;
        Required<Button>("UseReferenceAsTargetButton").IsEnabled = selected;
    }

    private async Task LoadReferencesAsync()
    {
        if (_snapshot is null || _revisions is null) return;
        _listingReferences?.Cancel(); _listingReferences?.Dispose();
        var cancellation = _listingReferences = new CancellationTokenSource();
        var root = _snapshot.Root;
        var list = Required<ListBox>("RevisionReferencesList"); list.ItemsSource = null;
        ShowReferencePicker(true); UpdateReferenceActions();
        Required<TextBlock>("RevisionReferenceNotice").Text = "Reading local references…";
        try
        {
            var references = await _revisions.ReferencesAsync(root, cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested || _listingReferences != cancellation || _snapshot?.Root != root) return;
            list.ItemsSource = references.Items; list.SelectedIndex = references.Items.Count > 0 ? 0 : -1;
            Required<TextBlock>("RevisionReferenceNotice").Text = references.HasMore ? "First 200 entries shown. Type a full ref or commit ID if omitted." :
                references.Items.Count == 0 ? "No references are available locally. Return and enter an available commit ID." : "Choose a local reference. Its commit is resolved when you compare.";
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!cancellation.IsCancellationRequested && !_closed) Required<TextBlock>("RevisionReferenceNotice").Text = error is InspectionException known ? known.Message : "Local references could not be read.";
        }
    }

    private async Task ApplyRevisionDraftAsync()
    {
        var selection = new RevisionSelection(Required<TextBox>("BaseRevisionInput").Text ?? "", Required<TextBox>("TargetRevisionInput").Text ?? "",
            (RevisionComparisonMode)Required<ComboBox>("RevisionModeSelector").SelectedIndex);
        try { selection.Validate(); }
        catch (InspectionException error) { ShowRevisionError(error.Message); return; }
        await CompareRevisionsAsync(selection);
    }

    private async Task CompareRevisionsAsync(RevisionSelection selection, RevisionEndpoints? pinned = null, CommitDetail? commit = null, int parent = 1, string? preferredPath = null)
    {
        if (_snapshot is null || _revisions is null) return;
        Cancel(); SaveCurrentWorkspace();
        _comparingRevisions?.Dispose();
        var cancellation = _comparingRevisions = new CancellationTokenSource();
        var root = _snapshot.Root;
        var preferred = preferredPath ?? (_files.SelectedItem as ChangedFileItem)?.Path;
        var working = _revisionComparison is null ? preferred : _workingSelectedPath;
        _isComparingRevisions = true; SetRevisionInputAvailability(false);
        Required<Control>("RevisionError").IsVisible = false;
        SetBusy(true); _status.Text = "Resolving local commits…";
        try
        {
            var endpoints = pinned ?? await _revisions.ResolveAsync(root, selection, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var comparison = await _revisions.CompareAsync(endpoints, cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested || _comparingRevisions != cancellation || _snapshot?.Root != root) return;
            _revisionComparison = comparison; _workingSelectedPath = working;
            _historyCommit = commit; _historyParent = parent;
            _allFiles = comparison.Files.Select(file => new FileChange(file.Path, null, ".", file.Status)).ToArray();
            _updatingFiles = true; _filter.Text = ""; _updatingFiles = false;
            _fileCount.Text = comparison.Files.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            UpdateRevisionChrome(); ShowComparison(); CloseRevisionPicker(false, false);
            _isComparingRevisions = false;
            FilterFiles(preferred);
            await PendingInspection;
        }
        catch (OperationCanceledException) { if (_comparingRevisions == cancellation && !_closed) _status.Text = "Revision comparison cancelled; no new comparison was loaded."; }
        catch (Exception error)
        {
            if (!_closed && !cancellation.IsCancellationRequested && _comparingRevisions == cancellation)
            {
                if (!Required<Control>("RevisionPanel").IsVisible) OpenRevisionPicker();
                ShowRevisionError(error is InspectionException known ? known.Message : "The local comparison could not be loaded.");
            }
        }
        finally
        {
            if (_comparingRevisions == cancellation)
            {
                _isComparingRevisions = false;
                if (!_isOpeningWorkspace) SetRevisionInputAvailability(true);
                SetBusy(false);
            }
        }
    }

    private void ShowRevisionError(string message)
    {
        var error = Required<TextBlock>("RevisionError"); error.Text = message; error.IsVisible = true;
        Required<Grid>("RevisionPanelLayout").Height = 400;
        _status.Text = "Revision comparison stopped; the previous comparison remains available.";
    }

    private void SetRevisionInputAvailability(bool enabled)
    {
        _files.IsEnabled = enabled;
        _filter.IsEnabled = _comparison.IsEnabled = enabled && _snapshot is not null;
        Required<Button>("DiffOptionsButton").IsEnabled = enabled && _snapshot is not null;
        Required<Button>("CompareRevisionsButton").IsEnabled = enabled && _snapshot is not null && _revisions is not null;
        Required<Button>("HistoryButton").IsEnabled = enabled && _snapshot is not null && _history is not null && _revisions is not null;
    }

    private void ClearRevisionComparison()
    {
        _revisionComparison = null; _workingSelectedPath = null;
        ResetHistory();
        UpdateRevisionChrome();
    }

    private void UpdateRevisionChrome()
    {
        var endpoints = _revisionComparison?.Endpoints;
        _comparison.IsVisible = endpoints is null;
        var button = Required<Button>("RevisionEndpointsButton"); button.IsVisible = endpoints is not null;
        Required<TextBlock>("ChangesHeading").Text = endpoints is null ? "Changes" : "Between commits";
        Required<TextBlock>("WorkspaceScopeHint").Text = endpoints is null ? "Local workspace" : "Pinned local commits";
        Required<TextBlock>("WorkspaceContext").Text = endpoints is null ? "Workspace" : "Revisions";
        if (endpoints is null) return;
        var kind = endpoints.IsRootComparison ? "Empty tree" : _historyCommit is not null ? $"Parent {_historyParent}" : endpoints.Mode == RevisionComparisonMode.MergeBase ? "Merge base" : "Direct";
        button.Content = endpoints.IsRootComparison ? $"Empty tree → {endpoints.TargetCommitId[..6]}" : $"{kind} {endpoints.EffectiveBaseCommitId[..6]} → {endpoints.TargetCommitId[..6]}";
        var description = $"{kind}: {endpoints.BaseName} ({endpoints.BaseCommitId}) to {endpoints.TargetName} ({endpoints.TargetCommitId}). Effective base: {endpoints.EffectiveBaseCommitId}. Refresh resolves names again.";
        ToolTip.SetTip(button, description); AutomationProperties.SetName(button, description);
    }

    private Task RefreshWorkspaceAsync()
    {
        if (_revisionComparison is { Endpoints: var endpoints })
            return CompareRevisionsAsync(new(endpoints.BaseName, endpoints.TargetName, endpoints.Mode), _historyCommit is null ? null : endpoints, _historyCommit, _historyParent);
        return _snapshot is null ? Task.CompletedTask : OpenRepositoryAsync(_snapshot.Root);
    }
}
