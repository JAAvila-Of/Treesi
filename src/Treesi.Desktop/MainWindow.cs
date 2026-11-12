using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using AvaloniaEdit;
using Treesi.Application;
using Treesi.Desktop.Appearance;
using Treesi.Desktop.Workspaces;
using Treesi.Domain;

namespace Treesi.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly IInspectionService _inspector;
    private readonly ThemeController _appearance;
    private readonly TextBlock _repositoryName, _repositoryLabel, _fileTitle, _comparisonLabel, _status, _fileCount, _added, _removed, _emptyTitle, _emptyDescription;
    private readonly ListBox _files;
    private readonly TextBox _filter, _search;
    private readonly TextEditor _diff;
    private readonly ComboBox _comparison, _themes;
    private readonly Button _refresh, _cancel, _findNext;
    private readonly Control _emptyState, _diffLabel;
    private readonly DiffDecoration _decoration;
    private readonly WorkspaceMotion _motion;
    private IReadOnlyList<FileChange> _allFiles = [];
    private RepositorySnapshot? _snapshot;
    private CancellationTokenSource? _opening, _loading;
    private bool _updatingFiles;
    private bool _closed;
    public Task PendingInspection { get; private set; } = Task.CompletedTask;
    public Task PendingMotion => _motion.Pending;

    public MainWindow(IInspectionService inspector, string? initialPath, ThemeController appearance, IMotionPreferenceStore? motionPreferences = null, IWorkspaceHistoryStore? workspaceHistory = null)
    {
        _inspector = inspector;
        _appearance = appearance;
        AvaloniaXamlLoader.Load(this);
        _repositoryName = Required<TextBlock>("RepositoryName"); _repositoryLabel = Required<TextBlock>("RepositoryLocation");
        _fileTitle = Required<TextBlock>("FileTitle"); _comparisonLabel = Required<TextBlock>("ComparisonDescription");
        _status = Required<TextBlock>("OperationStatus"); _fileCount = Required<TextBlock>("FileCount");
        _added = Required<TextBlock>("AddedCount"); _removed = Required<TextBlock>("RemovedCount");
        _emptyTitle = Required<TextBlock>("EmptyTitle"); _emptyDescription = Required<TextBlock>("EmptyDescription");
        _files = Required<ListBox>("ChangedFiles"); _filter = Required<TextBox>("FileFilter");
        _search = Required<TextBox>("DiffSearch"); _diff = Required<TextEditor>("DiffEditor");
        _comparison = Required<ComboBox>("ComparisonSelector"); _themes = Required<ComboBox>("ThemeSelector");
        _refresh = Required<Button>("RefreshButton"); _cancel = Required<Button>("CancelButton");
        _findNext = Required<Button>("FindNextButton");
        _filter.IsEnabled = false; _comparison.IsEnabled = false;
        _search.IsEnabled = false; _findNext.IsEnabled = false;
        _emptyState = Required<Control>("EmptyState"); _diffLabel = Required<Control>("DiffLabel");
        _diff.Options.EnableHyperlinks = false;
        _diff.Options.EnableEmailHyperlinks = false;
        _diff.Options.AllowScrollBelowDocument = false;
        _decoration = new DiffDecoration(_diff);
        _motion = new WorkspaceMotion(this) { Reduced = motionPreferences?.Load() ?? false };
        var reduceMotion = Required<CheckBox>("ReduceMotionToggle");
        reduceMotion.IsChecked = _motion.Reduced;
        reduceMotion.PropertyChanged += (_, args) =>
        {
            if (args.Property != CheckBox.IsCheckedProperty) return;
            _motion.Reduced = reduceMotion.IsChecked == true;
            if (_motion.Reduced) Required<ProgressBar>("BusyIndicator").IsIndeterminate = false;
            if (motionPreferences is not null && !motionPreferences.Save(_motion.Reduced))
                _status.Text = "Motion preference changed for this session; it could not be saved.";
        };
        InitializeDiffWorkspace();
        InitializePresentation();
        _themes.ItemsSource = ThemeCatalog.All;
        _themes.SelectedItem = appearance.Current;
        _themes.SelectionChanged += (_, _) =>
        {
            if (_themes.SelectedItem is ThemeDefinition theme && !_appearance.Apply(theme))
                _status.Text = "Theme changed for this session; the preference could not be saved.";
        };
        _appearance.Changed += OnThemeChanged;
        _files.SelectionChanged += (_, _) => { if (!_updatingFiles) PendingInspection = LoadDiffAsync(); };
        _comparison.SelectionChanged += (_, _) => { if (!_updatingFiles) PendingInspection = LoadDiffAsync(); };
        _filter.PropertyChanged += (_, args) => { if (args.Property == TextBox.TextProperty) FilterFiles(); };
        Required<Button>("OpenFolderButton").Click += async (_, _) => await PickAsync(false);
        Required<Button>("OpenFileButton").Click += async (_, _) => await PickAsync(true);
        _refresh.Click += async (_, _) => { if (_snapshot is not null) await OpenRepositoryAsync(_snapshot.Root); };
        _cancel.Click += (_, _) => { Cancel(); _status.Text = _isOpeningWorkspace ? "Cancelling opening…" : "Cancelling inspection…"; };
        _findNext.Click += (_, _) => FindNext();
        _search.KeyDown += (_, args) => { if (args.Key == Key.Enter) { FindNext(); args.Handled = true; } };
        KeyDown += async (_, args) =>
        {
            if (!Required<Control>("HomeSurface").IsVisible && (args.KeyModifiers & KeyModifiers.Alt) != 0 && args.Key is Key.Up or Key.Down)
            {
                NavigateBlock(args.Key == Key.Up ? -1 : 1); args.Handled = true; return;
            }
            if (args.Key == Key.Escape)
            {
                if (Required<Control>("OpenRecoveryNotice").IsVisible) DismissOpenRecovery();
                else if (_isOpeningWorkspace) Cancel();
                else if (Required<Control>("HomeSurface").IsVisible && _snapshot is not null) ShowComparison();
                else if (_focusMode) SetFocusMode(false);
                else Cancel();
                args.Handled = true;
                return;
            }
            if ((args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0) return;
            switch (args.Key)
            {
                case Key.O: args.Handled = true; await PickAsync(false); break;
                case Key.R: args.Handled = true; if (_snapshot is not null) await OpenRepositoryAsync(_snapshot.Root); break;
                case Key.B:
                    if (Required<Control>("HomeSurface").IsVisible) break;
                    args.Handled = true; ToggleFilesPanel(); break;
                case Key.F:
                    if (Required<Control>("HomeSurface").IsVisible) break;
                    args.Handled = true;
                    if ((args.KeyModifiers & KeyModifiers.Shift) != 0) SetFocusMode(!_focusMode);
                    else _search.Focus();
                    break;
            }
        };
        InitializeWorkspaces(workspaceHistory, initialPath);
        Closed += (_, _) =>
        {
            SaveCurrentWorkspace();
            _closed = true;
            Cancel(); _appearance.Changed -= OnThemeChanged;
            _opening?.Dispose(); _opening = null;
            _loading?.Dispose(); _loading = null;
            _decoration.Dispose();
            _beforeDecoration.Dispose(); _afterDecoration.Dispose();
            _motion.Dispose();
        };
        if (initialPath is not null) Opened += async (_, _) => await OpenRepositoryAsync(initialPath);
    }

    private T Required<T>(string name) where T : Control => this.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing control: {name}");

    private void OnThemeChanged(ThemeDefinition theme)
    {
        _themes.SelectedItem = theme;
        _decoration.Refresh();
        _beforeDecoration.Refresh(); _afterDecoration.Refresh();
        _motion.Reveal(Required<Control>("FileHeading"));
    }

    private async Task PickAsync(bool file)
    {
        if (file)
        {
            var selection = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open a file inside a Git repository", AllowMultiple = false });
            if (selection.FirstOrDefault()?.TryGetLocalPath() is { } path) await OpenRepositoryAsync(path);
        }
        else
        {
            var selection = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Open a Git repository", AllowMultiple = false });
            if (selection.FirstOrDefault()?.TryGetLocalPath() is { } path) await OpenRepositoryAsync(path);
        }
    }

    public Task OpenRepositoryAsync(string path) => OpenWorkspaceAsync(path, null);

    private async Task OpenWorkspaceAsync(string path, WorkspaceEntry? resume)
    {
        Cancel();
        SaveCurrentWorkspace();
        var previous = (_files.SelectedItem as ChangedFileItem)?.Path;
        var previousRoot = _snapshot?.Root;
        var wasHome = Required<Control>("HomeSurface").IsVisible;
        _opening?.Dispose();
        var cancellation = _opening = new CancellationTokenSource();
        _isOpeningWorkspace = true;
        _files.IsEnabled = false; _filter.IsEnabled = false; _comparison.IsEnabled = false;
        Required<Control>("OpenRecoveryNotice").IsVisible = false;
        Required<TextBlock>("HomeNotice").IsVisible = false;
        Required<Button>("RetryOpenButton").IsVisible = false;
        SetBusy(true); _status.Text = "Opening location…";
        try
        {
            var snapshot = await _inspector.InspectAsync(path, cancellation.Token);
            if (_opening != cancellation || _closed) return;
            if (cancellation.IsCancellationRequested)
            {
                _status.Text = "Opening cancelled; no new workspace was loaded.";
                return;
            }
            _updatingFiles = true;
            try
            {
                _files.ItemsSource = null;
                _filter.Text = "";
                if (resume is not null)
                {
                    _comparison.SelectedIndex = (int)resume.Comparison;
                    _diffMode.SelectedIndex = resume.Split ? 1 : 0;
                    _focusMode = false; _filesPanelVisible = resume.FilesVisible; ApplyPresentation();
                }
            }
            finally { _updatingFiles = false; }
            _snapshot = snapshot; _allFiles = snapshot.Files;
            var selected = snapshot.SelectedPath ?? resume?.SelectedPath ?? (snapshot.Root == previousRoot ? previous : null);
            if (selected is not null && !_allFiles.Any(item => item.Path == selected))
                _allFiles = [new FileChange(selected, null, ".", "."), .. _allFiles];
            _repositoryName.Text = Path.GetFileName(snapshot.Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            _repositoryLabel.Text = snapshot.Root;
            ToolTip.SetTip(_repositoryLabel, snapshot.Root);
            _refresh.IsEnabled = true;
            _isOpeningWorkspace = false;
            ShowComparison();
            _failedWorkspace = null;
            FilterFiles(selected);
            _fileCount.Text = snapshot.Files.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (_allFiles.Count == 0)
            {
                _fileTitle.Text = "Working tree is clean";
                _comparisonLabel.Text = "No local file changes were reported.";
                ShowState("All clear.", "Your working tree has no reported changes. Refresh when you are ready to check again.");
                _status.Text = "Inspection complete · Submodule contents excluded";
            }
            var saved = CurrentWorkspace() is not { } current || _workspaceHistory.Remember(current);
            await PendingInspection;
            if (wasHome && _opening == cancellation && !cancellation.IsCancellationRequested) FocusComparison();
            if (_opening == cancellation && !cancellation.IsCancellationRequested && !saved)
                _status.Text = "Workspace opened for this session; the recent shortcut could not be saved.";
        }
        catch (OperationCanceledException) { if (_opening == cancellation) _status.Text = "Opening cancelled; no new workspace was loaded."; }
        catch (Exception exception) { if (_opening == cancellation && !cancellation.IsCancellationRequested) ShowOpenFailure(path, resume, exception); }
        finally
        {
            if (_opening == cancellation)
            {
                _isOpeningWorkspace = false;
                _files.IsEnabled = true;
                _filter.IsEnabled = _snapshot is not null; _comparison.IsEnabled = _snapshot is not null;
                SetBusy(false);
            }
        }
    }

    private void FilterFiles(string? preferred = null)
    {
        if (_updatingFiles || _closed) return;
        preferred ??= (_files.SelectedItem as ChangedFileItem)?.Path;
        var filtered = _allFiles.Where(file => file.Path.Contains(_filter.Text ?? "", StringComparison.OrdinalIgnoreCase)).Select(file => new ChangedFileItem(file)).ToArray();
        _updatingFiles = true;
        try
        {
            _files.ItemsSource = filtered;
            _files.SelectedItem = filtered.FirstOrDefault(item => item.Path == preferred) ?? filtered.FirstOrDefault();
        }
        finally { _updatingFiles = false; }
        if (filtered.Length == 0 && _snapshot is not null)
        {
            _loading?.Cancel();
            _fileTitle.Text = _allFiles.Count == 0 ? "Working tree is clean" : "No matching files";
            ShowState("No matching files.", "Try a different file name or clear the filter to see all changes.");
            _status.Text = "No files match the current filter";
        }
        PendingInspection = LoadDiffAsync();
    }

    private async Task LoadDiffAsync()
    {
        if (_closed) return;
        _loading?.Cancel(); _loading?.Dispose(); _loading = null;
        if (_snapshot is null || _files.SelectedItem is not ChangedFileItem item) return;
        var cancellation = _loading = new CancellationTokenSource();
        var comparison = _comparison.SelectedIndex switch { 1 => Comparison.Staged, 2 => Comparison.Head, _ => Comparison.WorkingTree };
        _fileTitle.Text = item.Name;
        _comparisonLabel.Text = item.Path;
        ToolTip.SetTip(_comparisonLabel, item.Path);
        ShowState("Loading differences…", "Reading the selected file and comparison.");
        _status.Text = "Loading diff…"; SetBusy(true);
        var start = Stopwatch.GetTimestamp();
        try
        {
            var result = await _inspector.DiffAsync(_snapshot.Root, item.Path, comparison, cancellation.Token);
            if (_closed || _loading != cancellation) return;
            if (cancellation.IsCancellationRequested) { ShowCancelledDiff(); return; }
            if (result.Patch.Length > 200000)
                ShowState("This change is too large to preview.", "The desktop preview is limited to 200,000 characters. Narrow your selection or use the structured CLI.");
            else if (result.IsBinary)
                ShowState("Binary file changed.", "A text comparison is not available for this file.");
            else if (result.Patch.Length == 0)
                ShowState("No differences here.", "This file has no changes for the selected comparison. Try another comparison above.");
            else
            {
                SetWorkspaceDiff(result);
                _emptyState.IsVisible = false; _diffLabel.IsVisible = true;
                _search.IsEnabled = true; _findNext.IsEnabled = true;
            }
            var added = result.Hunks.Sum(hunk => hunk.Lines.Count(line => line.Kind == DiffLineKind.Added));
            var removed = result.Hunks.Sum(hunk => hunk.Lines.Count(line => line.Kind == DiffLineKind.Removed));
            _added.Text = $"+{added}"; _removed.Text = $"−{removed}";
            _status.Text = $"{result.Hunks.Count} change {(result.Hunks.Count == 1 ? "block" : "blocks")} · {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F0} ms";
        }
        catch (OperationCanceledException)
        {
            if (_loading == cancellation)
            {
                ShowCancelledDiff();
            }
        }
        catch (Exception exception) { if (_loading == cancellation && !cancellation.IsCancellationRequested) ShowError(exception); }
        finally { if (_loading == cancellation) SetBusy(false); }
    }

    private void ShowState(string title, string description)
    {
        _emptyState.IsVisible = true; _diff.IsVisible = false; _diffLabel.IsVisible = false;
        _search.IsEnabled = false; _findNext.IsEnabled = false;
        _emptyTitle.Text = title; _emptyDescription.Text = description;
        _decoration.Clear(); _added.Text = "+0"; _removed.Text = "−0";
        _motion.Stop();
        ClearDiffWorkspace();
    }

    private void FindNext()
    {
        var term = _search.Text;
        if (string.IsNullOrEmpty(term)) { _search.Focus(); return; }
        if (_currentDiff is null) { _status.Text = "There is no text diff to search"; return; }
        var editor = ActiveEditor;
        var text = editor.Text;
        var next = text.IndexOf(term, Math.Min(editor.SelectionStart + editor.SelectionLength, text.Length), StringComparison.OrdinalIgnoreCase);
        if (next < 0 && _diffMode.SelectedIndex == 1)
        {
            var other = editor == _before ? _after : _before;
            var match = other.Text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (match >= 0) { editor = other; text = other.Text; next = match; }
        }
        if (next < 0) next = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (next < 0) { _status.Text = "Text not found in the current diff"; return; }
        editor.Focus(); editor.Select(next, term.Length);
        RevealLine(editor, editor.Document.GetLineByOffset(next).LineNumber);
        RevealMatch(editor, next, term.Length);
        _status.Text = "Match selected in diff";
    }

    private void SetBusy(bool value)
    {
        value |= _isOpeningWorkspace;
        _cancel.IsVisible = value;
        var progress = Required<ProgressBar>("BusyIndicator");
        progress.IsIndeterminate = value && !_motion.Reduced;
        progress.IsVisible = value;
    }
    private void Cancel() { _opening?.Cancel(); _loading?.Cancel(); }
    private void ShowCancelledDiff()
    {
        ShowState("Comparison cancelled.", "No complete result is available. Select a file or refresh to try again.");
        _status.Text = "Diff cancelled";
    }
    private void ShowError(Exception exception)
    {
        ShowState("This selection could not be inspected.", exception is InspectionException inspection ? inspection.Message : "Check that the selected path is accessible and try again.");
        _status.Text = "Inspection stopped";
    }
}
