using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Application;
using Treesi.Desktop.Appearance;
using Treesi.Desktop.Workspaces;
using Treesi.Domain;

namespace Treesi.Desktop.Tests;

[TestFixture]
public sealed class WorkspaceGroupTests
{
    private static string Root(string name) => Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "workspace-groups", name);
    private static T Get<T>(Window window, string name) where T : Control => window.FindControl<T>(name)!;
    private static void Click(Window window, string name) => Get<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static MainWindow Create(Inspector inspector, Store store) => new(inspector, null,
        new ThemeController(Avalonia.Application.Current!, new Preferences()), workspaceCatalog: store)
    { Width = 900, Height = 560 };
    private static void Group(Window window, string name) => Get<ListBox>(window, "WorkspaceGroupsList").SelectedItem = Get<ListBox>(window, "WorkspaceGroupsList").Items.Cast<WorkspaceGroupChoice>().Single(item => item.Name == name);
    private static void NewGroup(Window window, string name)
    {
        if (!Get<Control>(window, "WorkspacePickerPanel").IsVisible) Click(window, "WorkspacePickerButton");
        Click(window, "NewWorkspaceGroupButton"); Get<TextBox>(window, "WorkspaceNameInput").Text = name; Click(window, "SaveWorkspaceNameButton");
    }

    [AvaloniaTest]
    public void StartupAndGroupSelectionReadOnlyMetadataAndPickerFitsBothThemes()
    {
        var studio = new LocalWorkspace(Guid.NewGuid(), "Studio", [Root("unavailable"), Root("other")]);
        var personal = new LocalWorkspace(Guid.NewGuid(), "Personal", []);
        var store = new Store { Document = new([studio, personal], studio.Id) }; var inspector = new Inspector(); var window = Create(inspector, store);
        try
        {
            window.Show(); store.Loads.Test().Be(1); inspector.Inspections.Test().Be(0); inspector.Diffs.Test().Be(0);
            Get<TextBlock>(window, "WorkspacePickerCaption").Text.Test().Be("Studio");
            Click(window, "WorkspacePickerButton");
            Get<ListBox>(window, "WorkspaceRepositoriesList").Items.Count.Test().Be(2);
            Group(window, "Personal"); inspector.Inspections.Test().Be(0);
            Get<Control>(window, "EmptyWorkspaceGroup").IsVisible.Test().BeTrue();
            store.Document.SelectedWorkspaceId.Test().Be(personal.Id);
            foreach (var theme in new[] { ThemeCatalog.Dark, ThemeCatalog.Light })
            {
                Get<ComboBox>(window, "ThemeSelector").SelectedItem = theme; Frame(window, "workspaces-" + theme.Id + ".png");
                foreach (var name in new[] { "WorkspacePickerPanel", "NewWorkspaceGroupButton", "WorkspaceNameInput", "SaveWorkspaceNameButton", "DeleteWorkspaceGroupButton", "WorkspaceGroupsList", "WorkspaceRepositoriesList", "AddCurrentWorkspaceRepositoryButton", "AddWorkspaceRepositoryButton", "ReplaceWorkspaceRepositoryButton", "RemoveWorkspaceRepositoryButton", "OpenWorkspaceRepositoryButton" })
                {
                    var control = Get<Control>(window, name); var point = control.TranslatePoint(default, window)!.Value;
                    (point.X >= 0 && point.Y >= 0 && point.X + control.Bounds.Width <= window.ClientSize.Width + 1 && point.Y + control.Bounds.Height <= window.ClientSize.Height + 1).Test().BeTrue();
                }
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task CreateRenameCurrentMembershipAndRemovalKeepTabsAndRecentsIndependent()
    {
        var inspector = new Inspector(); var store = new Store(); var window = Create(inspector, store);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root("active"));
            var editor = Get<TextEditor>(window, "DiffEditor"); editor.Select(editor.Text.IndexOf("new", StringComparison.Ordinal), 3);
            NewGroup(window, "Studio"); var id = store.Document.SelectedWorkspaceId;
            var reads = inspector.Inspections; var diffs = inspector.Diffs;
            Click(window, "AddCurrentWorkspaceRepositoryButton");
            inspector.Inspections.Test().Be(reads); inspector.Diffs.Test().Be(diffs);
            store.Document.Workspaces.Single().RepositoryRoots.Single().Test().Be(Root("active"));
            Frame(window, "workspaces-membership-dark.png");
            Get<TextBox>(window, "WorkspaceNameInput").Text = "Research"; Click(window, "SaveWorkspaceNameButton");
            store.Document.SelectedWorkspaceId.Test().Be(id); Get<TextBlock>(window, "WorkspacePickerCaption").Text.Test().Be("Research");
            Click(window, "OpenWorkspaceRepositoryButton"); await window.PendingInspection;
            editor.SelectedText.Test().Be("new"); inspector.Inspections.Test().Be(reads); inspector.Diffs.Test().Be(diffs);
            Click(window, "RecentWorkspacesButton"); Click(window, "ClearRecentsButton");
            Click(window, "WorkspacePickerButton");
            Get<ListBox>(window, "WorkspaceRepositoriesList").Items.Count.Test().Be(1);
            Click(window, "DeleteWorkspaceGroupButton");
            store.Document.Workspaces.Count.Test().Be(0); Get<TabStrip>(window, "RepositoryTabs").Items.Count.Test().Be(1);
            Click(window, "CloseWorkspacePickerButton"); Click(window, "ReturnToWorkspaceButton");
            editor.SelectedText.Test().Be("new"); Get<TextBlock>(window, "RepositoryName").Text.Test().Be("active");
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task ExplicitValidationAddsAndReplacesShortcutsWithoutOpeningTabsOrChangingTheActiveDiff()
    {
        var inspector = new Inspector(); var store = new Store(); var window = Create(inspector, store);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root("active")); NewGroup(window, "Studio");
            var diffs = inspector.Diffs;
            await window.AddRepositoryToSelectedWorkspaceAsync(Root("other"));
            store.Document.Workspaces.Single().RepositoryRoots.Single().Test().Be(Root("other"));
            Get<TabStrip>(window, "RepositoryTabs").Items.Count.Test().Be(1); inspector.Diffs.Test().Be(diffs);
            Get<TextBlock>(window, "RepositoryName").Text.Test().Be("active");
            await window.AddRepositoryToSelectedWorkspaceAsync(Root("relocated"), true);
            store.Document.Workspaces.Single().RepositoryRoots.Single().Test().Be(Root("relocated"));
            Get<TextEditor>(window, "DiffEditor").Text.Contains("new", StringComparison.Ordinal).Test().BeTrue();
            Click(window, "OpenWorkspaceRepositoryButton"); await window.PendingInspection;
            Get<TabStrip>(window, "RepositoryTabs").Items.Count.Test().Be(2);
            Get<TextBlock>(window, "RepositoryName").Text.Test().Be("relocated");
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task LateValidationCannotAddToAnotherGroupAndMissingLocationsKeepTheCurrentComparison()
    {
        var inspector = new Inspector(); var store = new Store(); var window = Create(inspector, store);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root("active")); NewGroup(window, "Studio"); NewGroup(window, "Personal"); Group(window, "Studio");
            var delayed = new TaskCompletionSource<RepositorySnapshot>(); inspector.Delayed[Root("slow")] = delayed;
            var loading = window.AddRepositoryToSelectedWorkspaceAsync(Root("slow"));
            Group(window, "Personal"); inspector.LastToken.IsCancellationRequested.Test().BeTrue();
            delayed.SetResult(Inspector.Snapshot(Root("slow"))); await loading;
            store.Document.Workspaces.All(item => item.RepositoryRoots.Count == 0).Test().BeTrue();
            await window.AddRepositoryToSelectedWorkspaceAsync(Root("missing"));
            Get<TextBlock>(window, "WorkspaceGroupNotice").Text!.Contains("does not exist", StringComparison.Ordinal).Test().BeTrue();
            store.Document.Workspaces.All(item => item.RepositoryRoots.Count == 0).Test().BeTrue();
            var closing = new TaskCompletionSource<RepositorySnapshot>(); inspector.Delayed[Root("closing")] = closing;
            loading = window.AddRepositoryToSelectedWorkspaceAsync(Root("closing")); Click(window, "CloseWorkspacePickerButton");
            closing.SetResult(Inspector.Snapshot(Root("closing"))); await loading;
            store.Document.Workspaces.All(item => item.RepositoryRoots.Count == 0).Test().BeTrue();
            Get<TextBlock>(window, "RepositoryName").Text.Test().Be("active");
            Click(window, "WorkspacePickerButton");
            var exiting = new TaskCompletionSource<RepositorySnapshot>(); inspector.Delayed[Root("exiting")] = exiting;
            loading = window.AddRepositoryToSelectedWorkspaceAsync(Root("exiting")); window.Close();
            inspector.LastToken.IsCancellationRequested.Test().BeTrue();
            exiting.SetResult(Inspector.Snapshot(Root("exiting"))); await loading;
            var reads = inspector.Inspections; await window.AddRepositoryToSelectedWorkspaceAsync(Root("after-close"));
            inspector.Inspections.Test().Be(reads); store.Document.Workspaces.All(item => item.RepositoryRoots.Count == 0).Test().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task MissingStoredRootCanBeReplacedAfterExplicitOpenFailure()
    {
        var group = new LocalWorkspace(Guid.NewGuid(), "Studio", [Root("missing")]);
        var store = new Store { Document = new([group], group.Id) }; var window = Create(new Inspector(), store);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root("active")); Click(window, "WorkspacePickerButton");
            Click(window, "OpenWorkspaceRepositoryButton"); await window.PendingInspection;
            Get<Control>(window, "OpenRecoveryNotice").IsVisible.Test().BeTrue();
            Get<TextBlock>(window, "RepositoryName").Text.Test().Be("active");
            Click(window, "WorkspacePickerButton"); await window.AddRepositoryToSelectedWorkspaceAsync(Root("located"), true);
            store.Document.Workspaces.Single().RepositoryRoots.Single().Test().Be(Root("located"));
            Click(window, "OpenWorkspaceRepositoryButton"); await window.PendingInspection;
            Get<TextBlock>(window, "RepositoryName").Text.Test().Be("located");
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task FailedPersistenceIsVisibleAndPickerContainsKeyboardFocus()
    {
        var inspector = new Inspector(); var store = new Store { SaveSucceeds = false }; var window = Create(inspector, store);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root("active")); NewGroup(window, "Session group");
            Get<Control>(window, "WorkspaceSessionNotice").IsVisible.Test().BeTrue();
            Get<TextBlock>(window, "WorkspaceGroupNotice").Text!.Contains("could not be saved", StringComparison.Ordinal).Test().BeTrue();
            store.Document.Workspaces.Count.Test().Be(0);
            var reads = inspector.Inspections; Frame(window);
            Get<TextBox>(window, "WorkspaceNameInput").Focus();
            for (var i = 0; i < 20; i++) { Key(window, Avalonia.Input.Key.Tab); Get<Control>(window, "WorkspacePickerPanel").IsKeyboardFocusWithin.Test().BeTrue(); }
            Key(window, Avalonia.Input.Key.R, RawInputModifiers.Control); Key(window, Avalonia.Input.Key.W, RawInputModifiers.Control); Key(window, Avalonia.Input.Key.F6);
            inspector.Inspections.Test().Be(reads); Get<TabStrip>(window, "RepositoryTabs").Items.Count.Test().Be(1);
            Key(window, Avalonia.Input.Key.Escape);
            Get<Control>(window, "WorkspacePickerPanel").IsVisible.Test().BeFalse();
            Get<Button>(window, "WorkspacePickerButton").IsFocused.Test().BeTrue();
        }
        finally { window.Close(); }
    }

    private static void Key(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    { window.KeyPress(key, modifiers, PhysicalKey.None, null); window.KeyRelease(key, modifiers, PhysicalKey.None, null); Dispatcher.UIThread.RunJobs(); }

    private static void Frame(Window window, string? name = null)
    {
        Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
        if (name is null || Environment.GetEnvironmentVariable("TREESI_RENDER_OUTPUT") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory); frame!.Save(Path.Combine(directory, name), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    private sealed class Preferences : IThemePreferenceStore { public string? Load() => "dark"; public bool Save(string themeId) => true; }
    private sealed class Store : IWorkspaceCatalogStore
    {
        public WorkspaceCatalogDocument Document { get; set; } = WorkspaceCatalogDocument.Empty;
        public int Loads { get; private set; }
        public bool SaveSucceeds { get; init; } = true;
        public WorkspaceCatalogLoad Load() { Loads++; return new(Document); }
        public bool Save(WorkspaceCatalogDocument document) { if (!SaveSucceeds) return false; Document = document; return true; }
    }
    private sealed class Inspector : IInspectionService
    {
        public int Inspections { get; private set; }
        public int Diffs { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public Dictionary<string, TaskCompletionSource<RepositorySnapshot>> Delayed { get; } = [];
        public static RepositorySnapshot Snapshot(string root) => new(root, null, [new("sample.cs", null, ".", "M")]);
        public Task<RepositorySnapshot> InspectAsync(string path, CancellationToken cancellationToken = default)
        {
            Inspections++; LastToken = cancellationToken;
            if (path == Root("missing")) return Task.FromException<RepositorySnapshot>(new InspectionException("path_not_found", "The selected path does not exist."));
            return Delayed.TryGetValue(path, out var pending) ? pending.Task : Task.FromResult(Snapshot(path));
        }
        public Task<FileDiff> DiffAsync(string repository, string path, Comparison comparison, CancellationToken cancellationToken = default, DiffOptions? options = null)
        {
            Diffs++;
            return Task.FromResult(new FileDiff(path, comparison, "diff --git a/sample.cs b/sample.cs\n--- a/sample.cs\n+++ b/sample.cs\n@@ -1 +1 @@\n-old\n+new\n", false,
                [new("@@ -1 +1 @@", [new(DiffLineKind.Removed, 1, null, "old"), new(DiffLineKind.Added, null, 1, "new")])], Options: options));
        }
    }
}
