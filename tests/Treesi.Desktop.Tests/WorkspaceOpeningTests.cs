using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using AvaloniaEdit;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Application;
using Treesi.Desktop.Appearance;
using Treesi.Desktop.Workspaces;
using Treesi.Domain;

namespace Treesi.Desktop.Tests;

[TestFixture]
public sealed class WorkspaceOpeningTests
{
    private static string Root(string name) => Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "workspace", name);
    private static T Control<T>(Window window, string name) where T : Avalonia.Controls.Control => window.FindControl<T>(name)!;
    private static void Click(Window window, string name) => Control<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static MainWindow Window(Inspector inspector, HistoryStore store, string? initial = null) => new(inspector, initial,
        new ThemeController(Avalonia.Application.Current!, new Themes()), workspaceHistory: store);

    [AvaloniaTest]
    public async Task StartupDoesNotInspectRecentsAndResumeRestoresTheSavedView()
    {
        var inspector = new Inspector();
        var entry = new WorkspaceEntry(Root("treesi"), "docs/README.md", Comparison.Head, true, false);
        var store = new HistoryStore { Entries = [entry, new(Root("design-system")), new(Root("sample-api"))] };
        var window = Window(inspector, store);
        try
        {
            window.Show();
            inspector.Inspections.Count.Test().Be(0);
            Control<Control>(window, "HomeSurface").IsVisible.Test().BeTrue();
            Control<ListBox>(window, "RecentRepositories").SelectedItem.Test().Be(entry);
            SaveFrame(window, "home-dark.png");
            Control<ComboBox>(window, "ThemeSelector").SelectedItem = ThemeCatalog.Light;
            window.Width = 900; window.Height = 560;
            SaveFrame(window, "home-light-compact.png");
            foreach (var name in new[] { "ResumeWorkspaceButton", "RemoveRecentButton", "ClearRecentsButton", "HomeOpenRepositoryButton", "HomeOpenFileButton" })
            {
                var control = Control<Control>(window, name);
                var point = control.TranslatePoint(default, window)!.Value;
                (point.X >= 0 && point.X + control.Bounds.Width <= window.ClientSize.Width).Test().BeTrue();
                (point.Y >= 0 && point.Y + control.Bounds.Height <= window.ClientSize.Height).Test().BeTrue();
            }
            Click(window, "ResumeWorkspaceButton"); await window.PendingInspection;
            inspector.Inspections.Count.Test().Be(1);
            inspector.Diffs.Single().Test().Be((entry.Root, entry.SelectedPath!, Comparison.Head));
            Control<TextBlock>(window, "FileTitle").Text.Test().Be("README.md");
            Control<TabStrip>(window, "DiffModeSelector").SelectedIndex.Test().Be(1);
            Control<Control>(window, "FilesPanel").IsVisible.Test().BeFalse();
            Control<Control>(window, "HomeSurface").IsVisible.Test().BeFalse();
            SaveFrame(window, "resumed-light-compact.png");
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task ClosingSavesSelectionAndViewButForgottenWorkspacesAreNotReinserted()
    {
        var inspector = new Inspector(); var store = new HistoryStore();
        var window = Window(inspector, store);
        window.Show(); await window.OpenRepositoryAsync(Root("treesi"));
        Control<ListBox>(window, "ChangedFiles").SelectedIndex = 1; await window.PendingInspection;
        Control<ComboBox>(window, "ComparisonSelector").SelectedIndex = 1; await window.PendingInspection;
        Control<TabStrip>(window, "DiffModeSelector").SelectedIndex = 1;
        Click(window, "ToggleFilesButton");
        window.Close();
        store.Entries.Single().Test().Be(new WorkspaceEntry(Root("treesi"), "docs/README.md", Comparison.Staged, true, false));

        var next = Window(inspector, store);
        try
        {
            next.Show(); Click(next, "ResumeWorkspaceButton"); await next.PendingInspection;
            Click(next, "RecentWorkspacesButton"); Click(next, "RemoveRecentButton");
            Control<Control>(next, "NoRecentWorkspaces").IsVisible.Test().BeTrue();
            Click(next, "ReturnToWorkspaceButton");
            Control<TextBlock>(next, "FileTitle").Text.Test().Be("README.md");
        }
        finally { next.Close(); }
        store.Entries.Count.Test().Be(0);
    }

    [AvaloniaTest]
    public async Task FailedOpeningPreservesTheLoadedComparisonAndRetryCanRecover()
    {
        var inspector = new Inspector(); var store = new HistoryStore();
        var window = Window(inspector, store);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root("treesi"));
            var editor = Control<TextEditor>(window, "DiffEditor");
            editor.Select(editor.Text.IndexOf("new", StringComparison.Ordinal), 3);
            var selected = Control<ListBox>(window, "ChangedFiles").SelectedItem;
            inspector.Failure = new InspectionException("path_not_found", "The selected path does not exist.");
            await window.OpenRepositoryAsync(Root("missing"));
            editor.SelectedText.Test().Be("new");
            Control<ListBox>(window, "ChangedFiles").SelectedItem.Test().Be(selected);
            Control<TextBlock>(window, "RepositoryName").Text.Test().Be("treesi");
            Control<Control>(window, "OpenRecoveryNotice").IsVisible.Test().BeTrue();
            Control<ComboBox>(window, "ComparisonSelector").IsEnabled.Test().BeTrue();
            store.Entries.Count.Test().Be(1);
            SaveFrame(window, "open-recovery-dark.png");
            inspector.Failure = null;
            Click(window, "RetryWorkspaceButton"); await window.PendingInspection;
            Control<TextBlock>(window, "RepositoryName").Text.Test().Be("missing");
            store.Entries[0].Root.Test().Be(Root("missing"));
            Control<Control>(window, "OpenRecoveryNotice").IsVisible.Test().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task CancelledAndSupersededOpeningCannotReplaceTheCurrentWorkspace()
    {
        var inspector = new Inspector(); var store = new HistoryStore();
        var window = Window(inspector, store);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root("treesi"));
            var old = new TaskCompletionSource<RepositorySnapshot>(); inspector.Delayed[Root("slow")] = old;
            var pending = window.OpenRepositoryAsync(Root("slow"));
            Control<ProgressBar>(window, "BusyIndicator").IsVisible.Test().BeTrue();
            Control<TextBlock>(window, "RepositoryName").Text.Test().Be("treesi");
            Click(window, "CancelButton"); old.SetResult(Inspector.Snapshot(Root("slow"))); await pending;
            Control<TextBlock>(window, "RepositoryName").Text.Test().Be("treesi");
            var stale = new TaskCompletionSource<RepositorySnapshot>(); inspector.Delayed[Root("slow")] = stale;
            pending = window.OpenRepositoryAsync(Root("slow"));
            await window.OpenRepositoryAsync(Root("newer"));
            stale.SetResult(Inspector.Snapshot(Root("slow"))); await pending;
            Control<TextBlock>(window, "RepositoryName").Text.Test().Be("newer");
            store.Entries.Any(item => item.Root == Root("slow")).Test().BeFalse();
            Control<ProgressBar>(window, "BusyIndicator").IsVisible.Test().BeFalse();
            Control<TextBox>(window, "FileFilter").IsEnabled.Test().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task VisitingRecentsDuringTheInitialDiffKeepsTheValidatedRepositoryAndShowsCancellation()
    {
        var delayed = new TaskCompletionSource<FileDiff>();
        var inspector = new Inspector { DelayedDiff = delayed };
        var store = new HistoryStore();
        var window = Window(inspector, store);
        try
        {
            window.Show(); var opening = window.OpenRepositoryAsync(Root("treesi"));
            Click(window, "RecentWorkspacesButton");
            store.Entries.Single().Root.Test().Be(Root("treesi"));
            delayed.SetResult(new FileDiff("src/code.cs", Comparison.WorkingTree, "+ready\n", false, []));
            await opening;
            Control<Control>(window, "HomeSurface").IsVisible.Test().BeTrue();
            Click(window, "ReturnToWorkspaceButton");
            Control<TextBlock>(window, "EmptyTitle").Text.Test().Be("Comparison cancelled.");
            Control<ProgressBar>(window, "BusyIndicator").IsVisible.Test().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task InitialOpeningFailureShowsActionableHomeRecoveryWithoutAddingARecent()
    {
        foreach (var message in new[] { "The selected path does not exist.", "Git was not found. Install Git and make it available on PATH." })
        {
            var inspector = new Inspector { Failure = new InspectionException("unavailable", message) };
            var store = new HistoryStore();
            var window = Window(inspector, store, Root("unavailable"));
            try
            {
                window.Show(); await window.PendingInspection;
                Control<Control>(window, "HomeSurface").IsVisible.Test().BeTrue();
                Control<TextBlock>(window, "HomeNotice").Text.Test().Be(message);
                Control<Button>(window, "RetryOpenButton").IsVisible.Test().BeTrue();
                store.Entries.Count.Test().Be(0);
                inspector.Failure = null;
                Click(window, "RetryOpenButton"); await window.PendingInspection;
                Control<TextBlock>(window, "RepositoryName").Text.Test().Be("unavailable");
                Control<Control>(window, "HomeSurface").IsVisible.Test().BeFalse();
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaTest]
    public async Task MissingSavedFileShowsAnExplicitErrorAndCurrentFilesRemainAvailable()
    {
        var inspector = new Inspector { MissingDiff = "src/removed.cs" };
        var store = new HistoryStore { Entries = [new WorkspaceEntry(Root("treesi"), "src/removed.cs", FilesVisible: false)] };
        var window = Window(inspector, store);
        try
        {
            window.Show(); Click(window, "ResumeWorkspaceButton"); await window.PendingInspection;
            Control<TextBlock>(window, "EmptyTitle").Text.Test().Be("This selection could not be inspected.");
            Control<TextBlock>(window, "RepositoryName").Text.Test().Be("treesi");
            Click(window, "ToggleFilesButton");
            Control<ListBox>(window, "ChangedFiles").SelectedIndex = 1; await window.PendingInspection;
            Control<TextBlock>(window, "FileTitle").Text.Test().Be("code.cs");
            Control<TextBox>(window, "DiffSearch").IsEnabled.Test().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task StartupArgumentOverridesRecentsAndSaveFailuresKeepTheSessionUsable()
    {
        var inspector = new Inspector(); var store = new HistoryStore { Entries = [new(Root("old"))], CanSave = false };
        var window = Window(inspector, store, Root("explicit"));
        try
        {
            window.Show(); await window.PendingInspection;
            inspector.Inspections.Single().Test().Be(Root("explicit"));
            Control<TextBlock>(window, "RepositoryName").Text.Test().Be("explicit");
            Control<TextBlock>(window, "OperationStatus").Text!.Contains("could not be saved", StringComparison.Ordinal).Test().BeTrue();
            Click(window, "RecentWorkspacesButton"); Click(window, "ClearRecentsButton");
            Control<Button>(window, "ResumeWorkspaceButton").IsEnabled.Test().BeFalse();
            Control<TextBlock>(window, "HomeNotice").Text!.Contains("could not be saved", StringComparison.Ordinal).Test().BeTrue();
            Click(window, "ReturnToWorkspaceButton");
            Control<TextEditor>(window, "DiffEditor").Text!.Contains("+new", StringComparison.Ordinal).Test().BeTrue();
        }
        finally { window.Close(); }
    }

    private static void SaveFrame(MainWindow window, string name)
    {
        Control<CheckBox>(window, "ReduceMotionToggle").IsChecked = true;
        using var frame = window.CaptureRenderedFrame(); frame.Test().NotBeNull();
        if (Environment.GetEnvironmentVariable("TREESI_RENDER_OUTPUT") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        frame!.Save(Path.Combine(directory, name), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    private sealed class Themes : IThemePreferenceStore
    {
        public string? Load() => "dark";
        public bool Save(string themeId) => true;
    }

    private sealed class HistoryStore : IWorkspaceHistoryStore
    {
        public IReadOnlyList<WorkspaceEntry> Entries { get; set; } = [];
        public bool CanSave { get; init; } = true;
        public IReadOnlyList<WorkspaceEntry> Load() => Entries;
        public bool Save(IReadOnlyList<WorkspaceEntry> entries) { if (!CanSave) return false; Entries = entries.ToArray(); return true; }
    }

    private sealed class Inspector : IInspectionService
    {
        public TaskCompletionSource<FileDiff>? DelayedDiff { get; init; }
        public string? MissingDiff { get; init; }
        public Exception? Failure { get; set; }
        public List<string> Inspections { get; } = [];
        public List<(string Root, string Path, Comparison Comparison)> Diffs { get; } = [];
        public Dictionary<string, TaskCompletionSource<RepositorySnapshot>> Delayed { get; } = [];
        public static RepositorySnapshot Snapshot(string root) => new(root, null, [new("src/code.cs", null, ".", "M"), new("docs/README.md", null, ".", "M")]);
        public Task<RepositorySnapshot> InspectAsync(string path, CancellationToken cancellationToken = default)
        {
            Inspections.Add(path);
            if (Failure is { } error) return Task.FromException<RepositorySnapshot>(error);
            return Delayed.TryGetValue(path, out var pending) ? pending.Task : Task.FromResult(Snapshot(path));
        }
        public Task<FileDiff> DiffAsync(string repository, string path, Comparison comparison, CancellationToken cancellationToken = default, DiffOptions? options = null)
        {
            Diffs.Add((repository, path, comparison));
            if (path == MissingDiff) return Task.FromException<FileDiff>(new InspectionException("path_not_found", "The saved file is no longer available. Choose another changed file."));
            if (DelayedDiff is not null) return DelayedDiff.Task;
            return Task.FromResult(new FileDiff(path, comparison, "diff --git a/src/code.cs b/src/code.cs\n--- a/src/code.cs\n+++ b/src/code.cs\n@@ -1 +1 @@\n-old\n+new\n", false,
                [new("@@ -1 +1 @@", [new(DiffLineKind.Removed, 1, null, "old"), new(DiffLineKind.Added, null, 1, "new")])]));
        }
    }
}
