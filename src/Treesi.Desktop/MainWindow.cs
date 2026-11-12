using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using AvaloniaEdit;
using Treesi.Application;
using Treesi.Desktop.Appearance;
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
    private IReadOnlyList<FileChange> _allFiles = [];
    private RepositorySnapshot? _snapshot;
    private CancellationTokenSource? _opening, _loading;
    private bool _updatingFiles;
    private bool _closed;
    public Task PendingInspection { get; private set; } = Task.CompletedTask;

    public MainWindow(IInspectionService inspector, string? initialPath, ThemeController appearance)
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
        InitializeDiffWorkspace();
        _themes.ItemsSource = ThemeCatalog.All;
        _themes.SelectedItem = appearance.Current;
        _themes.SelectionChanged += (_, _) =>
        {
            if (_themes.SelectedItem is ThemeDefinition theme && !_appearance.Apply(theme))
                _status.Text = "Theme changed for this session; the preference could not be saved.";
        };
        _appearance.Changed += OnThemeChanged;
        _files.SelectionChanged += (_, _) => { if (!_updatingFiles) PendingInspection = LoadDiffAsync(); };
        _comparison.SelectionChanged += (_, _) => PendingInspection = LoadDiffAsync();
        _filter.PropertyChanged += (_, args) => { if (args.Property == TextBox.TextProperty) FilterFiles(); };
        Required<Button>("OpenFolderButton").Click += async (_, _) => await PickAsync(false);
        Required<Button>("OpenFileButton").Click += async (_, _) => await PickAsync(true);
        _refresh.Click += async (_, _) => { if (_snapshot is not null) await OpenRepositoryAsync(_snapshot.Root); };
        _cancel.Click += (_, _) => Cancel();
        _findNext.Click += (_, _) => FindNext();
        _search.KeyDown += (_, args) => { if (args.Key == Key.Enter) { FindNext(); args.Handled = true; } };
        KeyDown += async (_, args) =>
        {
            if ((args.KeyModifiers & KeyModifiers.Alt) != 0 && args.Key is Key.Up or Key.Down)
            {
                NavigateBlock(args.Key == Key.Up ? -1 : 1); args.Handled = true; return;
            }
            if (args.Key == Key.Escape) { Cancel(); args.Handled = true; }
            if ((args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0) return;
            switch (args.Key)
            {
                case Key.O: args.Handled = true; await PickAsync(false); break;
                case Key.R: args.Handled = true; if (_snapshot is not null) await OpenRepositoryAsync(_snapshot.Root); break;
                case Key.F: args.Handled = true; _search.Focus(); break;
            }
        };
        Closed += (_, _) =>
        {
            _closed = true;
            Cancel(); _appearance.Changed -= OnThemeChanged;
            _opening?.Dispose(); _opening = null;
            _loading?.Dispose(); _loading = null;
            _decoration.Dispose();
            _beforeDecoration.Dispose(); _afterDecoration.Dispose();
        };
        if (initialPath is not null) Opened += async (_, _) => await OpenRepositoryAsync(initialPath);
    }

    private T Required<T>(string name) where T : Control => this.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing control: {name}");

    private void OnThemeChanged(ThemeDefinition theme)
    {
        _themes.SelectedItem = theme;
        _decoration.Refresh();
        _beforeDecoration.Refresh(); _afterDecoration.Refresh();
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

    public async Task OpenRepositoryAsync(string path)
    {
        Cancel();
        var previous = (_files.SelectedItem as ChangedFileItem)?.Path;
        var previousRoot = _snapshot?.Root;
        _opening?.Dispose();
        var cancellation = _opening = new CancellationTokenSource();
        _snapshot = null; _allFiles = []; _files.ItemsSource = null; _refresh.IsEnabled = false;
        _filter.IsEnabled = false; _comparison.IsEnabled = false;
        _repositoryName.Text = "Opening repository…"; _repositoryLabel.Text = path;
        _fileCount.Text = "0";
        ShowState("Opening repository…", "Reading the local working tree.");
        SetBusy(true); _status.Text = "Inspecting repository…";
        try
        {
            var snapshot = await _inspector.InspectAsync(path, cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            _snapshot = snapshot; _allFiles = snapshot.Files;
            var selected = snapshot.SelectedPath ?? (snapshot.Root == previousRoot ? previous : null);
            if (selected is not null && !_allFiles.Any(item => item.Path == selected))
                _allFiles = [new FileChange(selected, null, ".", "."), .. _allFiles];
            _repositoryName.Text = Path.GetFileName(snapshot.Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            _repositoryLabel.Text = snapshot.Root;
            ToolTip.SetTip(_repositoryLabel, snapshot.Root);
            _refresh.IsEnabled = true;
            _filter.IsEnabled = true; _comparison.IsEnabled = true;
            _updatingFiles = true;
            _filter.Text = "";
            _updatingFiles = false;
            FilterFiles(selected);
            _fileCount.Text = snapshot.Files.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (_allFiles.Count == 0)
            {
                _fileTitle.Text = "Working tree is clean";
                _comparisonLabel.Text = "No local file changes were reported.";
                ShowState("All clear.", "Your working tree has no reported changes. Refresh when you are ready to check again.");
                _status.Text = "Inspection complete · Submodule contents excluded";
            }
            await PendingInspection;
        }
        catch (OperationCanceledException) { if (_opening == cancellation) _status.Text = "Inspection cancelled"; }
        catch (Exception exception) { if (_opening == cancellation && !cancellation.IsCancellationRequested) ShowError(exception); }
        finally { if (_opening == cancellation) SetBusy(false); }
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
            if (cancellation.IsCancellationRequested) return;
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
        catch (OperationCanceledException) { if (_loading == cancellation) _status.Text = "Diff cancelled"; }
        catch (Exception exception) { if (_loading == cancellation && !cancellation.IsCancellationRequested) ShowError(exception); }
        finally { if (_loading == cancellation) SetBusy(false); }
    }

    private void ShowState(string title, string description)
    {
        _emptyState.IsVisible = true; _diff.IsVisible = false; _diffLabel.IsVisible = false;
        _search.IsEnabled = false; _findNext.IsEnabled = false;
        _emptyTitle.Text = title; _emptyDescription.Text = description;
        _decoration.Clear(); _added.Text = "+0"; _removed.Text = "−0";
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
        _status.Text = "Match selected in diff";
    }

    private void SetBusy(bool value) => _cancel.IsVisible = value;
    private void Cancel() { _opening?.Cancel(); _loading?.Cancel(); }
    private void ShowError(Exception exception)
    {
        ShowState("This selection could not be inspected.", exception is InspectionException inspection ? inspection.Message : "Check that the selected path is accessible and try again.");
        _status.Text = "Inspection stopped";
    }
}
