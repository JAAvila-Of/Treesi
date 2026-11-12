using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Application;
using Treesi.Desktop.Appearance;
using Treesi.Domain;

namespace Treesi.Desktop.Tests;

[TestFixture]
public sealed class DiffNavigationTests
{
    [AvaloniaTest]
    public async Task BlockNavigationWrapsAndKeepsItsPositionAcrossLayoutsAndThemes()
    {
        var inspector = new Inspector();
        var themes = new ThemeController(Avalonia.Application.Current!, new Preferences());
        var window = new MainWindow(inspector, null, themes);
        try
        {
            window.Show(); await window.OpenRepositoryAsync("fixture");
            using var initialFrame = window.CaptureRenderedFrame();
            Click(window, "NextBlockButton");
            window.FindControl<TextEditor>("DiffEditor")!.SelectedText.Test().Be("@@ -1,60 +1,60 @@");
            Click(window, "NextBlockButton");
            window.FindControl<TextBlock>("BlockPosition")!.Text.Test().Be("Block 2 of 2");
            (window.FindControl<TextEditor>("DiffEditor")!.VerticalOffset > 0).Test().BeTrue();
            window.FindControl<TabStrip>("DiffModeSelector")!.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            var before = window.FindControl<TextEditor>("BeforeEditor")!;
            var after = window.FindControl<TextEditor>("AfterEditor")!;
            before.SelectedText.Test().Be("@@ -900 +900 @@");
            after.SelectedText.Test().Be("@@ -900 +900 @@");
            (after.VerticalOffset > 0).Test().BeTrue();
            themes.Apply(ThemeCatalog.Light); Dispatcher.UIThread.RunJobs();
            after.SelectedText.Test().Be("@@ -900 +900 @@");
            Click(window, "NextBlockButton");
            window.FindControl<TextBlock>("BlockPosition")!.Text.Test().Be("Block 1 of 2");
            Click(window, "PreviousBlockButton");
            window.FindControl<TextBlock>("BlockPosition")!.Text.Test().Be("Block 2 of 2");
            inspector.Calls.Test().Be(1);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task SplitScrollingStaysAlignedInBothDirections()
    {
        var window = new MainWindow(new Inspector(), null, new ThemeController(Avalonia.Application.Current!, new Preferences()));
        try
        {
            window.Show(); await window.OpenRepositoryAsync("fixture");
            window.FindControl<TabStrip>("DiffModeSelector")!.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            var before = window.FindControl<TextEditor>("BeforeEditor")!;
            var after = window.FindControl<TextEditor>("AfterEditor")!;
            var scroller = before.FindDescendantOfType<ScrollViewer>()!;
            scroller.Offset = scroller.Offset.WithY(350); Dispatcher.UIThread.RunJobs();
            (before.VerticalOffset > 0).Test().BeTrue();
            (Math.Abs(before.VerticalOffset - after.VerticalOffset) < 1).Test().BeTrue();
            var otherScroller = after.FindDescendantOfType<ScrollViewer>()!;
            otherScroller.Offset = otherScroller.Offset.WithY(80); Dispatcher.UIThread.RunJobs();
            (Math.Abs(before.VerticalOffset - after.VerticalOffset) < 1).Test().BeTrue();
            before.IsReadOnly.Test().BeTrue(); after.IsReadOnly.Test().BeTrue();
            before.Options.EnableHyperlinks.Test().BeFalse(); after.Options.EnableEmailHyperlinks.Test().BeFalse();
        }
        finally { window.Close(); }
    }

    private static void Click(Window window, string name) => window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaTest]
    public async Task MetadataOnlyChangesRemainVisibleInUnifiedMode()
    {
        var window = new MainWindow(new Inspector(), null, new ThemeController(Avalonia.Application.Current!, new Preferences()));
        try
        {
            window.Show(); await window.OpenRepositoryAsync("fixture");
            window.FindControl<TabStrip>("DiffModeSelector")!.SelectedIndex = 1;
            window.FindControl<ComboBox>("ComparisonSelector")!.SelectedIndex = 2;
            await window.PendingInspection;
            window.FindControl<Control>("SplitView")!.IsVisible.Test().BeFalse();
            window.FindControl<TextEditor>("DiffEditor")!.IsVisible.Test().BeTrue();
            window.FindControl<TextEditor>("DiffEditor")!.Text.Test().Contain("new mode 100755");
            window.FindControl<TabStrip>("DiffModeSelector")!.IsEnabled.Test().BeFalse();
        }
        finally { window.Close(); }
    }

    private sealed class Preferences : IThemePreferenceStore
    {
        public string? Load() => "dark";
        public bool Save(string themeId) => true;
    }

    private sealed class Inspector : IInspectionService
    {
        public int Calls { get; private set; }
        public Task<RepositorySnapshot> InspectAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RepositorySnapshot("fixture", "sample.txt", [new FileChange("sample.txt", null, ".", "M")]));
        public Task<FileDiff> DiffAsync(string repository, string path, Comparison comparison, CancellationToken cancellationToken = default, DiffOptions? options = null)
        {
            Calls++;
            if (comparison == Comparison.Head) return Task.FromResult(new FileDiff(path, comparison,
                "diff --git a/sample.txt b/sample.txt\nold mode 100644\nnew mode 100755\n", false, []));
            var context = Enumerable.Range(1, 60).Select(i => new DiffLine(DiffLineKind.Context, i, i, $"context {i}")).ToArray();
            var hunks = new[] { new DiffHunk("@@ -1,60 +1,60 @@", context),
                new DiffHunk("@@ -900 +900 @@", [new DiffLine(DiffLineKind.Removed, 900, null, "before"), new DiffLine(DiffLineKind.Added, null, 900, "after")]) };
            var patch = string.Join("\n", hunks.SelectMany(hunk => new[] { hunk.Header }.Concat(hunk.Lines.Select(line =>
                (line.Kind == DiffLineKind.Removed ? "-" : line.Kind == DiffLineKind.Added ? "+" : " ") + line.Text)))) + "\n";
            return Task.FromResult(new FileDiff(path, comparison, patch, false, hunks));
        }
    }
}
