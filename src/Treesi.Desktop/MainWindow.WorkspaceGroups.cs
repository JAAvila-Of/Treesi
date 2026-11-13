using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Treesi.Application;
using Treesi.Desktop.Workspaces;
using Treesi.Domain;

namespace Treesi.Desktop;

public sealed partial class MainWindow
{
    private WorkspaceCatalog _workspaceCatalog = null!;
    private bool _updatingWorkspacePicker, _creatingWorkspace, _checkingWorkspaceRoot;
    private CancellationTokenSource? _addingWorkspaceRepository;

    private Guid? SelectedWorkspaceId => _workspaceCatalog.Document.SelectedWorkspaceId;

    private void InitializeWorkspaceGroups(IWorkspaceCatalogStore? store)
    {
        _workspaceCatalog = new(store);
        Required<Button>("WorkspacePickerButton").Click += (_, _) => OpenWorkspacePicker();
        Required<Button>("CloseWorkspacePickerButton").Click += (_, _) => CloseWorkspacePicker();
        Required<Button>("NewWorkspaceGroupButton").Click += (_, _) =>
        {
            CancelWorkspaceRootCheck(); _creatingWorkspace = true;
            Required<TextBox>("WorkspaceNameInput").Text = "";
            UpdateWorkspaceGroupActions(); Required<TextBox>("WorkspaceNameInput").Focus();
            ShowWorkspaceGroupNotice("Choose a name for the new workspace. Selecting a group discards this draft.");
        };
        Required<Button>("CancelWorkspaceNameButton").Click += (_, _) => RefreshWorkspacePicker();
        Required<Button>("SaveWorkspaceNameButton").Click += (_, _) => EditWorkspaceGroup(() =>
        {
            var name = Required<TextBox>("WorkspaceNameInput").Text ?? "";
            if (_creatingWorkspace) _workspaceCatalog.Create(name);
            else if (SelectedWorkspaceId is { } id) _workspaceCatalog.Rename(id, name);
        }, _creatingWorkspace ? "Workspace created." : "Workspace renamed.");
        Required<Button>("DeleteWorkspaceGroupButton").Click += (_, _) => EditWorkspaceGroup(() =>
        {
            if (SelectedWorkspaceId is { } id) _workspaceCatalog.Remove(id);
        }, "Group removed. Repository files and open tabs were not changed.");
        Required<Button>("AddCurrentWorkspaceRepositoryButton").Click += (_, _) => EditWorkspaceGroup(() =>
        {
            if (SelectedWorkspaceId is { } id && _snapshot is { } snapshot) _workspaceCatalog.AddRepository(id, snapshot.Root);
        }, "Current repository added to the workspace.");
        Required<Button>("AddWorkspaceRepositoryButton").Click += async (_, _) => await PickWorkspaceRepositoryAsync(false);
        Required<Button>("ReplaceWorkspaceRepositoryButton").Click += async (_, _) => await PickWorkspaceRepositoryAsync(true);
        Required<Button>("CancelWorkspaceValidationButton").Click += (_, _) =>
        {
            CancelWorkspaceRootCheck(); UpdateWorkspaceGroupActions();
            ShowWorkspaceGroupNotice("Location validation cancelled. Workspace shortcuts were not changed.");
        };
        Required<Button>("RemoveWorkspaceRepositoryButton").Click += (_, _) => EditWorkspaceGroup(() =>
        {
            if (SelectedWorkspaceId is { } id && SelectedWorkspaceRepository is { } item) _workspaceCatalog.RemoveRepository(id, item.Root);
        }, "Shortcut removed. Repository files and open tabs were not changed.");
        Required<Button>("OpenWorkspaceRepositoryButton").Click += (_, _) => PendingInspection = OpenWorkspaceRepositoryAsync();
        Required<ListBox>("WorkspaceGroupsList").SelectionChanged += (_, _) =>
        {
            if (_updatingWorkspacePicker || Required<ListBox>("WorkspaceGroupsList").SelectedItem is not WorkspaceGroupChoice group) return;
            CancelWorkspaceRootCheck(); _workspaceCatalog.Select(group.Id);
            RefreshWorkspacePicker(); ShowWorkspaceGroupNotice("Group selected. Open tabs remain available; no repositories were read.");
        };
        Required<ListBox>("WorkspaceRepositoriesList").SelectionChanged += (_, _) => UpdateWorkspaceGroupActions();
        Required<ListBox>("WorkspaceRepositoriesList").KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && SelectedWorkspaceRepository is not null && !_checkingWorkspaceRoot && !_creatingWorkspace)
            { args.Handled = true; PendingInspection = OpenWorkspaceRepositoryAsync(); }
        };
        UpdateWorkspacePickerCaption();
    }

    private WorkspaceRepositoryChoice? SelectedWorkspaceRepository => Required<ListBox>("WorkspaceRepositoriesList").SelectedItem as WorkspaceRepositoryChoice;

    private void OpenWorkspacePicker()
    {
        CloseWorktrees(false);
        Cancel(); CloseComparisonOptions(false); CloseRevisionPicker(true, false);
        Required<Control>("CommitDetailsPanel").IsVisible = false;
        Required<Control>("OpenRecoveryNotice").IsVisible = false;
        RefreshWorkspacePicker();
        Required<Control>("WorkspacePickerPanel").IsVisible = true;
        ShowWorkspaceGroupNotice(_workspaceCatalog.LoadNotice ?? "Groups organize local shortcuts. Open tabs stay available when you switch groups.");
        ListRegion(Required<ListBox>("WorkspaceGroupsList"), Required<Button>("NewWorkspaceGroupButton")).Focus();
        _motion.Reveal(Required<Control>("WorkspacePickerPanel"));
    }

    private void CloseWorkspacePicker(bool focus = true)
    {
        CloseWorktrees(false);
        CancelWorkspaceRootCheck();
        Required<Control>("WorkspacePickerPanel").IsVisible = false;
        if (focus) Required<Button>("WorkspacePickerButton").Focus();
    }

    private void RefreshWorkspacePicker(string? preferredRoot = null)
    {
        _updatingWorkspacePicker = true; _creatingWorkspace = false;
        try
        {
            var groups = new[] { new WorkspaceGroupChoice(null, "Recent repositories", _workspaceHistory.Entries.Count) }
                .Concat(_workspaceCatalog.Document.Workspaces.Select(item => new WorkspaceGroupChoice(item.Id, item.Name, item.RepositoryRoots.Count))).ToArray();
            var list = Required<ListBox>("WorkspaceGroupsList"); list.ItemsSource = groups;
            list.SelectedItem = groups.Single(item => item.Id == SelectedWorkspaceId);
            var selected = _workspaceCatalog.Document.Workspaces.FirstOrDefault(item => item.Id == SelectedWorkspaceId);
            Required<TextBox>("WorkspaceNameInput").Text = selected?.Name ?? "";
            var roots = selected?.RepositoryRoots ?? _workspaceHistory.Entries.Select(item => item.Root).ToArray();
            var items = roots.Select(root => new WorkspaceRepositoryChoice(root, FindRepositoryTab(root) is not null)).ToArray();
            var repositories = Required<ListBox>("WorkspaceRepositoriesList"); repositories.ItemsSource = items;
            repositories.SelectedItem = items.FirstOrDefault(item => WorkspaceCatalog.RootComparer.Equals(item.Root, preferredRoot)) ?? items.FirstOrDefault();
            Required<Control>("EmptyWorkspaceGroup").IsVisible = items.Length == 0;
            UpdateWorkspacePickerCaption(); UpdateWorkspaceGroupActions();
        }
        finally { _updatingWorkspacePicker = false; }
    }

    private void UpdateWorkspacePickerCaption()
    {
        var name = _workspaceCatalog.Document.Workspaces.FirstOrDefault(item => item.Id == SelectedWorkspaceId)?.Name;
        Required<TextBlock>("WorkspacePickerCaption").Text = name ?? "Local repositories";
        ToolTip.SetTip(Required<TextBlock>("WorkspacePickerCaption"), name ?? "Local repositories");
        AutomationProperties.SetName(Required<Button>("WorkspacePickerButton"), $"Choose local workspace. {name ?? "Recent repositories"} selected.");
    }

    private void UpdateWorkspaceGroupActions()
    {
        if (_workspaceCatalog is null) return;
        var group = _workspaceCatalog.Document.Workspaces.FirstOrDefault(item => item.Id == SelectedWorkspaceId);
        var editing = group is not null && !_creatingWorkspace && !_checkingWorkspaceRoot;
        var selected = SelectedWorkspaceRepository is not null;
        Required<Button>("DiscoverWorktreesButton").IsEnabled = _snapshot is not null && _worktrees is not null;
        Required<TextBox>("WorkspaceNameInput").IsEnabled = !_checkingWorkspaceRoot && (_creatingWorkspace || group is not null);
        Required<Button>("SaveWorkspaceNameButton").IsEnabled = !_checkingWorkspaceRoot && (_creatingWorkspace || group is not null);
        Required<Button>("SaveWorkspaceNameButton").Content = _creatingWorkspace ? "Create" : "Save name";
        Required<Button>("CancelWorkspaceNameButton").IsVisible = _creatingWorkspace;
        Required<Button>("DeleteWorkspaceGroupButton").IsEnabled = editing;
        Required<Button>("NewWorkspaceGroupButton").IsEnabled = !_checkingWorkspaceRoot && _workspaceCatalog.Document.Workspaces.Count < WorkspaceCatalog.MaximumWorkspaces;
        Required<Button>("AddWorkspaceRepositoryButton").IsEnabled = editing && group!.RepositoryRoots.Count < WorkspaceCatalog.MaximumRepositories;
        Required<Button>("AddCurrentWorkspaceRepositoryButton").IsEnabled = editing && _snapshot is not null && group!.RepositoryRoots.Count < WorkspaceCatalog.MaximumRepositories && !group.RepositoryRoots.Contains(_snapshot.Root, WorkspaceCatalog.RootComparer);
        Required<Button>("ReplaceWorkspaceRepositoryButton").IsEnabled = editing && selected;
        Required<Button>("RemoveWorkspaceRepositoryButton").IsEnabled = editing && selected;
        Required<Button>("OpenWorkspaceRepositoryButton").IsEnabled = selected && !_checkingWorkspaceRoot && !_creatingWorkspace;
        Required<Control>("CancelWorkspaceValidationButton").IsVisible = _checkingWorkspaceRoot;
        Required<Control>("WorkspaceValidationIndicator").IsVisible = _checkingWorkspaceRoot;
        Required<Control>("WorkspaceRepositoriesList").IsEnabled = !_creatingWorkspace;
    }

    private void ShowWorkspaceGroupNotice(string message)
    {
        Required<TextBlock>("WorkspaceGroupNotice").Text = message;
        ToolTip.SetTip(Required<TextBlock>("WorkspaceGroupNotice"), message);
        Required<TextBlock>("WorkspaceSessionNotice").IsVisible = _workspaceCatalog.SessionOnly;
    }

    private void EditWorkspaceGroup(Action edit, string success)
    {
        CancelWorkspaceRootCheck();
        try
        {
            edit(); RefreshWorkspacePicker();
            ShowWorkspaceGroupNotice(_workspaceCatalog.SessionOnly ? success + " Changes are available for this session; they could not be saved." : success);
        }
        catch (InspectionException error) { ShowWorkspaceGroupNotice(error.Message); }
    }

    private async Task OpenWorkspaceRepositoryAsync()
    {
        if (SelectedWorkspaceRepository is not { } item || _checkingWorkspaceRoot || _creatingWorkspace) return;
        CloseWorkspacePicker(false);
        if (FindRepositoryTab(item.Root) is { } tab)
        { SelectRepositoryTab(tab); ActivateRepositoryTab(tab); return; }
        var recent = _workspaceHistory.Entries.FirstOrDefault(entry => WorkspaceCatalog.RootComparer.Equals(entry.Root, item.Root));
        await OpenWorkspaceAsync(item.Root, recent);
    }

    private async Task PickWorkspaceRepositoryAsync(bool replace)
    {
        if (_closed || SelectedWorkspaceId is not { } id || _creatingWorkspace || _checkingWorkspaceRoot) return;
        var previous = replace ? SelectedWorkspaceRepository?.Root : null;
        if (replace && previous is null) return;
        var selection = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = replace ? "Choose a replacement Git repository location" : "Add a local Git repository to this workspace",
            AllowMultiple = false
        });
        if (_closed || selection.FirstOrDefault()?.TryGetLocalPath() is not { } path || SelectedWorkspaceId != id || !Required<Control>("WorkspacePickerPanel").IsVisible) return;
        if (replace && SelectedWorkspaceRepository?.Root != previous) return;
        await AddRepositoryToSelectedWorkspaceAsync(path, replace);
    }

    public Task AddRepositoryToSelectedWorkspaceAsync(string path, bool replaceSelected = false)
    {
        if (_closed || SelectedWorkspaceId is not { } id || _creatingWorkspace || _checkingWorkspaceRoot || !Required<Control>("WorkspacePickerPanel").IsVisible) return Task.CompletedTask;
        var previous = replaceSelected ? SelectedWorkspaceRepository?.Root : null;
        if (replaceSelected && previous is null) return Task.CompletedTask;
        return PendingInspection = ValidateWorkspaceRepositoryAsync(id, path, previous);
    }

    private async Task ValidateWorkspaceRepositoryAsync(Guid id, string path, string? replace)
    {
        CancelWorkspaceRootCheck();
        var cancellation = _addingWorkspaceRepository = new CancellationTokenSource();
        _checkingWorkspaceRoot = true; UpdateWorkspaceGroupActions();
        ShowWorkspaceGroupNotice("Validating the selected local Git repository…");
        try
        {
            var snapshot = await _inspector.InspectAsync(path, cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested || _addingWorkspaceRepository != cancellation || SelectedWorkspaceId != id) return;
            if (replace is null) _workspaceCatalog.AddRepository(id, snapshot.Root);
            else _workspaceCatalog.ReplaceRepository(id, replace, snapshot.Root);
            RefreshWorkspacePicker(snapshot.Root);
            ShowWorkspaceGroupNotice(_workspaceCatalog.SessionOnly ? "Shortcut updated for this session; settings could not be saved." : "Shortcut updated. No repository was opened or moved.");
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!_closed && !cancellation.IsCancellationRequested && _addingWorkspaceRepository == cancellation)
                ShowWorkspaceGroupNotice(error is InspectionException known ? known.Message : "The location could not be validated. Choose a local Git working tree.");
        }
        finally
        {
            if (_addingWorkspaceRepository == cancellation)
            { _checkingWorkspaceRoot = false; UpdateWorkspaceGroupActions(); }
        }
    }

    private void CancelWorkspaceRootCheck()
    {
        _addingWorkspaceRepository?.Cancel(); _addingWorkspaceRepository?.Dispose(); _addingWorkspaceRepository = null;
        _checkingWorkspaceRoot = false;
    }
}
