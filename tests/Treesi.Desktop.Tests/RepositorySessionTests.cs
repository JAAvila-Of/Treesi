using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Application;
using Treesi.Desktop.Appearance;
using Treesi.Desktop.Workspaces;
using Treesi.Domain;

namespace Treesi.Desktop.Tests;

[TestFixture]
public sealed class RepositorySessionTests
{
    private static string Root(string name) => Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "sessions", name);
    private static T Get<T>(Window window, string name) where T : Control => window.FindControl<T>(name)!;
    private static void Click(Window window, string name) => Get<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static MainWindow Create(Inspector inspector, Revisions? revisions = null, History? history = null) => new(inspector, null,
        new ThemeController(Avalonia.Application.Current!, new Preferences()), revisions: revisions, history: history)
    { Width = 900, Height = 560 };
    private static void Select(Window window, string root) => Get<TabStrip>(window, "RepositoryTabs").SelectedItem = Get<TabStrip>(window, "RepositoryTabs").Items.Cast<RepositoryTab>().Single(tab => tab.Root == root);
    private static void Frame(Window window) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
    private static void CloseTab(Window window, string root)
    {
        Frame(window);
        window.GetVisualDescendants().OfType<Button>().Single(button => button.Tag is RepositoryTab tab && tab.Root == root)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    [AvaloniaTest]
    public async Task SwitchingTabsRestoresSelectionLayoutOptionsAndSearchWithoutReadingGit()
    {
        var inspector = new Inspector(); var window = Create(inspector);
        try
        {
            window.Show(); Get<CheckBox>(window, "ReduceMotionToggle").IsChecked = true;
            await window.OpenRepositoryAsync(Root("first"));
            Get<ListBox>(window, "ChangedFiles").SelectedIndex = 1; await window.PendingInspection;
            Get<ComboBox>(window, "ComparisonSelector").SelectedIndex = 2; await window.PendingInspection;
            Click(window, "DiffOptionsButton"); Get<CheckBox>(window, "FullContextToggle").IsChecked = true;
            Click(window, "ApplyDiffOptionsButton"); await window.PendingInspection;
            Get<TextBox>(window, "FileFilter").Text = "notes"; await window.PendingInspection;
            Get<TextBox>(window, "DiffSearch").Text = "new-first";
            var editor = Get<TextEditor>(window, "DiffEditor");
            editor.Select(editor.Text.IndexOf("new-first", StringComparison.Ordinal), "new-first".Length);
            Frame(window);
            var scroll = editor.FindDescendantOfType<ScrollViewer>()!;
            scroll.Offset = new Vector(0, 300); Frame(window);
            var offset = editor.VerticalOffset;
            await window.OpenRepositoryAsync(Root("second"));
            Get<TabStrip>(window, "DiffModeSelector").SelectedIndex = 1;
            SaveFrame(window, "session-tabs-dark-split.png");
            var reads = inspector.Diffs; var inspections = inspector.Inspections;
            Select(window, Root("first")); Frame(window);
            inspector.Diffs.Test().Be(reads); inspector.Inspections.Test().Be(inspections);
            editor.SelectedText.Test().Be("new-first");
            (Math.Abs(editor.VerticalOffset - offset) < 1).Test().BeTrue();
            Get<TextBox>(window, "FileFilter").Text.Test().Be("notes");
            Get<TextBox>(window, "DiffSearch").Text.Test().Be("new-first");
            Get<ComboBox>(window, "ComparisonSelector").SelectedIndex.Test().Be(2);
            Get<TabStrip>(window, "DiffModeSelector").SelectedIndex.Test().Be(0);
            Click(window, "DiffOptionsButton"); Get<CheckBox>(window, "FullContextToggle").IsChecked.Test().Be(true);
            Click(window, "CancelDiffOptionsButton");
            Select(window, Root("second")); Frame(window);
            Get<TabStrip>(window, "DiffModeSelector").SelectedIndex.Test().Be(1);
            Get<TextBox>(window, "FileFilter").Text.Test().Be("");
            Get<TextBox>(window, "DiffSearch").Text.Test().Be("");
            Get<TextBlock>(window, "OperationStatus").Text!.Contains("Saved tab view", StringComparison.Ordinal).Test().BeTrue();
            Get<ComboBox>(window, "ThemeSelector").SelectedItem = ThemeCatalog.Light;
            SaveFrame(window, "session-tabs-light-split.png");
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task LateDiffCannotReplaceAnotherTabOrResumeAfterRoundTrip()
    {
        var inspector = new Inspector(); var window = Create(inspector);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root("first")); await window.OpenRepositoryAsync(Root("second"));
            Select(window, Root("first"));
            var pending = inspector.DelayedDiff = new();
            Get<ListBox>(window, "ChangedFiles").SelectedIndex = 1; var loading = window.PendingInspection;
            Select(window, Root("second"));
            Get<TextEditor>(window, "DiffEditor").Text.Contains("new-second", StringComparison.Ordinal).Test().BeTrue();
            inspector.LastDiffToken.IsCancellationRequested.Test().BeTrue();
            Select(window, Root("first"));
            Get<TextBlock>(window, "EmptyTitle").Text.Test().Be("Inspection paused.");
            pending.SetResult(Inspector.Diff(Root("first"), "notes.cs", Comparison.WorkingTree, DiffOptions.Default));
            await loading;
            Get<TextBlock>(window, "EmptyTitle").Text.Test().Be("Inspection paused.");
            Get<ProgressBar>(window, "BusyIndicator").IsVisible.Test().BeFalse();
            inspector.DelayedDiff = null;
            Click(window, "RefreshButton"); await window.PendingInspection;
            Get<TextEditor>(window, "DiffEditor").Text.Contains("new-first", StringComparison.Ordinal).Test().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task SwitchingTabsInvalidatesPendingOpeningAndDoesNotAddALateTab()
    {
        var inspector = new Inspector(); var window = Create(inspector);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root("first")); await window.OpenRepositoryAsync(Root("second"));
            var pending = inspector.DelayedOpening = new(); var opening = window.OpenRepositoryAsync(Root("slow"));
            Select(window, Root("first"));
            pending.SetResult(Inspector.Snapshot(Root("slow"))); await opening;
            Get<TabStrip>(window, "RepositoryTabs").Items.Count.Test().Be(2);
            Get<TextBlock>(window, "RepositoryName").Text.Test().Be("first");
            Get<TextEditor>(window, "DiffEditor").Text.Contains("new-first", StringComparison.Ordinal).Test().BeTrue();
            Get<ProgressBar>(window, "BusyIndicator").IsVisible.Test().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task CloseReleasesTabsKeepsOtherViewsAndReturnsHomeAfterTheLastOne()
    {
        var inspector = new Inspector(); var window = Create(inspector);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root("first")); await window.OpenRepositoryAsync(Root("second"));
            await window.OpenRepositoryAsync(Root("second"));
            Get<TabStrip>(window, "RepositoryTabs").Items.Count.Test().Be(2);
            CloseTab(window, Root("first"));
            Get<TextBlock>(window, "RepositoryName").Text.Test().Be("second");
            CloseTab(window, Root("second"));
            Get<TabStrip>(window, "RepositoryTabs").Items.Count.Test().Be(0);
            Get<Control>(window, "HomeSurface").IsVisible.Test().BeTrue();
            Get<Button>(window, "ReturnToWorkspaceButton").IsVisible.Test().BeFalse();
            Get<Control>(window, "RepositoryTabBar").IsVisible.Test().BeFalse();
            await window.OpenRepositoryAsync(Root("third"));
            Get<TextBlock>(window, "RepositoryName").Text.Test().Be("third");
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task TabLimitDoesNotEvictOrReplaceTheLoadedRepository()
    {
        var inspector = new Inspector(); var window = Create(inspector);
        try
        {
            window.Show();
            for (var i = 0; i < 9; i++) await window.OpenRepositoryAsync(Root("repository-" + i));
            Get<TabStrip>(window, "RepositoryTabs").Items.Count.Test().Be(8);
            Get<TextBlock>(window, "RepositoryName").Text.Test().Be("repository-7");
            Get<TextBlock>(window, "OpenRecoveryMessage").Text!.Contains("Eight repository tabs", StringComparison.Ordinal).Test().BeTrue();
            CloseTab(window, Root("repository-0"));
            await window.OpenRepositoryAsync(Root("repository-8"));
            Get<TextBlock>(window, "RepositoryName").Text.Test().Be("repository-8");
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task HistoryAndPinnedRevisionContextReturnWithoutResolvingAgain()
    {
        var inspector = new Inspector(); var revisions = new Revisions(); var history = new History();
        var window = Create(inspector, revisions, history);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root("first"));
            Click(window, "HistoryButton"); await window.PendingInspection;
            Get<ListBox>(window, "HistoryCommits").SelectedIndex = 0; await window.PendingInspection;
            var endpoint = Get<Button>(window, "RevisionEndpointsButton").Content;
            await window.OpenRepositoryAsync(Root("second"));
            var reads = revisions.Reads; var historyReads = history.Reads;
            Select(window, Root("first")); Frame(window);
            revisions.Reads.Test().Be(reads); history.Reads.Test().Be(historyReads);
            Get<Button>(window, "RevisionEndpointsButton").Content.Test().Be(endpoint);
            Get<Control>(window, "HistorySidebar").IsVisible.Test().BeTrue();
            Get<ListBox>(window, "HistoryCommits").SelectedIndex.Test().Be(0);
            Get<TextEditor>(window, "DiffEditor").Text.Contains("historical", StringComparison.Ordinal).Test().BeTrue();
            Click(window, "RevisionEndpointsButton");
            Get<TextBlock>(window, "CommitIdentity").Text.Test().Be(new string('b', 40));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task OverflowTabsRemainReachableWithoutReducingTheCodeCanvas()
    {
        var window = Create(new Inspector());
        try
        {
            window.Show();
            var roots = Enumerable.Range(0, 8).Select(i => Root(new string('x', 80) + "-" + i)).ToArray();
            foreach (var root in roots) await window.OpenRepositoryAsync(root);
            foreach (var root in new[] { roots[0], roots[^1] })
            {
                Select(window, root); Frame(window);
                var tabs = Get<TabStrip>(window, "RepositoryTabs");
                var item = tabs.ContainerFromIndex(tabs.SelectedIndex)!;
                var point = item.TranslatePoint(default, window)!.Value;
                (point.X >= -1 && point.X + item.Bounds.Width <= window.ClientSize.Width + 1).Test().BeTrue();
                (Get<Control>(window, "DiffSurface").Bounds.Height >= 370).Test().BeTrue();
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task KeyboardCyclesTabsAndNewTabKeepsTheCurrentComparison()
    {
        var window = Create(new Inspector());
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root("first")); await window.OpenRepositoryAsync(Root("second")); Frame(window);
            window.KeyPress(Key.Tab, RawInputModifiers.Control, PhysicalKey.None, null); window.KeyRelease(Key.Tab, RawInputModifiers.Control, PhysicalKey.None, null); Frame(window);
            Get<TextBlock>(window, "RepositoryName").Text.Test().Be("first");
            window.KeyPress(Key.Tab, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null); window.KeyRelease(Key.Tab, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null); Frame(window);
            Get<TextBlock>(window, "RepositoryName").Text.Test().Be("second");
            window.KeyPress(Key.T, RawInputModifiers.Control, PhysicalKey.None, null); window.KeyRelease(Key.T, RawInputModifiers.Control, PhysicalKey.None, null);
            Get<Control>(window, "HomeSurface").IsVisible.Test().BeTrue();
            Frame(window);
            var tabs = Get<TabStrip>(window, "RepositoryTabs");
            var item = tabs.ContainerFromIndex(tabs.SelectedIndex)!;
            var point = item.TranslatePoint(new Point(8, 12), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Frame(window);
            Get<Control>(window, "HomeSurface").IsVisible.Test().BeFalse();
            Get<TextBlock>(window, "RepositoryName").Text.Test().Be("second");
            window.KeyPress(Key.W, RawInputModifiers.Control, PhysicalKey.None, null); window.KeyRelease(Key.W, RawInputModifiers.Control, PhysicalKey.None, null); Frame(window);
            Get<TabStrip>(window, "RepositoryTabs").Items.Count.Test().Be(1);
            Get<TextBlock>(window, "RepositoryName").Text.Test().Be("first");
        }
        finally { window.Close(); }
    }

    private sealed class Preferences : IThemePreferenceStore { public string? Load() => "dark"; public bool Save(string themeId) => true; }

    private static void SaveFrame(Window window, string name)
    {
        Frame(window);
        if (Environment.GetEnvironmentVariable("TREESI_RENDER_OUTPUT") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(directory, name), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
    private sealed class Inspector : IInspectionService
    {
        public int Inspections { get; private set; }
        public int Diffs { get; private set; }
        public TaskCompletionSource<RepositorySnapshot>? DelayedOpening { get; set; }
        public TaskCompletionSource<FileDiff>? DelayedDiff { get; set; }
        public CancellationToken LastDiffToken { get; private set; }
        public static RepositorySnapshot Snapshot(string root) => new(root, null, [new("review.cs", null, ".", "M"), new("notes.cs", null, ".", "M")]);
        public Task<RepositorySnapshot> InspectAsync(string path, CancellationToken cancellationToken = default)
        { Inspections++; return DelayedOpening?.Task ?? Task.FromResult(Snapshot(path)); }
        public Task<FileDiff> DiffAsync(string repository, string path, Comparison comparison, CancellationToken cancellationToken = default, DiffOptions? options = null)
        { Diffs++; LastDiffToken = cancellationToken; return DelayedDiff?.Task ?? Task.FromResult(Diff(repository, path, comparison, options ?? DiffOptions.Default)); }
        public static FileDiff Diff(string root, string path, Comparison comparison, DiffOptions options)
        {
            var name = Path.GetFileName(root);
            var lines = Enumerable.Range(1, 50).Select(i => new DiffLine(DiffLineKind.Context, i, i, "// context " + i)).ToList();
            lines.Add(new(DiffLineKind.Removed, 51, null, "old-" + name)); lines.Add(new(DiffLineKind.Added, null, 51, "new-" + name));
            lines.AddRange(Enumerable.Range(52, 50).Select(i => new DiffLine(DiffLineKind.Context, i, i, "// context " + i)));
            const string header = "@@ -1,101 +1,101 @@";
            var patch = $"diff --git a/{path} b/{path}\n--- a/{path}\n+++ b/{path}\n{header}\n" + string.Join('\n', lines.Select(line => (line.Kind switch { DiffLineKind.Added => "+", DiffLineKind.Removed => "-", _ => " " }) + line.Text)) + "\n";
            return new(path, comparison, patch, false, [new(header, lines)], Options: options);
        }
    }

    private sealed class Revisions : IRevisionService
    {
        public int Reads { get; private set; }
        public Task<RevisionReferences> ReferencesAsync(string repository, CancellationToken cancellationToken = default) => Task.FromResult(new RevisionReferences([], false));
        public Task<RevisionEndpoints> ResolveAsync(string repository, RevisionSelection selection, CancellationToken cancellationToken = default) => throw new InvalidOperationException("History must retain pinned endpoints.");
        public Task<RevisionComparison> CompareAsync(RevisionEndpoints endpoints, CancellationToken cancellationToken = default)
        { Reads++; return Task.FromResult(new RevisionComparison(endpoints, [new("review.cs", "M")])); }
        public Task<FileDiff> DiffAsync(RevisionEndpoints endpoints, string path, DiffOptions? options = null, CancellationToken cancellationToken = default)
        { Reads++; return Task.FromResult(Inspector.Diff(Root("historical"), path, Comparison.Revisions, options ?? DiffOptions.Default) with { Revisions = endpoints }); }
    }

    private sealed class History : IHistoryService
    {
        public int Reads { get; private set; }
        public Task<HistoryPage> PageAsync(string repository, HistoryQuery query, CancellationToken cancellationToken = default)
        { Reads++; return Task.FromResult(new HistoryPage(repository, new('b', 40), null, 0, 30, [new(new('b', 40), "fix: synthetic review", "Example", "2026-01-01")], null)); }
        public Task<CommitDetail> CommitAsync(string repository, string revision, CancellationToken cancellationToken = default)
        { Reads++; return Task.FromResult(new CommitDetail(repository, revision, [new('a', 40)], "Example", "Example", "Synthetic commit")); }
    }
}
