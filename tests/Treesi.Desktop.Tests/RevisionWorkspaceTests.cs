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
public sealed class RevisionWorkspaceTests
{
    private static readonly string Root = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "workspace", "treesi");
    private static T Get<T>(Window window, string name) where T : Control => window.FindControl<T>(name)!;
    private static void Click(Window window, string name) => Get<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static MainWindow Create(Working working, Revisions revisions, History? history = null) =>
        new(working, null, new ThemeController(Avalonia.Application.Current!, new Preferences()), workspaceHistory: history, revisions: revisions);
    private static void Draft(MainWindow window, string from = "main", string to = "feature", RevisionComparisonMode mode = RevisionComparisonMode.Direct)
    {
        Click(window, "CompareRevisionsButton");
        Get<TextBox>(window, "BaseRevisionInput").Text = from; Get<TextBox>(window, "TargetRevisionInput").Text = to;
        Get<ComboBox>(window, "RevisionModeSelector").SelectedIndex = (int)mode;
    }

    [AvaloniaTest]
    public async Task ReferenceBrowsingIsExplicitAndDraftCancellationKeepsTheWorkingDiff()
    {
        var working = new Working(); var revisions = new Revisions(); var window = Create(working, revisions);
        window.Width = 900; window.Height = 560;
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root);
            Draft(window); revisions.ReferenceCalls.Test().Be(0); revisions.ResolveCalls.Test().Be(0);
            SaveFrame(window, "revision-picker-dark-compact.png");
            foreach (var name in new[] { "BaseRevisionInput", "TargetRevisionInput", "RevisionModeSelector", "ApplyRevisionButton", "CancelRevisionButton", "BrowseRevisionReferencesButton" }) AssertInside(window, Get<Control>(window, name));
            Click(window, "BrowseRevisionReferencesButton"); await window.PendingInspection;
            revisions.ReferenceCalls.Test().Be(1);
            SaveFrame(window, "local-references-dark-compact.png");
            Get<ListBox>(window, "RevisionReferencesList").SelectedIndex = 1;
            Click(window, "UseReferenceAsBaseButton");
            Get<TextBox>(window, "BaseRevisionInput").Text.Test().Be("refs/heads/feature");
            Click(window, "SwapRevisionsButton");
            Get<TextBox>(window, "TargetRevisionInput").Text.Test().Be("refs/heads/feature");
            Click(window, "CancelRevisionButton");
            Get<TextEditor>(window, "DiffEditor").Text.Test().Be("working content"); working.DiffCalls.Test().Be(1);
            revisions.ResolveCalls.Test().Be(0);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task HistoricalDiffsReusePinnedEndpointsAndReturningToWorkingRestoresItsSelection()
    {
        var working = new Working(); var revisions = new Revisions(); var history = new History(); var window = Create(working, revisions, history);
        window.Width = 900; window.Height = 560;
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root);
            Draft(window, mode: RevisionComparisonMode.MergeBase); Click(window, "ApplyRevisionButton"); await window.PendingInspection;
            Get<Control>(window, "RevisionEndpointsButton").IsVisible.Test().BeTrue();
            Get<Control>(window, "ComparisonSelector").IsVisible.Test().BeFalse();
            Get<TextBlock>(window, "WorkspaceScopeHint").Text.Test().Be("Pinned local commits");
            Get<TabStrip>(window, "DiffModeSelector").SelectedIndex = 1;
            Get<TextBlock>(window, "BeforeLabel").Text!.StartsWith("MERGE BASE", StringComparison.Ordinal).Test().BeTrue();
            Get<TextBlock>(window, "AfterLabel").Text!.Contains(new string('b', 12), StringComparison.Ordinal).Test().BeTrue();
            Click(window, "DiffOptionsButton"); Get<TextBox>(window, "ContextLinesInput").Text = "10";
            Click(window, "ApplyDiffOptionsButton"); await window.PendingInspection;
            revisions.ResolveCalls.Test().Be(1); revisions.CompareCalls.Test().Be(1); revisions.Diffs.Count.Test().Be(2);
            (revisions.Diffs[0] == revisions.Diffs[1]).Test().BeTrue();
            foreach (var theme in new[] { ThemeCatalog.Dark, ThemeCatalog.Light })
            {
                Get<ComboBox>(window, "ThemeSelector").SelectedItem = theme;
                SaveFrame(window, $"revision-{theme.Id}-compact.png");
                var canvas = Get<Control>(window, "DiffSurface"); var body = Get<Control>(window, "WorkspaceColumns");
                (canvas.Bounds.Height / body.Bounds.Height >= .80).Test().BeTrue();
                (canvas.Bounds.Width * canvas.Bounds.Height / (body.Bounds.Width * body.Bounds.Height) >= .60).Test().BeTrue();
                AssertInside(window, Get<Control>(window, "RevisionEndpointsButton")); AssertInside(window, Get<Control>(window, "CompareRevisionsButton"));
            }
            Get<ListBox>(window, "ChangedFiles").SelectedIndex = 1; await window.PendingInspection;
            ((ChangedFileItem)Get<ListBox>(window, "ChangedFiles").SelectedItem!).State.Test().Be("Deleted between commits");
            Click(window, "RecentWorkspacesButton");
            history.Entries.Single().SelectedPath.Test().Be("sample.txt");
            Click(window, "ReturnToWorkspaceButton"); Click(window, "RevisionEndpointsButton"); Click(window, "ReturnToWorkingButton"); await window.PendingInspection;
            Get<Control>(window, "ComparisonSelector").IsVisible.Test().BeTrue();
            Get<TextBlock>(window, "FileTitle").Text.Test().Be("sample.txt");
            Get<TextEditor>(window, "DiffEditor").Text.Test().Be("working content"); working.InspectCalls.Test().Be(2);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task FailureCancellationAndOpeningAnotherRootCannotCommitObsoleteComparisons()
    {
        var working = new Working(); var revisions = new Revisions(); var window = Create(working, revisions);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root);
            Draft(window, "missing"); Click(window, "ApplyRevisionButton"); await window.PendingInspection;
            Get<Control>(window, "RevisionError").IsVisible.Test().BeTrue();
            Get<TextEditor>(window, "DiffEditor").Text.Test().Be("working content"); Get<ListBox>(window, "ChangedFiles").IsEnabled.Test().BeTrue();
            var delayed = new TaskCompletionSource<RevisionEndpoints>(); revisions.DelayedResolve = delayed;
            Get<TextBox>(window, "BaseRevisionInput").Text = "slow"; Click(window, "ApplyRevisionButton"); var pending = window.PendingInspection;
            Click(window, "CancelRevisionButton"); delayed.SetResult(Revisions.Endpoints(Root, new("slow", "feature"))); await pending;
            revisions.CompareCalls.Test().Be(0);
            Get<TextEditor>(window, "DiffEditor").Text.Test().Be("working content");
            var comparison = new TaskCompletionSource<RevisionComparison>(); revisions.DelayedCompare = comparison;
            Draft(window); Click(window, "ApplyRevisionButton"); pending = window.PendingInspection;
            var another = Path.Combine(Path.GetDirectoryName(Root)!, "another"); await window.OpenRepositoryAsync(another);
            Get<ProgressBar>(window, "BusyIndicator").IsVisible.Test().BeFalse();
            comparison.SetResult(new(Revisions.Endpoints(Root, new("main", "feature")), [new("obsolete.txt", "M")])); await pending;
            Get<TextBlock>(window, "RepositoryName").Text.Test().Be("another");
            Get<Control>(window, "RevisionEndpointsButton").IsVisible.Test().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task EmptyAndFilteredHistoricalListsKeepTheirComparisonScope()
    {
        var working = new Working(); var revisions = new Revisions(); var window = Create(working, revisions);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root);
            Draft(window, "main", "main"); Click(window, "ApplyRevisionButton"); await window.PendingInspection;
            Get<TextBlock>(window, "EmptyTitle").Text.Test().Be("No changed files between these commits.");
            Get<TextBlock>(window, "OperationStatus").Text.Test().Be("Revision comparison complete · 0 files");
            Draft(window); Click(window, "ApplyRevisionButton"); await window.PendingInspection;
            Get<TextBox>(window, "FileFilter").Text = "missing";
            Get<TextBlock>(window, "EmptyTitle").Text.Test().Be("No matching files.");
            Get<TextBox>(window, "FileFilter").Text = ""; await window.PendingInspection;
            Get<TextBox>(window, "DiffSearch").IsEnabled.Test().BeTrue(); revisions.ResolveCalls.Test().Be(2);
        }
        finally { window.Close(); }
    }

    private static void SaveFrame(MainWindow window, string name)
    {
        Get<CheckBox>(window, "ReduceMotionToggle").IsChecked = true;
        using var frame = window.CaptureRenderedFrame(); frame.Test().NotBeNull();
        if (Environment.GetEnvironmentVariable("TREESI_RENDER_OUTPUT") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory); frame!.Save(Path.Combine(directory, name), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
    private static void AssertInside(MainWindow window, Control control)
    {
        var point = control.TranslatePoint(default, window)!.Value;
        (point.X >= 0 && point.X + control.Bounds.Width <= window.ClientSize.Width + 1).Test().BeTrue();
        (point.Y >= 0 && point.Y + control.Bounds.Height <= window.ClientSize.Height + 1).Test().BeTrue();
    }
    private sealed class Preferences : IThemePreferenceStore { public string? Load() => "dark"; public bool Save(string themeId) => true; }
    private sealed class History : IWorkspaceHistoryStore
    {
        public IReadOnlyList<WorkspaceEntry> Entries { get; private set; } = [];
        public IReadOnlyList<WorkspaceEntry> Load() => Entries;
        public bool Save(IReadOnlyList<WorkspaceEntry> entries) { Entries = entries.ToArray(); return true; }
    }
    private sealed class Working : IInspectionService
    {
        public int InspectCalls { get; private set; }
        public int DiffCalls { get; private set; }
        public Task<RepositorySnapshot> InspectAsync(string path, CancellationToken cancellationToken = default)
        { InspectCalls++; return Task.FromResult(new RepositorySnapshot(path, null, [new("sample.txt", null, ".", "M")])); }
        public Task<FileDiff> DiffAsync(string repository, string path, Comparison comparison, CancellationToken cancellationToken = default, DiffOptions? options = null)
        { DiffCalls++; return Task.FromResult(new FileDiff(path, comparison, "working content", false, [])); }
    }
    private sealed class Revisions : IRevisionService
    {
        public int ReferenceCalls { get; private set; }
        public int ResolveCalls { get; private set; }
        public int CompareCalls { get; private set; }
        public List<RevisionEndpoints> Diffs { get; } = [];
        public TaskCompletionSource<RevisionEndpoints>? DelayedResolve { get; set; }
        public TaskCompletionSource<RevisionComparison>? DelayedCompare { get; set; }
        public static RevisionEndpoints Endpoints(string root, RevisionSelection selection) => new(root, selection.Base, new string('a', 40), selection.Target,
            new string(selection.Base == selection.Target ? 'a' : 'b', 40), new string(selection.Mode == RevisionComparisonMode.MergeBase ? 'c' : 'a', 40), selection.Mode);
        public Task<RevisionReferences> ReferencesAsync(string repository, CancellationToken cancellationToken = default)
        { ReferenceCalls++; return Task.FromResult(new RevisionReferences([new("HEAD", new string('b', 40), "head"), new("refs/heads/feature", new string('b', 40), "branch")], false)); }
        public Task<RevisionEndpoints> ResolveAsync(string repository, RevisionSelection selection, CancellationToken cancellationToken = default)
        {
            ResolveCalls++;
            if (selection.Base == "missing") return Task.FromException<RevisionEndpoints>(new InspectionException("revision_not_found", "This reference is not available locally."));
            if (selection.Base == "slow" && DelayedResolve is not null) return DelayedResolve.Task;
            return Task.FromResult(Endpoints(repository, selection));
        }
        public Task<RevisionComparison> CompareAsync(RevisionEndpoints endpoints, CancellationToken cancellationToken = default)
        {
            CompareCalls++;
            return DelayedCompare?.Task ?? Task.FromResult(new RevisionComparison(endpoints, endpoints.BaseName == endpoints.TargetName ? [] : [new("sample.txt", "M"), new("deleted.txt", "D")]));
        }
        public Task<FileDiff> DiffAsync(RevisionEndpoints endpoints, string path, DiffOptions? options = null, CancellationToken cancellationToken = default)
        {
            Diffs.Add(endpoints);
            var lines = new DiffLine[] { new(DiffLineKind.Context, 1, 1, "first"), new(DiffLineKind.Removed, 2, null, "original"), new(DiffLineKind.Added, null, 2, "historical"), new(DiffLineKind.Context, 3, 3, "last") };
            return Task.FromResult(new FileDiff(path, Comparison.Revisions, "diff --git a/sample.txt b/sample.txt\n--- a/sample.txt\n+++ b/sample.txt\n@@ -1,3 +1,3 @@\n first\n-original\n+historical\n last\n", false,
                [new("@@ -1,3 +1,3 @@", lines)], Options: options ?? DiffOptions.Default, Revisions: endpoints));
        }
    }
}
