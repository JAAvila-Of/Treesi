using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Application;
using Treesi.Desktop.Appearance;
using Treesi.Domain;

namespace Treesi.Desktop.Tests;

[TestFixture]
public sealed class FullContextWorkspaceTests
{
    private static readonly string Root = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "workspace", "context-example");
    private static T Get<T>(Window window, string name) where T : Control => window.FindControl<T>(name)!;
    private static void Click(Window window, string name) => Get<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static MainWindow Create(Inspector inspector, string theme = "dark", IRevisionService? revisions = null) => new(inspector, null,
        new ThemeController(Avalonia.Application.Current!, new Preferences(theme)), revisions: revisions)
    { Width = 900, Height = 560 };
    private static void ApplyFull(MainWindow window, bool full)
    {
        Click(window, "DiffOptionsButton"); Get<CheckBox>(window, "FullContextToggle").IsChecked = full; Click(window, "ApplyDiffOptionsButton");
    }

    [AvaloniaTest]
    [TestCase("dark", false)]
    [TestCase("light", false)]
    [TestCase("dark", true)]
    [TestCase("light", true)]
    public async Task ExpansionKeepsTheReviewedSourceAndNavigatesSeparateEdits(string theme, bool split)
    {
        var inspector = new Inspector(); var window = Create(inspector, theme);
        try
        {
            window.Show(); Get<CheckBox>(window, "ReduceMotionToggle").IsChecked = true; await window.OpenRepositoryAsync(Root);
            Get<TabStrip>(window, "DiffModeSelector").SelectedIndex = split ? 1 : 0; window.CaptureRenderedFrame()?.Dispose();
            var editor = Get<TextEditor>(window, split ? "AfterEditor" : "DiffEditor");
            var start = editor.Text.IndexOf("var context = 7;", StringComparison.Ordinal); editor.Select(start, "var context = 7;".Length);
            Get<TextBox>(window, "DiffSearch").Text = "context";
            ApplyFull(window, true); await window.PendingInspection; Dispatcher.UIThread.RunJobs();
            editor.SelectedText.Test().Be("var context = 7;");
            editor.Text.Contains("using System;", StringComparison.Ordinal).Test().BeTrue();
            Get<TextBox>(window, "DiffSearch").Text.Test().Be("context");
            Get<TextBlock>(window, "ComparisonDescription").Text!.Contains("Full file context", StringComparison.Ordinal).Test().BeTrue();
            Get<TextBlock>(window, "BlockPosition").Text.Test().Be("2 change blocks");
            SaveFrame(window, $"full-context-{theme}-{(split ? "split" : "unified")}.png");
            Click(window, "NextBlockButton"); Click(window, "NextBlockButton");
            var selected = Get<TextEditor>(window, split ? "BeforeEditor" : "DiffEditor").SelectedText;
            selected.Contains("return requested;", StringComparison.Ordinal).Test().BeTrue();
            ApplyFull(window, false); await window.PendingInspection;
            Get<TextEditor>(window, split ? "BeforeEditor" : "DiffEditor").Text.Contains("using System;", StringComparison.Ordinal).Test().BeFalse();
            inspector.Options.Last().FullContext.Test().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task FailedOrOversizedOptionReloadKeepsThePreviousTextAndRestoresItsOptions()
    {
        var inspector = new Inspector(); var window = Create(inspector);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root); var previous = Get<TextEditor>(window, "DiffEditor").Text;
            foreach (var oversized in new[] { false, true })
            {
                inspector.Error = oversized ? null : new InspectionException("output_limit", "Git output limit reached."); inspector.Oversized = oversized;
                ApplyFull(window, true); await window.PendingInspection;
                Get<TextEditor>(window, "DiffEditor").Text.Test().Be(previous); Get<TextEditor>(window, "DiffEditor").IsVisible.Test().BeTrue();
                Get<TextBlock>(window, "OperationStatus").Text!.Contains("Previous comparison retained", StringComparison.Ordinal).Test().BeTrue();
                Click(window, "DiffOptionsButton"); Get<CheckBox>(window, "FullContextToggle").IsChecked.Test().Be(false); Click(window, "CancelDiffOptionsButton");
                Get<TextBox>(window, "DiffSearch").IsEnabled.Test().BeTrue();
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task CancellationAndLateResultsCannotCommitAnObsoleteExpansion()
    {
        var inspector = new Inspector(); var window = Create(inspector);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root); var previous = Get<TextEditor>(window, "DiffEditor").Text;
            inspector.Delayed = new(); ApplyFull(window, true); var pending = window.PendingInspection;
            Get<TextEditor>(window, "DiffEditor").Text.Test().Be(previous); Get<TextEditor>(window, "DiffEditor").IsVisible.Test().BeTrue();
            Click(window, "CancelButton"); inspector.Delayed.SetResult(Inspector.Diff(new(FullContext: true))); await pending;
            Get<TextEditor>(window, "DiffEditor").Text.Test().Be(previous);
            inspector.Delayed = new(); ApplyFull(window, true); pending = window.PendingInspection;
            Click(window, "CancelButton"); inspector.Delayed.SetException(new InspectionException("output_limit", "Late limit failure.")); await pending;
            Click(window, "DiffOptionsButton"); Get<CheckBox>(window, "FullContextToggle").IsChecked.Test().Be(false); Click(window, "CancelDiffOptionsButton");
            inspector.Delayed = new(); ApplyFull(window, true); pending = window.PendingInspection;
            await window.OpenRepositoryAsync(Root + "-other");
            inspector.Delayed.SetResult(Inspector.Diff(new(FullContext: true))); await pending;
            Get<TextEditor>(window, "DiffEditor").Text.Test().Be(previous);
            Get<TextBlock>(window, "ComparisonDescription").Text!.Contains("Context 3", StringComparison.Ordinal).Test().BeTrue();
            Get<Button>(window, "DiffOptionsButton").IsEnabled.Test().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task HistoricalExpansionReusesPinnedEndpointsWithoutResolvingNamesAgain()
    {
        var inspector = new Inspector(); var revisions = new Revisions(); var window = Create(inspector, revisions: revisions);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root); Click(window, "CompareRevisionsButton"); Click(window, "ApplyRevisionButton"); await window.PendingInspection;
            var pinned = revisions.Endpoints.Last(); ApplyFull(window, true); await window.PendingInspection;
            revisions.ResolveCalls.Test().Be(1); revisions.Endpoints.Last().Test().Be(pinned);
            Get<TextEditor>(window, "DiffEditor").Text.Contains("using System;", StringComparison.Ordinal).Test().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task FullContextIsADraftAndDefaultsRestoreTheBoundedContext()
    {
        var inspector = new Inspector(); var window = Create(inspector);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root); var calls = inspector.Options.Count;
            Click(window, "DiffOptionsButton"); Get<CheckBox>(window, "FullContextToggle").IsChecked = true;
            Get<TextBox>(window, "ContextLinesInput").IsEnabled.Test().BeFalse(); Click(window, "CancelDiffOptionsButton");
            inspector.Options.Count.Test().Be(calls);
            Click(window, "DiffOptionsButton"); Get<CheckBox>(window, "FullContextToggle").IsChecked.Test().Be(false);
            Get<CheckBox>(window, "FullContextToggle").IsChecked = true; Click(window, "ResetDiffOptionsButton");
            Get<TextBox>(window, "ContextLinesInput").IsEnabled.Test().BeTrue(); Get<CheckBox>(window, "FullContextToggle").IsChecked.Test().Be(false);
            Click(window, "ApplyDiffOptionsButton"); inspector.Options.Count.Test().Be(calls);
            Click(window, "DiffOptionsButton"); Get<TextBox>(window, "ContextLinesInput").Text = "bad";
            Get<CheckBox>(window, "FullContextToggle").IsChecked = true; Click(window, "ApplyDiffOptionsButton"); await window.PendingInspection;
            inspector.Options.Last().Test().Be(new DiffOptions(FullContext: true));
            Click(window, "DiffOptionsButton"); Get<CheckBox>(window, "FullContextToggle").IsFocused.Test().BeTrue();
        }
        finally { window.Close(); }
    }

    [Test]
    public void SourceAnchorsIgnoreMetadataAndPreferTheSameSideWhenRowsMove()
    {
        var removed = new DiffLine(DiffLineKind.Removed, 20, null, "old"); var added = new DiffLine(DiffLineKind.Added, null, 20, "new");
        var oldRows = new Dictionary<int, DiffLine> { [5] = removed, [6] = added };
        var anchor = DiffSourceMap.Capture(oldRows, 6, 2, "new")!;
        var newRows = new Dictionary<int, DiffLine> { [21] = removed, [22] = added };
        DiffSourceMap.Locate(newRows, anchor).Test().Be(22);
        DiffSourceMap.Capture(new Dictionary<int, DiffLine>(), 1, 1, "").Test().BeNull();
    }

    private static void SaveFrame(Window window, string name)
    {
        using var frame = window.CaptureRenderedFrame(); frame.Test().NotBeNull();
        if (Environment.GetEnvironmentVariable("TREESI_RENDER_OUTPUT") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory); frame!.Save(Path.Combine(directory, name), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
    private sealed class Preferences(string theme) : IThemePreferenceStore { public string? Load() => theme; public bool Save(string themeId) => true; }
    private sealed class Revisions : IRevisionService
    {
        public int ResolveCalls { get; private set; }
        public List<RevisionEndpoints> Endpoints { get; } = [];
        public Task<RevisionReferences> ReferencesAsync(string repository, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<RevisionEndpoints> ResolveAsync(string repository, RevisionSelection selection, CancellationToken cancellationToken = default)
        { ResolveCalls++; return Task.FromResult(new RevisionEndpoints(repository, selection.Base, new('a', 40), selection.Target, new('b', 40), new('a', 40), selection.Mode)); }
        public Task<RevisionComparison> CompareAsync(RevisionEndpoints endpoints, CancellationToken cancellationToken = default) => Task.FromResult(new RevisionComparison(endpoints, [new("ReviewPreferences.cs", "M")]));
        public Task<FileDiff> DiffAsync(RevisionEndpoints endpoints, string path, DiffOptions? options = null, CancellationToken cancellationToken = default)
        { Endpoints.Add(endpoints); return Task.FromResult(Inspector.Diff(options ?? DiffOptions.Default) with { Comparison = Comparison.Revisions, Revisions = endpoints }); }
    }
    private sealed class Inspector : IInspectionService
    {
        public List<DiffOptions> Options { get; } = [];
        public InspectionException? Error { get; set; }
        public bool Oversized { get; set; }
        public TaskCompletionSource<FileDiff>? Delayed { get; set; }
        public Task<RepositorySnapshot> InspectAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(new RepositorySnapshot(path, "ReviewPreferences.cs", [new("ReviewPreferences.cs", null, ".", "M")]));
        public Task<FileDiff> DiffAsync(string repository, string path, Comparison comparison, CancellationToken cancellationToken = default, DiffOptions? options = null)
        {
            options ??= DiffOptions.Default; Options.Add(options);
            if (options.FullContext && Error is not null) return Task.FromException<FileDiff>(Error);
            if (options.FullContext && Oversized) return Task.FromResult(new FileDiff(path, comparison, new string('x', 200001), false, [], Options: options));
            return options.FullContext && Delayed is not null ? Delayed.Task : Task.FromResult(Diff(options));
        }
        public static FileDiff Diff(DiffOptions options)
        {
            var source = Source.Replace("\r", "", StringComparison.Ordinal).Split('\n');
            var changes = Enumerable.Range(0, source.Length).Where(row => source[row].Contains("var context = 3;", StringComparison.Ordinal) || source[row].Contains("return requested;", StringComparison.Ordinal)).ToArray();
            var ranges = options.FullContext ? new[] { (Start: 0, End: source.Length) } : changes.Select(row => (Start: Math.Max(0, row - 3), End: Math.Min(source.Length, row + 4))).ToArray();
            var hunks = new List<DiffHunk>();
            foreach (var (start, end) in ranges)
            {
                var lines = new List<DiffLine>();
                for (var row = start; row < end; row++)
                {
                    if (changes.Contains(row))
                    {
                        lines.Add(new(DiffLineKind.Removed, row + 1, null, source[row]));
                        lines.Add(new(DiffLineKind.Added, null, row + 1, source[row].Replace("var context = 3;", "var context = 7;", StringComparison.Ordinal).Replace("return requested;", "return Math.Min(requested, maximum);", StringComparison.Ordinal)));
                    }
                    else lines.Add(new(DiffLineKind.Context, row + 1, row + 1, source[row]));
                }
                hunks.Add(new($"@@ -{start + 1},{end - start} +{start + 1},{end - start} @@", lines));
            }
            var patch = "diff --git a/ReviewPreferences.cs b/ReviewPreferences.cs\n--- a/ReviewPreferences.cs\n+++ b/ReviewPreferences.cs\n" + string.Join("\n", hunks.Select(hunk => hunk.Header + "\n" + string.Join("\n", hunk.Lines.Select(line => (line.Kind == DiffLineKind.Added ? "+" : line.Kind == DiffLineKind.Removed ? "-" : " ") + line.Text)))) + "\n";
            return new("ReviewPreferences.cs", Comparison.WorkingTree, patch, false, hunks, Options: options);
        }
        private const string Source = """
            using System;
            using System.Collections.Generic;

            namespace Treesi.Example;

            public sealed class ReviewPreferences
            {
                private readonly int maximum = 50;

                public ReviewPreferences(int maximum)
                {
                    ArgumentOutOfRangeException.ThrowIfNegative(maximum);
                    this.maximum = maximum;
                }

                public ReviewOptions Create(string repository)
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(repository);

                    // Keep review choices local to the current session.
                    var context = 3;
                    var whitespace = WhitespaceMode.Exact;

                    return new ReviewOptions(repository, context, whitespace);
                }

                public IReadOnlyList<string> SupportedComparisons()
                {
                    return new[]
                    {
                        "Working tree vs Index",
                        "Index vs HEAD",
                        "Working tree vs HEAD",
                        "Pinned local commits"
                    };
                }

                public int ResolveContext(int requested)
                {
                    ArgumentOutOfRangeException.ThrowIfNegative(requested);

                    // Respect the configured review budget.
                    return requested;
                }
            }
            """;
    }
}
