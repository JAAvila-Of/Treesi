using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Treesi.Application;
using Treesi.Desktop.Graph;
using Treesi.Domain;

namespace Treesi.Desktop;

internal sealed record GraphSession(CommitGraphPage? Page, IReadOnlyList<GraphRow> Rows, string? Selected, bool Visible, bool Inspector, string? Search, string? Draft, double Offset, string? Notice, GraphScope? References, string? ReferenceFilter, double HorizontalOffset);

public sealed partial class MainWindow
{
    private readonly IGraphService? _graphs;
    private CommitGraphPage? _graphPage;
    private GraphScope? _graphReferenceScope;
    private IReadOnlyList<GraphRow> _graphRows = [];
    private CancellationTokenSource? _readingGraph, _reviewingGraph;
    private bool _graphVisible, _graphInspector, _updatingGraph;

    private void InitializeGraph()
    {
        var laneScroll = Required<ScrollBar>("GraphLaneScroll");
        laneScroll.KeyDown += (_, args) =>
        {
            double? value = args.Key switch
            {
                Avalonia.Input.Key.Left => laneScroll.Value - laneScroll.SmallChange,
                Avalonia.Input.Key.Right => laneScroll.Value + laneScroll.SmallChange,
                Avalonia.Input.Key.Home => 0,
                Avalonia.Input.Key.End => laneScroll.Maximum,
                Avalonia.Input.Key.PageUp => laneScroll.Value - laneScroll.LargeChange,
                Avalonia.Input.Key.PageDown => laneScroll.Value + laneScroll.LargeChange,
                _ => null
            };
            if (value is null) return;
            laneScroll.Value = Math.Clamp(value.Value, 0, laneScroll.Maximum); args.Handled = true;
        };
        Required<ScrollBar>("GraphLaneScroll").PropertyChanged += (_, args) =>
        {
            if (args.Property == RangeBase.ValueProperty)
                GraphLaneCanvas.SetHorizontalOffset(Required<Control>("GraphContent"), Required<ScrollBar>("GraphLaneScroll").Value);
        };
        Required<Button>("GraphViewButton").Click += (_, _) =>
        {
            if (_snapshot is null || _graphs is null) return;
            ShowGraph(); if (_graphPage is null) PendingInspection = LoadGraphAsync();
        };
        Required<Button>("ChangesViewButton").Click += (_, _) => { if (_snapshot is not null) { _readingGraph?.Cancel(); _reviewingGraph?.Cancel(); if (_graphVisible) _comparingRevisions?.Cancel(); ShowComparison(); } };
        Required<Button>("BackToGraphButton").Click += (_, _) => ShowGraph();
        Required<Button>("GraphRefreshButton").Click += (_, _) => PendingInspection = LoadGraphAsync(reference: _graphPage?.Scope.Reference);
        Required<Button>("GraphGoButton").Click += (_, _) => PendingInspection = LoadGraphAsync(reference: string.IsNullOrWhiteSpace(Required<TextBox>("GraphStartInput").Text) ? null : Required<TextBox>("GraphStartInput").Text);
        Required<Button>("MoreGraphButton").Click += (_, _) => PendingInspection = LoadGraphAsync(true);
        Required<Button>("GraphDetailsButton").Click += (_, _) => { _graphInspector = !_graphInspector; UpdateGraphInspector(); };
        Required<Button>("CloseGraphDetailsButton").Click += (_, _) => { _graphInspector = false; UpdateGraphInspector(); Required<Button>("GraphDetailsButton").Focus(); };
        Required<Button>("ReviewGraphCommitButton").Click += (_, _) => PendingInspection = ReviewGraphCommitAsync();
        Required<Button>("GraphReviewButton").Click += (_, _) => PendingInspection = ReviewGraphCommitAsync();
        Required<ListBox>("GraphCommits").SelectionChanged += (_, _) => { if (!_updatingGraph) { _reviewingGraph?.Cancel(); if (_graphVisible) _comparingRevisions?.Cancel(); RevealSelectedGraphLane(); UpdateGraphInspector(); } };
        Required<ListBox>("GraphCommits").KeyDown += (_, args) => { if (args.Key == Avalonia.Input.Key.Enter) { args.Handled = true; PendingInspection = ReviewGraphCommitAsync(); } };
        Required<TextBox>("GraphStartInput").KeyDown += (_, args) => { if (args.Key == Avalonia.Input.Key.Enter) { args.Handled = true; PendingInspection = LoadGraphAsync(reference: string.IsNullOrWhiteSpace(Required<TextBox>("GraphStartInput").Text) ? null : Required<TextBox>("GraphStartInput").Text); } };
        Required<ListBox>("GraphReferences").SelectionChanged += (_, _) =>
        {
            if (_updatingGraph || Required<ListBox>("GraphReferences").SelectedItem is not GraphReferenceChoice { Reference: var reference }) return;
            PendingInspection = LoadGraphAsync(reference: reference.Kind == "all" ? null : reference.Name);
        };
        Required<TextBox>("GraphReferenceFilter").PropertyChanged += (_, args) => { if (args.Property == TextBox.TextProperty && !_updatingGraph) UpdateGraphReferences(); };
        Required<TextBox>("GraphFindInput").KeyDown += (_, args) => { if (args.Key == Avalonia.Input.Key.Enter) { FindGraphCommit(); args.Handled = true; } };
    }

    private void SetGraphVisibility(bool visible)
    {
        _graphVisible = visible;
        Required<Control>("GraphFrame").IsVisible = visible;
        Required<Button>("GraphViewButton").IsEnabled = _snapshot is not null && _graphs is not null;
        Required<Button>("ChangesViewButton").IsEnabled = _snapshot is not null;
        Required<Button>("GraphViewButton").Classes.Set("active-mode", visible);
        Required<Button>("ChangesViewButton").Classes.Set("active-mode", !visible);
        Required<Control>("BackToGraphButton").IsVisible = !visible && _graphPage is not null;
        if (visible) { Required<Control>("HomeSurface").IsVisible = false; Required<Control>("WorkspaceFrame").IsVisible = false; }
    }

    private void ShowGraph()
    {
        if (_snapshot is null || _graphs is null) return;
        _reviewingGraph?.Cancel(); _comparingRevisions?.Cancel();
        CloseWorkspacePicker(false); CloseRevisionPicker(true, false); CloseComparisonOptions(false);
        Required<Control>("CommitDetailsPanel").IsVisible = false;
        SetGraphVisibility(true); ApplyPresentation();
        Required<Button>("FocusWorkspaceButton").IsVisible = true;
        Required<Button>("FocusWorkspaceButton").IsEnabled = true;
        ListRegion(Required<ListBox>("GraphCommits"), Required<TextBox>("GraphStartInput")).Focus();
    }

    private async Task LoadGraphAsync(bool more = false, string? reference = null)
    {
        if (_snapshot is null || _graphs is null) return;
        _readingGraph?.Cancel(); _readingGraph?.Dispose(); _reviewingGraph?.Cancel();
        if (_graphVisible) _comparingRevisions?.Cancel();
        var cancellation = _readingGraph = new CancellationTokenSource(); var root = _snapshot.Root; var version = _sessionVersion;
        var previous = _graphPage; var selected = (Required<ListBox>("GraphCommits").SelectedItem as GraphRow)?.Commit.CommitId;
        var offset = Required<ListBox>("GraphCommits").FindDescendantOfType<ScrollViewer>()?.Offset ?? default;
        var horizontal = Required<ScrollBar>("GraphLaneScroll");
        var horizontalOffset = horizontal.Value;
        var query = more && previous is not null ? new GraphQuery(previous.Scope.Reference, Math.Min(GraphQuery.MaximumCommits, previous.Limit + 200), previous.Scope) : new GraphQuery(reference);
        Required<Button>("MoreGraphButton").IsEnabled = false;
        Required<TextBlock>("GraphNotice").Text = "Reading local topology…";
        try
        {
            var page = await _graphs.ReadAsync(root, query, cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested || _readingGraph != cancellation || _snapshot?.Root != root) return;
            if (more && previous is not null && (!page.Commits.Take(previous.Commits.Count).Select(item => (item.CommitId, string.Join(' ', item.Parents))).SequenceEqual(previous.Commits.Select(item => (item.CommitId, string.Join(' ', item.Parents))))))
                throw new InspectionException("graph_changed", "The available history changed. Refresh instead of extending this snapshot.");
            _graphReferenceScope = page.Scope; UpdateGraphReferences();
            var rows = GraphLayout.Create(page);
            _graphPage = page; _graphRows = rows;
            ResizeGraphLanes();
            if (!more) horizontal.Value = 0;
            _updatingGraph = true;
            try
            {
                var list = Required<ListBox>("GraphCommits"); list.ItemsSource = rows;
                list.SelectedItem = rows.FirstOrDefault(item => item.Commit.CommitId == selected) ?? rows.FirstOrDefault();
                Required<TextBox>("GraphStartInput").Text = page.Scope.Reference ?? "";
                UpdateGraphReferences();
            }
            finally { _updatingGraph = false; }
            Required<Control>("EmptyGraph").IsVisible = rows.Count == 0;
            Required<TextBlock>("EmptyGraph").Text = "No local commits in this scope. Choose another reference or refresh after creating a commit.";
            Required<TextBlock>("GraphNotice").Text = $"{rows.Count} commits · pinned local tips" + (page.HasMore ? " · more available" : "") + (page.IsShallow ? " · shallow history" : "") + (page.Scope.ReferencesLimited ? " · reference list limited" : "") + (rows.LastOrDefault()?.Continues == true ? " · parents continue outside this window" : "");
            UpdateGraphInspector();
            if (!more) RevealSelectedGraphLane();
            if (more) Dispatcher.UIThread.Post(() =>
            {
                if (!_closed && version == _sessionVersion && ReferenceEquals(_graphPage, page) && Required<ListBox>("GraphCommits").FindDescendantOfType<ScrollViewer>() is { } scroll)
                {
                    scroll.Offset = offset;
                    horizontal.Value = horizontalOffset;
                }
            }, DispatcherPriority.Loaded);
        }
        catch (OperationCanceledException) { if (_readingGraph == cancellation && !_closed) Required<TextBlock>("GraphNotice").Text = "Graph loading cancelled. Refresh to continue."; }
        catch (Exception error)
        {
            if (!_closed && !cancellation.IsCancellationRequested && _readingGraph == cancellation)
            {
                Required<TextBlock>("GraphNotice").Text = (error is InspectionException known ? known.Message : "The graph could not be read.") + (previous is not null ? " Previous graph retained." : "");
                if (previous is null)
                {
                    Required<Control>("EmptyGraph").IsVisible = true;
                    Required<TextBlock>("EmptyGraph").Text = Required<TextBlock>("GraphNotice").Text;
                }
            }
        }
        finally { if (_readingGraph == cancellation && !_closed) Required<Button>("MoreGraphButton").IsEnabled = _graphPage is { HasMore: true, Limit: < GraphQuery.MaximumCommits }; }
    }

    private void ResizeGraphLanes()
    {
        var width = _graphRows.FirstOrDefault()?.LaneWidth ?? 144;
        var viewport = Math.Min(240, width);
        Required<Grid>("GraphColumnHeadings").ColumnDefinitions[1].Width = new GridLength(viewport);
        var scroll = Required<ScrollBar>("GraphLaneScroll");
        scroll.Width = viewport; scroll.ViewportSize = viewport;
        scroll.Maximum = width - viewport; scroll.SmallChange = 18; scroll.LargeChange = viewport;
        scroll.IsVisible = scroll.Maximum > 0;
    }

    private void UpdateGraphReferences()
    {
        var updating = _updatingGraph; _updatingGraph = true;
        try
        {
            var text = Required<TextBox>("GraphReferenceFilter").Text ?? "";
            var refs = new[] { new GraphReference("All local references", "", "all") }.Concat(_graphReferenceScope?.References ?? []).Where(item => item.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).Select(item => new GraphReferenceChoice(item)).ToArray();
            var list = Required<ListBox>("GraphReferences"); list.ItemsSource = refs;
            list.SelectedItem = refs.FirstOrDefault(item => _graphPage?.Scope.Reference is { } name ? item.Reference.Name == name : item.Kind == "all");
        }
        finally { _updatingGraph = updating; }
    }

    private void UpdateGraphInspector()
    {
        var row = Required<ListBox>("GraphCommits").SelectedItem as GraphRow;
        Required<Control>("GraphInspector").IsVisible = _graphInspector && row is not null;
        Required<Button>("GraphReviewButton").IsEnabled = row is not null && _history is not null && _revisions is not null;
        if (row is null) return;
        Required<TextBlock>("GraphCommitTitle").Text = row.Subject;
        Required<TextBlock>("GraphCommitIdentity").Text = row.Commit.CommitId;
        Required<TextBlock>("GraphCommitAuthor").Text = row.Author + "\n" + row.Date;
        Required<TextBlock>("GraphCommitParents").Text = row.Commit.Parents.Count == 0 ? "Initial commit · empty-tree comparison" : "Parents\n" + string.Join('\n', row.Commit.Parents);
        Required<Button>("ReviewGraphCommitButton").IsEnabled = _history is not null && _revisions is not null;
    }

    private void RevealSelectedGraphLane()
    {
        if (Required<ListBox>("GraphCommits").SelectedItem is not GraphRow row) return;
        var scroll = Required<ScrollBar>("GraphLaneScroll");
        var laneX = 12 + row.Lane * 18;
        if (laneX < scroll.Value + 8) scroll.Value = Math.Max(0, laneX - 8);
        else if (laneX > scroll.Value + scroll.ViewportSize - 8) scroll.Value = Math.Min(scroll.Maximum, laneX - scroll.ViewportSize + 8);
    }

    private async Task ReviewGraphCommitAsync()
    {
        if (_snapshot is null || _history is null || _revisions is null || Required<ListBox>("GraphCommits").SelectedItem is not GraphRow row) return;
        _reviewingGraph?.Cancel(); _reviewingGraph?.Dispose();
        var cancellation = _reviewingGraph = new CancellationTokenSource(); var root = _snapshot.Root;
        try
        {
            var detail = await _history.CommitAsync(root, row.Commit.CommitId, cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested || _reviewingGraph != cancellation || _snapshot?.Root != root) return;
            var endpoints = detail.Comparison();
            await CompareRevisionsAsync(new(endpoints.BaseName, endpoints.TargetName), endpoints, detail);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!_closed && !cancellation.IsCancellationRequested && _reviewingGraph == cancellation)
                Required<TextBlock>("GraphNotice").Text = error is InspectionException known ? known.Message : "This commit could not be reviewed.";
        }
    }

    private void FindGraphCommit(bool backwards = false)
    {
        var text = Required<TextBox>("GraphFindInput").Text;
        if (string.IsNullOrEmpty(text)) { Required<TextBox>("GraphFindInput").Focus(); return; }
        var list = Required<ListBox>("GraphCommits"); var start = list.SelectedIndex; var step = backwards ? -1 : 1;
        for (var i = 1; i <= _graphRows.Count; i++)
        {
            var index = (Math.Max(0, start) + step * i + _graphRows.Count) % _graphRows.Count; var row = _graphRows[index];
            if (!(row.Subject + " " + row.Author + " " + row.Commit.CommitId + " " + row.References).Contains(text, StringComparison.OrdinalIgnoreCase)) continue;
            list.SelectedItem = row; list.ScrollIntoView(row); return;
        }
        Required<TextBlock>("GraphNotice").Text = "No matching commit in the loaded window. Load more or choose another reference.";
    }

    private GraphSession CaptureGraph() => new(_graphPage, _graphRows, (Required<ListBox>("GraphCommits").SelectedItem as GraphRow)?.Commit.CommitId,
        _graphVisible, _graphInspector, Required<TextBox>("GraphFindInput").Text, Required<TextBox>("GraphStartInput").Text,
        Required<ListBox>("GraphCommits").FindDescendantOfType<ScrollViewer>()?.Offset.Y ?? 0, Required<TextBlock>("GraphNotice").Text, _graphReferenceScope, Required<TextBox>("GraphReferenceFilter").Text, Required<ScrollBar>("GraphLaneScroll").Value);

    private void RestoreGraph(GraphSession? saved)
    {
        _updatingGraph = true;
        try
        {
            _graphPage = saved?.Page; _graphReferenceScope = saved?.References; _graphRows = saved?.Rows ?? []; _graphInspector = saved?.Inspector ?? false;
            ResizeGraphLanes(); Required<ScrollBar>("GraphLaneScroll").Value = 0;
            var list = Required<ListBox>("GraphCommits"); list.ItemsSource = _graphRows;
            list.SelectedItem = _graphRows.FirstOrDefault(row => row.Commit.CommitId == saved?.Selected) ?? _graphRows.FirstOrDefault();
            Required<TextBox>("GraphFindInput").Text = saved?.Search ?? ""; Required<TextBox>("GraphStartInput").Text = saved?.Draft ?? "";
            Required<TextBox>("GraphReferenceFilter").Text = saved?.ReferenceFilter ?? "";
            Required<TextBlock>("GraphNotice").Text = saved is null ? "Choose Graph to read local history." : "Saved graph snapshot · Refresh to read current tips";
            Required<Control>("EmptyGraph").IsVisible = _graphRows.Count == 0;
            Required<TextBlock>("EmptyGraph").Text = saved?.Page is null ? "No graph loaded. Choose Go or Refresh to read local commits." : "No local commits in this scope. Choose another reference or refresh after creating a commit.";
            Required<Button>("MoreGraphButton").IsEnabled = _graphPage is { HasMore: true, Limit: < GraphQuery.MaximumCommits };
            UpdateGraphReferences(); UpdateGraphInspector(); SetGraphVisibility(saved?.Visible == true);
            if (saved?.Visible == true) ShowGraph();
            if (saved is not null)
            {
                var version = _sessionVersion;
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_closed && version == _sessionVersion && list.FindDescendantOfType<ScrollViewer>() is { } scroll)
                    {
                        scroll.Offset = scroll.Offset.WithY(saved.Offset);
                        Required<ScrollBar>("GraphLaneScroll").Value = saved.HorizontalOffset;
                    }
                }, DispatcherPriority.Loaded);
            }
        }
        finally { _updatingGraph = false; }
    }
}
