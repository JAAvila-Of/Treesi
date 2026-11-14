using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Application;
using Treesi.Desktop.Appearance;
using Treesi.Desktop.Graph;
using Treesi.Desktop.Workspaces;
using Treesi.Domain;

namespace Treesi.Desktop.Tests;

[TestFixture]
public sealed class GraphWorkspaceTests
{
    private static readonly string Root = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "workspace", "treesi");
    private static string Id(int number) => number.ToString("x40");
    private static T Get<T>(Window window, string name) where T : Control => window.FindControl<T>(name)!;
    private static void Click(Window window, string name) => Get<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static MainWindow Create(IGraphService graphs, History? history = null, Revisions? revisions = null, string theme = "dark") => new(new Working(), null,
        new ThemeController(Avalonia.Application.Current!, new Preferences(theme)), revisions: revisions ?? new(), history: history ?? new(), graphs: graphs);

    [AvaloniaTest]
    [TestCase("dark")]
    [TestCase("light")]
    public async Task GraphDominatesCompactWindowVirtualizesAndReturnsFromReview(string theme)
    {
        var graphs = new Graphs(); var revisions = new Revisions(); var window = Create(graphs, revisions: revisions, theme: theme);
        window.Width = 900; window.Height = 560;
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root); graphs.Queries.Count.Test().Be(0);
            Click(window, "GraphViewButton"); await window.PendingInspection;
            var list = Get<ListBox>(window, "GraphCommits"); list.Items.Count.Test().Be(200);
            using (window.CaptureRenderedFrame()) { }
            (list.GetVisualDescendants().OfType<ListBoxItem>().Count() < 40).Test().BeTrue();
            var body = Get<Control>(window, "GraphFrame").Bounds;
            (list.Bounds.Height >= body.Height * .80).Test().BeTrue();
            (list.Bounds.Width * list.Bounds.Height >= body.Width * body.Height * .60).Test().BeTrue();
            foreach (var name in new[] { "GraphStartInput", "GraphFindInput", "GraphReviewButton", "MoreGraphButton" })
            {
                var control = Get<Control>(window, name); var point = control.TranslatePoint(default, window)!.Value;
                (point.X >= 0 && point.X + control.Bounds.Width <= window.ClientSize.Width + 1).Test().BeTrue();
            }
            SaveFrame(window, $"graph-{theme}-compact.png");
            list.SelectedIndex = 80; list.ScrollIntoView(list.SelectedItem!);
            using (window.CaptureRenderedFrame()) { }
            var offset = list.FindDescendantOfType<ScrollViewer>()!.Offset.Y;
            var selected = (GraphRow)list.SelectedItem!;
            Click(window, "GraphDetailsButton"); Get<TextBlock>(window, "GraphCommitIdentity").Text.Test().Be(selected.Commit.CommitId);
            Click(window, "ReviewGraphCommitButton"); await window.PendingInspection;
            revisions.Diffs.Last().TargetCommitId.Test().Be(selected.Commit.CommitId);
            Get<Control>(window, "GraphFrame").IsVisible.Test().BeFalse();
            Click(window, "BackToGraphButton"); Get<Control>(window, "GraphFrame").IsVisible.Test().BeTrue();
            using (window.CaptureRenderedFrame()) { }
            (Math.Abs(list.FindDescendantOfType<ScrollViewer>()!.Offset.Y - offset) < 1).Test().BeTrue();
            ((GraphRow)list.SelectedItem!).Commit.CommitId.Test().Be(selected.Commit.CommitId); graphs.Queries.Count.Test().Be(1);
            Click(window, "MoreGraphButton"); await window.PendingInspection;
            graphs.Queries.Last().Scope.Test().NotBeNull(); list.Items.Count.Test().Be(400);
            ((GraphRow)list.SelectedItem!).Commit.CommitId.Test().Be(selected.Commit.CommitId);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task LateGraphOrCommitCannotReplaceAnotherRepositoryAndTabsRestoreWithoutRead()
    {
        var graphs = new Graphs(); var history = new History(); var revisions = new Revisions(); var window = Create(graphs, history, revisions);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root); Click(window, "GraphViewButton"); await window.PendingInspection;
            Get<ListBox>(window, "GraphCommits").SelectedIndex = 8;
            Get<TextBox>(window, "GraphReferenceFilter").Text = "main";
            history.Delayed = new(); Click(window, "GraphReviewButton"); var reviewing = window.PendingInspection;
            await window.OpenRepositoryAsync(Root + "-other"); history.Delayed.SetResult(History.Detail(Root, Id(992))); await reviewing;
            revisions.Diffs.Count.Test().Be(0);
            Get<TextBox>(window, "GraphReferenceFilter").Text.Test().Be("");
            Get<TabStrip>(window, "RepositoryTabs").SelectedItem = Get<TabStrip>(window, "RepositoryTabs").Items.Cast<RepositoryTab>().Single(tab => tab.Root == Root);
            Get<Control>(window, "GraphFrame").IsVisible.Test().BeTrue();
            Get<ListBox>(window, "GraphCommits").SelectedIndex.Test().Be(8);
            Get<TextBox>(window, "GraphReferenceFilter").Text.Test().Be("main"); graphs.Queries.Count.Test().Be(1);
            graphs.Delayed = new(); Click(window, "GraphRefreshButton"); var loading = window.PendingInspection;
            await window.OpenRepositoryAsync(Root + "-other"); graphs.Delayed.SetResult(Graphs.Page(Root, new())); await loading;
            Get<Control>(window, "GraphFrame").IsVisible.Test().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase("dark")]
    [TestCase("light")]
    public async Task WideGraphPansLanesWhileKeepingCommitInformationFixed(string theme)
    {
        var window = Create(new WideGraphs(), theme: theme);
        window.Width = 900; window.Height = 560;
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root);
            Click(window, "GraphViewButton"); await window.PendingInspection;
            Get<CheckBox>(window, "ReduceMotionToggle").IsChecked = true;
            using (window.CaptureRenderedFrame()) { }
            var list = Get<ListBox>(window, "GraphCommits");
            list.Items.Count.Test().Be(200);
            (list.GetVisualDescendants().OfType<ListBoxItem>().Count() < 40).Test().BeTrue();
            var scroll = Get<ScrollBar>(window, "GraphLaneScroll");
            scroll.IsVisible.Test().BeTrue(); scroll.Maximum.Test().Be(198d);
            var lane = list.GetVisualDescendants().OfType<GraphLaneCanvas>().First();
            lane.Bounds.Width.Test().Be(240d);
            var headings = Get<Grid>(window, "GraphColumnHeadings");
            var headingBefore = headings.TranslatePoint(default, window)!.Value.X;
            var laneBefore = lane.TranslatePoint(default, window)!.Value.X;
            scroll.Value = 100;
            using (window.CaptureRenderedFrame()) { }
            GraphLaneCanvas.GetHorizontalOffset(lane).Test().Be(100d);
            (headingBefore - headings.TranslatePoint(default, window)!.Value.X).Test().Be(0d);
            (laneBefore - lane.TranslatePoint(default, window)!.Value.X).Test().Be(0d);
            Click(window, "GraphDetailsButton"); scroll.Value.Test().Be(100d);
            Click(window, "CloseGraphDetailsButton"); scroll.Value.Test().Be(100d);
            scroll.Focus().Test().BeTrue();
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            scroll.Value.Test().Be(118d);
            list.SelectedIndex = 23;
            (scroll.Value > 118).Test().BeTrue();
            (12 + 23 * 18 - scroll.Value <= scroll.ViewportSize - 8).Test().BeTrue();
            list.SelectedIndex = 80; list.ScrollIntoView(list.SelectedItem!);
            scroll.Value = 100;
            using (window.CaptureRenderedFrame()) { }
            Click(window, "GraphReviewButton"); await window.PendingInspection;
            Click(window, "BackToGraphButton");
            using (window.CaptureRenderedFrame()) { }
            scroll.Value.Test().Be(100d); list.SelectedIndex.Test().Be(80);
            await window.OpenRepositoryAsync(Root + "-other");
            Get<TabStrip>(window, "RepositoryTabs").SelectedIndex = 0;
            using (window.CaptureRenderedFrame()) { }
            scroll.Value.Test().Be(100d); list.SelectedIndex.Test().Be(80);
            SaveFrame(window, $"graph-{theme}-wide-lanes.png");
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task ChangedPrefixIsRefusedAndReferenceScopeCanBeNarrowed()
    {
        var graphs = new Graphs(); var window = Create(graphs);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root); Click(window, "GraphViewButton"); await window.PendingInspection;
            graphs.ChangePrefix = true; Click(window, "MoreGraphButton"); await window.PendingInspection;
            Get<ListBox>(window, "GraphCommits").Items.Count.Test().Be(200);
            Get<TextBlock>(window, "GraphNotice").Text!.Contains("Previous graph retained", StringComparison.Ordinal).Test().BeTrue();
            graphs.ChangePrefix = false;
            Get<ListBox>(window, "GraphReferences").SelectedIndex = 1; await window.PendingInspection;
            graphs.Queries.Last().Reference.Test().Be("refs/heads/main");
        }
        finally { window.Close(); }
    }

    [Test]
    public void LayoutKeepsMergeEdgesAndMarksTruncatedParents()
    {
        var scope = new GraphScope(Root, [Id(4)], []);
        GraphCommit Commit(int id, params int[] parents) => new(Id(id), parents.Select(Id).ToArray(), "example", "Example", "2026-01-01");
        var rows = GraphLayout.Create(new(scope, [Commit(4, 3, 2), Commit(3, 1), Commit(2, 1), Commit(1)], 200, false, false));
        rows[0].Segments.Count.Test().Be(2); rows[0].LaneCount.Test().Be(2);
        rows[2].Segments.Any(segment => segment.From != segment.To && !segment.Incoming).Test().BeTrue();
        rows.Last().Continues.Test().BeFalse();
        GraphLayout.Create(new(scope, [Commit(4, 3, 2)], 1, true, false)).Single().Continues.Test().BeTrue();
        GraphLayout.Create(new(scope, [Commit(100, Enumerable.Range(1, GraphLayout.MaximumLanes).ToArray())], 1, false, false)).Single().LaneCount.Test().Be(GraphLayout.MaximumLanes);
        Action tooWide = () => GraphLayout.Create(new(scope, [Commit(100, Enumerable.Range(1, GraphLayout.MaximumLanes + 1).ToArray())], 1, false, false));
        tooWide.Test().Throw<InspectionException>();
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
    private sealed class Graphs : IGraphService
    {
        public List<GraphQuery> Queries { get; } = [];
        public TaskCompletionSource<CommitGraphPage>? Delayed { get; set; }
        public bool ChangePrefix { get; set; }
        public static CommitGraphPage Page(string root, GraphQuery query) => new(query.Scope ?? new(root, [Id(1000)], [new("refs/heads/main", Id(1000), "branch"), new("refs/heads/feature", Id(998), "branch"), new("refs/tags/v0.1", Id(997), "tag")], query.Reference),
            Enumerable.Range(0, query.Limit).Select(index => new GraphCommit(Id(1000 - index), index switch { 0 => [Id(999), Id(998)], 1 or 2 => [Id(997)], _ => [Id(999 - index)] }, index switch { 0 => "merge: combine graph navigation", 1 => "feat: preserve review position", 2 => "feat: inspect feature changes", _ => $"feat: refine review {index}" }, "Treesi Example", "2026-01-10T12:00:00Z")).ToArray(), query.Limit, query.Limit < 1000, false);
        public Task<CommitGraphPage> ReadAsync(string repository, GraphQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query); var page = Page(repository, query);
            if (ChangePrefix) page = page with { Commits = page.Commits.Select((item, index) => index == 0 ? item with { CommitId = Id(2000) } : item).ToArray() };
            return Delayed?.Task ?? Task.FromResult(page);
        }
    }
    private sealed class WideGraphs : IGraphService
    {
        public Task<CommitGraphPage> ReadAsync(string repository, GraphQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CommitGraphPage(new(repository, Enumerable.Range(0, 24).Select(index => Id(10000 - index)).ToArray(), [], null),
                Enumerable.Range(0, 200).Select(index => new GraphCommit(Id(10000 - index), [Id(9976 - index)], $"Parallel history {index}", "Treesi Example", "2026-01-10T12:00:00Z")).ToArray(), 200, false, false));
    }

    private sealed class History : IHistoryService
    {
        public TaskCompletionSource<CommitDetail>? Delayed { get; set; }
        public static CommitDetail Detail(string root, string id) => new(root, id, [Id(999)], "Treesi Example", "Treesi Example", "feat: review");
        public Task<HistoryPage> PageAsync(string repository, HistoryQuery query, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<CommitDetail> CommitAsync(string repository, string revision, CancellationToken cancellationToken = default) => Delayed?.Task ?? Task.FromResult(Detail(repository, revision));
    }
    private sealed class Revisions : IRevisionService
    {
        public List<RevisionEndpoints> Diffs { get; } = [];
        public Task<RevisionReferences> ReferencesAsync(string repository, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<RevisionEndpoints> ResolveAsync(string repository, RevisionSelection selection, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Graph review must reuse pinned commits.");
        public Task<RevisionComparison> CompareAsync(RevisionEndpoints endpoints, CancellationToken cancellationToken = default) => Task.FromResult(new RevisionComparison(endpoints, [new("sample.txt", "M")]));
        public Task<FileDiff> DiffAsync(RevisionEndpoints endpoints, string path, DiffOptions? options = null, CancellationToken cancellationToken = default)
        {
            Diffs.Add(endpoints); return Task.FromResult(new FileDiff(path, Comparison.Revisions, "historical content", false, [], Revisions: endpoints));
        }
    }
}
