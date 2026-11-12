using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Application;
using Treesi.Desktop.Appearance;
using Treesi.Domain;

namespace Treesi.Desktop.Tests;

[TestFixture]
public sealed class DiffOptionsWorkspaceTests
{
    private static string Root(string name) => Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "workspace", name);
    private static T Control<T>(Window window, string name) where T : Avalonia.Controls.Control => window.FindControl<T>(name)!;
    private static void Click(Window window, string name) => Control<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static MainWindow Window(Inspector inspector) => new(inspector, null, new ThemeController(Avalonia.Application.Current!, new Preferences()));
    private static void Draft(MainWindow window, int context, WhitespaceMode whitespace)
    {
        Click(window, "DiffOptionsButton");
        Control<NumericUpDown>(window, "ContextLinesInput").Value = context;
        Control<ComboBox>(window, "WhitespaceSelector").SelectedIndex = (int)whitespace;
    }

    [AvaloniaTest]
    public async Task DraftsAreCancelableAndApplyingAnUnchangedChoiceDoesNotReloadGit()
    {
        var inspector = new Inspector(); var window = Window(inspector);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root("treesi"));
            var selection = Control<ListBox>(window, "ChangedFiles").SelectedItem;
            Draft(window, 10, WhitespaceMode.IgnoreTrailingSpace); Click(window, "CancelDiffOptionsButton");
            inspector.Requests.Count.Test().Be(1);
            Control<Control>(window, "WhitespaceNotice").IsVisible.Test().BeFalse();
            Click(window, "DiffOptionsButton");
            Control<NumericUpDown>(window, "ContextLinesInput").Value.Test().Be(3m);
            Click(window, "ApplyDiffOptionsButton"); inspector.Requests.Count.Test().Be(1);
            Draft(window, 10, WhitespaceMode.IgnoreTrailingSpace);
            Click(window, "ApplyDiffOptionsButton"); await window.PendingInspection;
            inspector.Requests.Last().Options.Test().Be(new DiffOptions(10, WhitespaceMode.IgnoreTrailingSpace));
            Control<ListBox>(window, "ChangedFiles").SelectedItem.Test().Be(selection);
            Control<Control>(window, "WhitespaceNotice").IsVisible.Test().BeTrue();
            Click(window, "DiffOptionsButton"); Click(window, "ResetDiffOptionsButton");
            inspector.Requests.Count.Test().Be(2);
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Control<Control>(window, "DiffOptionsPanel").IsVisible.Test().BeFalse();
            Control<Control>(window, "WhitespaceNotice").IsVisible.Test().BeTrue();
            Click(window, "DiffOptionsButton"); Click(window, "ResetDiffOptionsButton"); Click(window, "ApplyDiffOptionsButton");
            await window.PendingInspection;
            inspector.Requests.Last().Options.Test().Be(DiffOptions.Default);
            Control<Control>(window, "WhitespaceNotice").IsVisible.Test().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task FilteredEmptyDiffHasAnExplicitWarningAndCanReturnToExact()
    {
        var inspector = new Inspector(); var window = Window(inspector);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root("treesi"));
            Draft(window, 0, WhitespaceMode.IgnoreAllSpace); Click(window, "ApplyDiffOptionsButton"); await window.PendingInspection;
            Control<TextBlock>(window, "EmptyTitle").Text.Test().Be("No differences for these options.");
            Control<TextBlock>(window, "EmptyDescription").Text!.Contains("Return to Exact", StringComparison.Ordinal).Test().BeTrue();
            Control<Control>(window, "WhitespaceNotice").IsVisible.Test().BeTrue();
            Control<TextBox>(window, "DiffSearch").IsEnabled.Test().BeFalse();
            SaveFrame(window, "filtered-empty-dark.png");
            Click(window, "DiffOptionsButton"); Click(window, "ResetDiffOptionsButton"); Click(window, "ApplyDiffOptionsButton");
            await window.PendingInspection;
            Control<TextEditor>(window, "DiffEditor").IsVisible.Test().BeTrue();
            Control<Control>(window, "WhitespaceNotice").IsVisible.Test().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task CompactOptionsRemainAccessibleAndNeverShrinkThePrimaryCanvas()
    {
        var inspector = new Inspector(); var window = Window(inspector);
        window.Width = 900; window.Height = 560;
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root("treesi"));
            foreach (var theme in new[] { ThemeCatalog.Dark, ThemeCatalog.Light })
            {
                Control<ComboBox>(window, "ThemeSelector").SelectedItem = theme;
                Draft(window, 10, WhitespaceMode.IgnoreTrailingSpace);
                SaveFrame(window, $"options-{theme.Id}-compact.png");
                foreach (var name in new[] { "ContextLinesInput", "WhitespaceSelector", "ApplyDiffOptionsButton", "CancelDiffOptionsButton", "ResetDiffOptionsButton" })
                    AssertInside(window, Control<Control>(window, name));
                Control<NumericUpDown>(window, "ContextLinesInput").IsKeyboardFocusWithin.Test().BeTrue();
                Control<NumericUpDown>(window, "ContextLinesInput").Value = null;
                Click(window, "ApplyDiffOptionsButton");
                Control<Control>(window, "DiffOptionsError").IsVisible.Test().BeTrue();
                Control<Control>(window, "DiffOptionsPanel").IsVisible.Test().BeTrue();
                inspector.Requests.Count.Test().Be(theme == ThemeCatalog.Dark ? 1 : 2);
                Control<NumericUpDown>(window, "ContextLinesInput").Value = 3.5m;
                Click(window, "ApplyDiffOptionsButton");
                Control<Control>(window, "DiffOptionsError").IsVisible.Test().BeTrue();
                inspector.Requests.Count.Test().Be(theme == ThemeCatalog.Dark ? 1 : 2);
                Control<NumericUpDown>(window, "ContextLinesInput").Value = 10;
                Click(window, "ApplyDiffOptionsButton"); await window.PendingInspection;
                foreach (var mode in new[] { 0, 1 })
                {
                    Control<TabStrip>(window, "DiffModeSelector").SelectedIndex = mode;
                    SaveFrame(window, $"filtered-{theme.Id}-{mode}-compact.png");
                    AssertInside(window, Control<Control>(window, "DiffOptionsButton"));
                    AssertInside(window, Control<Control>(window, "WhitespaceNotice"));
                    var canvas = Control<Control>(window, "DiffSurface"); var body = Control<Control>(window, "WorkspaceColumns");
                    (canvas.Bounds.Height / body.Bounds.Height >= 0.80).Test().BeTrue();
                    (canvas.Bounds.Width * canvas.Bounds.Height / (body.Bounds.Width * body.Bounds.Height) >= 0.60).Test().BeTrue();
                }
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task SupersededOptionsCannotReplaceANewerResultAndChangingRootsRestoresExact()
    {
        var inspector = new Inspector(); var window = Window(inspector);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root("treesi"));
            var delayed = new TaskCompletionSource<FileDiff>(); inspector.Delayed = delayed;
            Draft(window, 50, WhitespaceMode.Exact); Click(window, "ApplyDiffOptionsButton"); var old = window.PendingInspection;
            Draft(window, 0, WhitespaceMode.IgnoreTrailingSpace); Click(window, "ApplyDiffOptionsButton"); await window.PendingInspection;
            delayed.SetResult(new FileDiff("src/ChangeSummary.cs", Comparison.WorkingTree, "stale request", false, [], Options: new(50)));
            await old;
            Control<TextEditor>(window, "DiffEditor").Text!.Contains("stale request", StringComparison.Ordinal).Test().BeFalse();
            Control<ProgressBar>(window, "BusyIndicator").IsVisible.Test().BeFalse();
            await window.OpenRepositoryAsync(Root("another-repository"));
            inspector.Requests.Last().Options.Test().Be(DiffOptions.Default);
            Control<Control>(window, "WhitespaceNotice").IsVisible.Test().BeFalse();
            Click(window, "DiffOptionsButton");
            Control<NumericUpDown>(window, "ContextLinesInput").Value.Test().Be(3m);
            Control<ComboBox>(window, "WhitespaceSelector").SelectedIndex.Test().Be(0);
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
        Control<CheckBox>(window, "ReduceMotionToggle").IsChecked = true;
        using var frame = window.CaptureRenderedFrame(); frame.Test().NotBeNull();
        if (Environment.GetEnvironmentVariable("TREESI_RENDER_OUTPUT") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory); frame!.Save(Path.Combine(directory, name), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    private sealed class Preferences : IThemePreferenceStore
    {
        public string? Load() => "dark";
        public bool Save(string themeId) => true;
    }

    private sealed class Inspector : IInspectionService
    {
        public List<(string Root, DiffOptions Options)> Requests { get; } = [];
        public TaskCompletionSource<FileDiff>? Delayed { get; set; }
        public Task<RepositorySnapshot> InspectAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(new RepositorySnapshot(path, "src/ChangeSummary.cs", [new("src/ChangeSummary.cs", null, ".", "M")]));
        public Task<FileDiff> DiffAsync(string repository, string path, Comparison comparison, CancellationToken cancellationToken = default, DiffOptions? options = null)
        {
            options ??= DiffOptions.Default; Requests.Add((repository, options));
            if (options.ContextLines == 50 && Delayed is not null) return Delayed.Task;
            if (options.Whitespace is WhitespaceMode.IgnoreAllSpace or WhitespaceMode.IgnoreSpaceChange)
                return Task.FromResult(new FileDiff(path, comparison, "", false, [], Options: options));
            var lines = new DiffLine[] { new(DiffLineKind.Context, 1, 1, "namespace Treesi;"), new(DiffLineKind.Context, 2, 2, ""),
                new(DiffLineKind.Context, 3, 3, "public static class ChangeSummary"), new(DiffLineKind.Context, 4, 4, "{"),
                new(DiffLineKind.Context, 5, 5, "    public static int Count(int value)"), new(DiffLineKind.Context, 6, 6, "    {"),
                new(DiffLineKind.Removed, 7, null, "        return value; "), new(DiffLineKind.Added, null, 7, "        return  value;"),
                new(DiffLineKind.Context, 8, 8, "    }"), new(DiffLineKind.Context, 9, 9, "}") };
            const string patch = "diff --git a/src/ChangeSummary.cs b/src/ChangeSummary.cs\n--- a/src/ChangeSummary.cs\n+++ b/src/ChangeSummary.cs\n@@ -1,9 +1,9 @@\n namespace Treesi;\n \n public static class ChangeSummary\n {\n     public static int Count(int value)\n     {\n-        return value; \n+        return  value;\n     }\n }\n";
            return Task.FromResult(new FileDiff(path, comparison, patch, false, [new("@@ -1,9 +1,9 @@", lines)], Options: options));
        }
    }
}
