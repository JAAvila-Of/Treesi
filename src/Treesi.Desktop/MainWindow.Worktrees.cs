using Avalonia.Controls;
using Avalonia.Input;
using Treesi.Application;
using Treesi.Desktop.Workspaces;

namespace Treesi.Desktop;

public sealed partial class MainWindow
{
    private readonly IWorktreeService? _worktrees;
    private CancellationTokenSource? _listingWorktrees;

    private void InitializeWorktrees()
    {
        Required<Button>("DiscoverWorktreesButton").Click += (_, _) => OpenWorktrees();
        Required<Button>("CloseWorktreesButton").Click += (_, _) => CloseWorktrees();
        Required<Button>("RefreshWorktreesButton").Click += (_, _) => PendingInspection = LoadWorktreesAsync();
        Required<Button>("OpenWorktreeButton").Click += (_, _) => PendingInspection = OpenSelectedWorktreeAsync();
        Required<ListBox>("RelatedWorktrees").SelectionChanged += (_, _) => UpdateWorktreeSelection();
        Required<ListBox>("RelatedWorktrees").KeyDown += (_, args) => { if (args.Key == Key.Enter) { args.Handled = true; PendingInspection = OpenSelectedWorktreeAsync(); } };
    }

    private void OpenWorktrees()
    {
        if (_closed || _snapshot is null || _worktrees is null) return;
        CloseWorkspacePicker(false);
        Required<Control>("WorktreesPanel").IsVisible = true;
        Required<Button>("RefreshWorktreesButton").Focus();
        PendingInspection = LoadWorktreesAsync();
    }

    private void CloseWorktrees(bool focus = true)
    {
        _listingWorktrees?.Cancel(); _listingWorktrees?.Dispose(); _listingWorktrees = null;
        Required<Control>("WorktreesPanel").IsVisible = false;
        if (focus) Required<Button>("WorkspacePickerButton").Focus();
    }

    private async Task LoadWorktreesAsync()
    {
        if (_closed || _snapshot is null || _worktrees is null || !Required<Control>("WorktreesPanel").IsVisible) return;
        _listingWorktrees?.Cancel(); _listingWorktrees?.Dispose();
        var cancellation = _listingWorktrees = new CancellationTokenSource(); var root = _snapshot.Root;
        var list = Required<ListBox>("RelatedWorktrees"); list.ItemsSource = null; UpdateWorktreeSelection();
        Required<TextBlock>("WorktreeNotice").Text = "Reading registered worktrees…";
        try
        {
            var result = await _worktrees.ListAsync(root, cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested || _listingWorktrees != cancellation || _snapshot?.Root != root || !Required<Control>("WorktreesPanel").IsVisible) return;
            var choices = result.Items.Select(item => new WorktreeChoice(item, SessionRootComparer.Equals(SessionRoot(item.Root), SessionRoot(root)), FindRepositoryTab(item.Root) is not null)).ToArray();
            list.ItemsSource = choices; list.SelectedItem = choices.FirstOrDefault(item => item.IsCurrent) ?? choices.FirstOrDefault();
            Required<TextBlock>("WorktreeNotice").Text = $"{choices.Length} registered locations · Choose one to open. Other folders were not inspected.";
            ListRegion(list, Required<Button>("RefreshWorktreesButton")).Focus();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!_closed && !cancellation.IsCancellationRequested && _listingWorktrees == cancellation)
                Required<TextBlock>("WorktreeNotice").Text = error is Treesi.Domain.InspectionException known ? known.Message : "Registered worktrees could not be read. Refresh to retry.";
        }
    }

    private void UpdateWorktreeSelection()
    {
        var choice = Required<ListBox>("RelatedWorktrees").SelectedItem as WorktreeChoice;
        Required<Button>("OpenWorktreeButton").IsEnabled = choice?.CanOpen == true;
        Required<TextBlock>("WorktreeDetails").Text = choice?.Details ?? "";
    }

    private async Task OpenSelectedWorktreeAsync()
    {
        if (Required<ListBox>("RelatedWorktrees").SelectedItem is not WorktreeChoice { CanOpen: true } choice) return;
        CloseWorktrees(false);
        if (FindRepositoryTab(choice.Worktree.Root) is { } tab) { SelectRepositoryTab(tab); ActivateRepositoryTab(tab); return; }
        await OpenRepositoryAsync(choice.Worktree.Root);
    }
}
