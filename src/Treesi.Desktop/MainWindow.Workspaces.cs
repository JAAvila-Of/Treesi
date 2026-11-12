using Avalonia.Controls;
using Treesi.Application;
using Treesi.Desktop.Workspaces;
using Treesi.Domain;

namespace Treesi.Desktop;

public sealed partial class MainWindow
{
    private WorkspaceHistory _workspaceHistory = null!;
    private ListBox _recents = null!;
    private (string Path, WorkspaceEntry? Entry)? _failedWorkspace;
    private bool _isOpeningWorkspace;

    private void InitializeWorkspaces(IWorkspaceHistoryStore? store, string? initialPath)
    {
        _workspaceHistory = new WorkspaceHistory(store);
        _recents = Required<ListBox>("RecentRepositories");
        _recents.SelectionChanged += (_, _) => UpdateRecentActions();
        Required<Button>("RecentWorkspacesButton").Click += (_, _) => ShowRecentWorkspaces();
        Required<Button>("ReturnToWorkspaceButton").Click += (_, _) => { Cancel(); ShowComparison(); };
        Required<Button>("HomeOpenRepositoryButton").Click += async (_, _) => await PickAsync(false);
        Required<Button>("HomeOpenFileButton").Click += async (_, _) => await PickAsync(true);
        Required<Button>("ResumeWorkspaceButton").Click += (_, _) =>
        {
            if (_recents.SelectedItem is WorkspaceEntry entry) PendingInspection = OpenWorkspaceAsync(entry.Root, entry);
        };
        Required<Button>("RemoveRecentButton").Click += (_, _) =>
        {
            if (_recents.SelectedItem is not WorkspaceEntry entry) return;
            var saved = _workspaceHistory.Forget(entry.Root);
            RefreshRecentWorkspaces();
            ShowHomeNotice(saved ? "Shortcut removed. Repository files were not changed." : "Shortcut removed for this session; the change could not be saved.");
            FocusHome();
        };
        Required<Button>("ClearRecentsButton").Click += (_, _) =>
        {
            var saved = _workspaceHistory.Clear(); RefreshRecentWorkspaces();
            ShowHomeNotice(saved ? "Recent shortcuts cleared. Repository files were not changed." : "List cleared for this session; the change could not be saved.");
            FocusHome();
        };
        foreach (var name in new[] { "RetryOpenButton", "RetryWorkspaceButton" })
            Required<Button>(name).Click += (_, _) =>
            {
                if (_failedWorkspace is { } failed) PendingInspection = OpenWorkspaceAsync(failed.Path, failed.Entry);
            };
        Required<Button>("ChooseWorkspaceButton").Click += (_, _) => ShowRecentWorkspaces();
        Required<Button>("DismissRecoveryButton").Click += (_, _) => DismissOpenRecovery();
        _recents.KeyDown += (_, args) =>
        {
            if (args.Key != Avalonia.Input.Key.Enter || _recents.SelectedItem is not WorkspaceEntry entry) return;
            args.Handled = true; PendingInspection = OpenWorkspaceAsync(entry.Root, entry);
        };
        Opened += (_, _) => { if (Required<Control>("HomeSurface").IsVisible) FocusHome(); };
        if (initialPath is null) ShowRecentWorkspaces();
    }

    private WorkspaceEntry? CurrentWorkspace() => _snapshot is null ? null : new WorkspaceEntry(_snapshot.Root,
        (_files.SelectedItem as ChangedFileItem)?.Path,
        _comparison.SelectedIndex switch { 1 => Comparison.Staged, 2 => Comparison.Head, _ => Comparison.WorkingTree },
        _diffMode.SelectedIndex == 1, _filesPanelVisible);

    private void SaveCurrentWorkspace()
    {
        if (CurrentWorkspace() is { } entry && !_workspaceHistory.Update(entry))
            _status.Text = "Workspace remains available for this session; its view could not be saved.";
    }

    private void RefreshRecentWorkspaces()
    {
        _recents.ItemsSource = _workspaceHistory.Entries;
        _recents.SelectedIndex = _workspaceHistory.Entries.Count > 0 ? 0 : -1;
        Required<Control>("NoRecentWorkspaces").IsVisible = _workspaceHistory.Entries.Count == 0;
        UpdateRecentActions();
    }

    private void UpdateRecentActions()
    {
        var selected = _recents.SelectedItem is WorkspaceEntry;
        Required<Button>("ResumeWorkspaceButton").IsEnabled = selected;
        Required<Button>("RemoveRecentButton").IsEnabled = selected;
        Required<Button>("ClearRecentsButton").IsEnabled = _workspaceHistory.Entries.Count > 0;
    }

    private void ShowRecentWorkspaces()
    {
        CloseComparisonOptions(false);
        Cancel(); SaveCurrentWorkspace(); SetFocusMode(false);
        Required<Control>("OpenRecoveryNotice").IsVisible = false;
        Required<Control>("WorkspaceFrame").IsVisible = false;
        Required<Control>("HomeSurface").IsVisible = true;
        Required<Button>("ReturnToWorkspaceButton").IsVisible = _snapshot is not null;
        Required<Button>("FocusWorkspaceButton").IsEnabled = false;
        Required<Button>("FocusWorkspaceButton").IsVisible = false;
        Required<TextBlock>("WorkspaceContext").Text = "Home";
        Required<TextBlock>("HomeNotice").IsVisible = false;
        Required<Button>("RetryOpenButton").IsVisible = false;
        RefreshRecentWorkspaces();
        SetBusy(false);
        if (IsVisible)
        {
            FocusHome();
            _motion.Reveal(Required<Control>("HomeSurface"));
        }
    }

    private void ShowComparison()
    {
        Required<Control>("HomeSurface").IsVisible = false;
        Required<Control>("WorkspaceFrame").IsVisible = true;
        Required<Button>("FocusWorkspaceButton").IsEnabled = true;
        Required<Button>("FocusWorkspaceButton").IsVisible = true;
        Required<TextBlock>("WorkspaceContext").Text = "Workspace";
        FocusComparison();
    }

    private void FocusHome()
    {
        if (_workspaceHistory.Entries.Count > 0) _recents.Focus();
        else Required<Button>("HomeOpenRepositoryButton").Focus();
    }

    private void FocusComparison()
    {
        if (!ActiveEditor.TextArea.Focus()) Required<Button>("ToggleFilesButton").Focus();
    }

    private void ShowHomeNotice(string message)
    {
        var notice = Required<TextBlock>("HomeNotice");
        notice.Text = message; notice.IsVisible = true;
    }

    private void DismissOpenRecovery()
    {
        Required<Control>("OpenRecoveryNotice").IsVisible = false;
        if (!ActiveEditor.TextArea.Focus()) Required<Button>("RecentWorkspacesButton").Focus();
    }

    private void ShowOpenFailure(string path, WorkspaceEntry? entry, Exception exception)
    {
        _failedWorkspace = (path, entry);
        if (_snapshot is null && !Required<Control>("HomeSurface").IsVisible) ShowRecentWorkspaces();
        var message = exception is InspectionException inspection ? inspection.Message : "Check the location's permissions and choose a local Git working tree.";
        _status.Text = "Location could not be opened. Choose another location or retry.";
        if (Required<Control>("HomeSurface").IsVisible || _snapshot is null)
        {
            ShowHomeNotice(message);
            Required<Button>("RetryOpenButton").IsVisible = true;
        }
        else
        {
            Required<TextBlock>("OpenRecoveryMessage").Text = message;
            Required<Control>("OpenRecoveryNotice").IsVisible = true;
            Required<Button>("RetryWorkspaceButton").Focus();
        }
    }
}
