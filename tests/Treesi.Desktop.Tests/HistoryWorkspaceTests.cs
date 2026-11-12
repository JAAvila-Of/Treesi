using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using AvaloniaEdit;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Application;
using Treesi.Desktop.Appearance;
using Treesi.Domain;

namespace Treesi.Desktop.Tests;

[TestFixture]
public sealed class HistoryWorkspaceTests
{
    private static readonly string Root = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "workspace", "treesi");
    private static readonly string Merge = new('b', 40), First = new('a', 40), Second = new('c', 40);
    private static T Get<T>(Window window, string name) where T : Control => window.FindControl<T>(name)!;
    private static void Click(Window window, string name) => Get<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static MainWindow Create(History history, Revisions revisions, string theme = "dark") => new(new Working(), null,
        new ThemeController(Avalonia.Application.Current!, new Preferences(theme)), revisions: revisions, history: history);

    [AvaloniaTest]
    [TestCase("dark")]
    [TestCase("light")]
    public async Task HistoryKeepsDiffDominantAndReviewsEachMergeParentInBothThemes(string theme)
    {
        var history = new History(); var revisions = new Revisions(); var window = Create(history, revisions, theme);
        window.Width = 900; window.Height = 560;
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root); history.Queries.Count.Test().Be(0);
            Click(window, "HistoryButton"); await window.PendingInspection; history.Queries.Count.Test().Be(1);
            Get<ListBox>(window, "HistoryCommits").SelectedIndex = 0; await window.PendingInspection;
            revisions.Diffs.Last().BaseCommitId.Test().Be(First); revisions.Diffs.Last().TargetCommitId.Test().Be(Merge);
            Get<TextEditor>(window, "DiffEditor").Text.Contains("historical", StringComparison.Ordinal).Test().BeTrue();
            Get<Control>(window, "HistorySidebar").IsVisible.Test().BeTrue();
            var surface = Get<Control>(window, "DiffSurface"); window.CaptureRenderedFrame()?.Dispose();
            var body = Get<Control>(window, "WorkspaceFrame").Bounds;
            (surface.Bounds.Height >= body.Height * .80).Test().BeTrue();
            (surface.Bounds.Width * surface.Bounds.Height >= body.Width * body.Height * .60).Test().BeTrue();
            SaveFrame(window, $"history-{theme}-compact.png");
            Click(window, "RevisionEndpointsButton");
            Get<TextBox>(window, "CommitMessage").Text.Test().Be("merge: combine branches\n\nUntrusted message displayed as plain text.\n");
            foreach (var name in new[] { "CommitParentSelector", "ApplyCommitParentButton", "CloseCommitDetailsButton", "CommitWorkingButton" }) AssertInside(window, Get<Control>(window, name));
            SaveFrame(window, $"commit-details-{theme}-compact.png");
            Get<ComboBox>(window, "CommitParentSelector").SelectedIndex = 1;
            Click(window, "ApplyCommitParentButton"); await window.PendingInspection;
            revisions.Diffs.Last().BaseCommitId.Test().Be(Second);
            Get<Button>(window, "RevisionEndpointsButton").Content!.ToString()!.Contains("Parent 2", StringComparison.Ordinal).Test().BeTrue();
            Click(window, "HistoryFilesButton"); Get<ListBox>(window, "ChangedFiles").IsVisible.Test().BeTrue();
            Click(window, "RevisionEndpointsButton"); Click(window, "CommitWorkingButton"); await window.PendingInspection;
            Get<TextEditor>(window, "DiffEditor").Text.Test().Be("working content");
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task PagesUsePinnedStartAndFileScopeAndInitialCommitHasEmptyBeforePane()
    {
        var history = new History(); var revisions = new Revisions(); var window = Create(history, revisions);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root);
            Click(window, "HistoryButton"); await window.PendingInspection;
            Click(window, "NextHistoryButton"); await window.PendingInspection;
            history.Queries.Last().Test().Be(new HistoryQuery(Merge, Offset: 30));
            Click(window, "PreviousHistoryButton"); await window.PendingInspection;
            history.Queries.Last().Test().Be(new HistoryQuery(Merge));
            Get<ComboBox>(window, "HistoryScopeSelector").SelectedIndex = 1; await window.PendingInspection;
            history.Queries.Last().Path.Test().Be("sample.txt");
            Get<ListBox>(window, "HistoryCommits").SelectedIndex = 1; await window.PendingInspection;
            revisions.Diffs.Last().IsRootComparison.Test().BeTrue();
            Get<TextBlock>(window, "BeforeLabel").Text.Test().Be("EMPTY TREE");
            Click(window, "RevisionEndpointsButton");
            Get<ComboBox>(window, "CommitParentSelector").Items.Count.Test().Be(1);
            Click(window, "CloseCommitDetailsButton");
            Get<Control>(window, "CommitDetailsPanel").IsVisible.Test().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task LateHistoryAndCommitResultsCannotReplaceANewRoot()
    {
        var history = new History(); var revisions = new Revisions(); var window = Create(history, revisions);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root);
            history.DelayedPage = new(); Click(window, "HistoryButton"); var loading = window.PendingInspection;
            var other = Root + "-other"; await window.OpenRepositoryAsync(other);
            history.DelayedPage.SetResult(History.Page(Root, new())); await loading;
            Get<Control>(window, "HistorySidebar").IsVisible.Test().BeFalse();
            history.DelayedPage = null; Click(window, "HistoryButton"); await window.PendingInspection;
            history.DelayedCommit = new(); Get<ListBox>(window, "HistoryCommits").SelectedIndex = 0; var choosing = window.PendingInspection;
            await window.OpenRepositoryAsync(Root);
            history.DelayedCommit.SetResult(History.Detail(other, Merge)); await choosing;
            revisions.Diffs.Count.Test().Be(0); Get<TextEditor>(window, "DiffEditor").Text.Test().Be("working content");
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task FailedHistoryRefreshKeepsTheLoadedPageAndItsPaginationScope()
    {
        var history = new History(); var window = Create(history, new Revisions());
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root); Click(window, "HistoryButton"); await window.PendingInspection;
            Get<TextBox>(window, "HistoryStartInput").Text = "missing"; Click(window, "ReloadHistoryButton"); await window.PendingInspection;
            Get<ListBox>(window, "HistoryCommits").Items.Count.Test().Be(30);
            Get<TextBlock>(window, "HistoryNotice").Text.Test().Be("This local revision is unavailable.");
            Click(window, "NextHistoryButton"); await window.PendingInspection;
            history.Queries.Last().Start.Test().Be(Merge); history.Queries.Last().Offset.Test().Be(30);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task LargeCommitMessagesUseAnExplicitBoundedDesktopPreview()
    {
        var history = new History { Message = new string('x', 20000) }; var window = Create(history, new Revisions());
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root); Click(window, "HistoryButton"); await window.PendingInspection;
            Get<ListBox>(window, "HistoryCommits").SelectedIndex = 0; await window.PendingInspection;
            Get<ListBox>(window, "ChangedFiles").IsVisible.Test().BeFalse();
            Click(window, "RevisionEndpointsButton");
            var text = Get<TextBox>(window, "CommitMessage").Text!;
            (text.Length < 16500).Test().BeTrue(); text.Contains("preview limited", StringComparison.Ordinal).Test().BeTrue();
        }
        finally { window.Close(); }
    }

    private static void AssertInside(MainWindow window, Control control)
    {
        var point = control.TranslatePoint(default, window)!.Value;
        (point.X >= 0 && point.X + control.Bounds.Width <= window.ClientSize.Width + 1).Test().BeTrue();
        (point.Y >= 0 && point.Y + control.Bounds.Height <= window.ClientSize.Height + 1).Test().BeTrue();
    }
    private static void SaveFrame(MainWindow window, string name)
    {
        Get<CheckBox>(window, "ReduceMotionToggle").IsChecked = true;
        using var frame = window.CaptureRenderedFrame(); frame.Test().NotBeNull();
        if (Environment.GetEnvironmentVariable("TREESI_RENDER_OUTPUT") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory); frame!.Save(Path.Combine(directory, name), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
    private sealed class Preferences(string theme) : IThemePreferenceStore { public string? Load() => theme; public bool Save(string themeId) => true; }
    private sealed class Working : IInspectionService
    {
        public Task<RepositorySnapshot> InspectAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(new RepositorySnapshot(path, null, [new("sample.txt", null, ".", "M")]));
        public Task<FileDiff> DiffAsync(string repository, string path, Comparison comparison, CancellationToken cancellationToken = default, DiffOptions? options = null) => Task.FromResult(new FileDiff(path, comparison, "working content", false, []));
    }
    private sealed class History : IHistoryService
    {
        public List<HistoryQuery> Queries { get; } = [];
        public TaskCompletionSource<HistoryPage>? DelayedPage { get; set; }
        public TaskCompletionSource<CommitDetail>? DelayedCommit { get; set; }
        public string? Message { get; init; }
        public static HistoryPage Page(string root, HistoryQuery query) => new(root, Merge, query.Path, query.Offset, query.Limit,
            Enumerable.Range(query.Offset, query.Limit).Select(index => new CommitSummary(index == 0 ? Merge : index.ToString("x40"), index == 0 ? "merge: combine branches" : $"fix: refine diff rendering {index}", "Treesi Example", "2026-01-10T12:00:00+00:00")).ToArray(), query.Offset == 0 ? 30 : null);
        public static CommitDetail Detail(string root, string id) => new(root, id, id == Merge ? [First, Second] : [], "Treesi Example <example@example.invalid>", "Treesi Example <example@example.invalid>", "merge: combine branches\n\nUntrusted message displayed as plain text.\n");
        public Task<HistoryPage> PageAsync(string repository, HistoryQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            if (query.Start == "missing") return Task.FromException<HistoryPage>(new InspectionException("revision_not_found", "This local revision is unavailable."));
            return DelayedPage?.Task ?? Task.FromResult(Page(repository, query));
        }
        public Task<CommitDetail> CommitAsync(string repository, string revision, CancellationToken cancellationToken = default) => DelayedCommit?.Task ?? Task.FromResult(Detail(repository, revision) with { Message = Message ?? Detail(repository, revision).Message });
    }
    private sealed class Revisions : IRevisionService
    {
        public List<RevisionEndpoints> Diffs { get; } = [];
        public Task<RevisionReferences> ReferencesAsync(string repository, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<RevisionEndpoints> ResolveAsync(string repository, RevisionSelection selection, CancellationToken cancellationToken = default) => throw new InvalidOperationException("History must reuse pinned commits.");
        public Task<RevisionComparison> CompareAsync(RevisionEndpoints endpoints, CancellationToken cancellationToken = default) => Task.FromResult(new RevisionComparison(endpoints, [new("sample.txt", "M")]));
        public Task<FileDiff> DiffAsync(RevisionEndpoints endpoints, string path, DiffOptions? options = null, CancellationToken cancellationToken = default)
        {
            Diffs.Add(endpoints);
            return Task.FromResult(new FileDiff(path, Comparison.Revisions, "diff --git a/sample.txt b/sample.txt\n--- a/sample.txt\n+++ b/sample.txt\n@@ -1,3 +1,3 @@\n first\n-original\n+historical\n last\n", false,
                [new("@@ -1,3 +1,3 @@", [new(DiffLineKind.Context, 1, 1, "first"), new(DiffLineKind.Removed, 2, null, "original"), new(DiffLineKind.Added, null, 2, "historical"), new(DiffLineKind.Context, 3, 3, "last")])], Options: options ?? DiffOptions.Default, Revisions: endpoints));
        }
    }
}
