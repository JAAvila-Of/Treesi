using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using AvaloniaEdit;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Application;
using Treesi.Desktop.Appearance;
using Treesi.Domain;

namespace Treesi.Desktop.Tests;

[TestFixture]
public sealed class AppearanceTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "treesi-headless-fixture");
    private const string Patch = "diff --git a/src/ChangeSummary.cs b/src/ChangeSummary.cs\nindex 20f9d32..fe89263 100644\n--- a/src/ChangeSummary.cs\n+++ b/src/ChangeSummary.cs\n@@ -1,7 +1,10 @@\n namespace Treesi;\n \n public static class ChangeSummary\n {\n-    public static string Describe(int files) => $\"Changes: {files}\";\n+    public static string Describe(int files)\n+    {\n+        ArgumentOutOfRangeException.ThrowIfNegative(files);\n+        return files == 1 ? \"1 changed file\" : $\"{files} changed files\";\n+    }\n }\n";

    [AvaloniaTest]
    public async Task SwitchingThemesPreservesDiffSelectionAndDoesNotInspectAgain()
    {
        var reader = new FixtureInspector(); var preferences = new MemoryPreferences();
        var themes = new ThemeController(Avalonia.Application.Current!, preferences);
        var window = new MainWindow(reader, null, themes);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root);
            var editor = window.FindControl<TextEditor>("DiffEditor")!;
            editor.Select(0, 12);
            var calls = reader.DiffCalls;
            var selector = window.FindControl<ComboBox>("ThemeSelector")!;
            selector.SelectedItem = ThemeCatalog.Light;
            Dispatcher.UIThread.RunJobs();
            themes.Current.Id.Test().Be("light"); preferences.Value.Test().Be("light");
            editor.Text.Test().Be(Patch); editor.SelectedText.Test().Be(Patch[..12]);
            reader.DiffCalls.Test().Be(calls);
            SaveFrame(window, "workspace-light.png");
            AssertContrast(window);
            AssertRenderedAddition(window, editor);
            selector.SelectedItem = ThemeCatalog.Dark;
            Dispatcher.UIThread.RunJobs();
            themes.Current.Id.Test().Be("dark"); preferences.Value.Test().Be("dark");
            editor.Text.Test().Be(Patch); editor.SelectedText.Test().Be(Patch[..12]);
            reader.DiffCalls.Test().Be(calls);
            SaveFrame(window, "workspace-dark.png");
            AssertContrast(window);
            AssertRenderedAddition(window, editor);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task CompactLayoutKeepsPrimaryActionsInsideTheWindow()
    {
        var themes = new ThemeController(Avalonia.Application.Current!, new MemoryPreferences());
        var window = new MainWindow(new FixtureInspector(), null, themes) { Width = 900, Height = 560 };
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root); Dispatcher.UIThread.RunJobs();
            foreach (var name in new[] { "OpenFolderButton", "OpenFileButton", "ThemeSelector", "ComparisonSelector", "DiffSearch", "FindNextButton" })
            {
                var control = window.FindControl<Control>(name)!;
                var position = control.TranslatePoint(default, window)!.Value;
                (position.X >= 0 && position.Y >= 0).Test().BeTrue();
                (position.X + control.Bounds.Width <= window.ClientSize.Width + 1).Test().BeTrue();
                (position.Y + control.Bounds.Height <= window.ClientSize.Height + 1).Test().BeTrue();
            }
            SaveFrame(window, "workspace-dark-compact.png");
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void EmptyStateRendersInBothThemesWithoutNativeWindows()
    {
        var themes = new ThemeController(Avalonia.Application.Current!, new MemoryPreferences());
        var window = new MainWindow(new FixtureInspector(), null, themes);
        try
        {
            window.Show();
            window.FindControl<Control>("EmptyState")!.IsVisible.Test().BeTrue();
            window.FindControl<TextEditor>("DiffEditor")!.IsVisible.Test().BeFalse();
            SaveFrame(window, "empty-dark.png");
            themes.Apply(ThemeCatalog.Light).Test().BeTrue();
            Dispatcher.UIThread.RunJobs();
            SaveFrame(window, "empty-light.png");
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task FilterAndSearchStillWorkAfterAThemeChange()
    {
        var themes = new ThemeController(Avalonia.Application.Current!, new MemoryPreferences());
        var window = new MainWindow(new FixtureInspector(), null, themes);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root);
            themes.Apply(ThemeCatalog.Light);
            var search = window.FindControl<TextBox>("DiffSearch")!;
            search.Text = "ThrowIfNegative";
            window.FindControl<Button>("FindNextButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.FindControl<TextEditor>("DiffEditor")!.SelectedText.Test().Be("ThrowIfNegative");
            window.FindControl<TextBox>("FileFilter")!.Text = "missing";
            await window.PendingInspection;
            window.FindControl<Control>("EmptyState")!.IsVisible.Test().BeTrue();
            window.FindControl<TextEditor>("DiffEditor")!.IsVisible.Test().BeFalse();
            window.FindControl<TextBox>("FileFilter")!.Text = "";
            await window.PendingInspection;
            window.FindControl<TextEditor>("DiffEditor")!.Text.Test().Be(Patch);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task ComparisonChangeUpdatesDiffInEitherTheme()
    {
        var themes = new ThemeController(Avalonia.Application.Current!, new MemoryPreferences());
        var window = new MainWindow(new FixtureInspector(), null, themes);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root);
            themes.Apply(ThemeCatalog.Light);
            window.FindControl<ComboBox>("ComparisonSelector")!.SelectedIndex = 1;
            await window.PendingInspection;
            window.FindControl<TextBlock>("EmptyTitle")!.Text.Test().Be("No differences here.");
            themes.Apply(ThemeCatalog.Dark);
            window.FindControl<ComboBox>("ComparisonSelector")!.SelectedIndex = 0;
            await window.PendingInspection;
            window.FindControl<TextEditor>("DiffEditor")!.Text.Test().Be(Patch);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void UnwritablePreferencesDoNotPreventThemeChanges()
    {
        var preferences = new MemoryPreferences { SaveSucceeds = false };
        var themes = new ThemeController(Avalonia.Application.Current!, preferences);
        var window = new MainWindow(new FixtureInspector(), null, themes);
        try
        {
            window.Show(); window.FindControl<ComboBox>("ThemeSelector")!.SelectedItem = ThemeCatalog.Light;
            themes.Current.Id.Test().Be("light");
            window.FindControl<TextBlock>("OperationStatus")!.Text.Test().Contain("could not be saved");
        }
        finally { window.Close(); }
    }

    [Test]
    public void PreferenceStoreRoundTripsAndIgnoresCorruptData()
    {
        var directory = Path.Combine(Path.GetTempPath(), "treesi-appearance-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "appearance.json");
        var store = new JsonThemePreferenceStore(path);
        try
        {
            store.Load().Test().BeNull(); store.Save("light").Test().BeTrue(); store.Load().Test().Be("light");
            File.WriteAllText(path, "not json"); store.Load().Test().BeNull();
            ThemeCatalog.Resolve("unknown-theme").Id.Test().Be("dark");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    private static void SaveFrame(Window window, string name)
    {
        using var frame = window.CaptureRenderedFrame();
        frame.Test().NotBeNull();
        frame!.PixelSize.Width.Test().BePositive(); frame.PixelSize.Height.Test().BePositive();
        if (Environment.GetEnvironmentVariable("TREESI_RENDER_OUTPUT") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            frame.Save(Path.Combine(directory, name), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
    }

    private static void AssertContrast(Window window)
    {
        foreach (var pair in new[]
        {
            ("Text.Primary", "Surface.Panel"), ("Text.Secondary", "Surface.Sidebar"), ("Text.Subtle", "Diff.Gutter"),
            ("Accent.Contrast", "Accent.Primary"), ("Text.Primary", "Accent.Soft"), ("Diff.AddText", "Diff.AddBackground"),
            ("Diff.RemoveText", "Diff.RemoveBackground"), ("Diff.HunkText", "Diff.HunkBackground")
        })
        {
            var foreground = ((ISolidColorBrush)window.FindResource(window.ActualThemeVariant, pair.Item1)!).Color;
            var background = ((ISolidColorBrush)window.FindResource(window.ActualThemeVariant, pair.Item2)!).Color;
            var first = Luminance(foreground); var second = Luminance(background);
            ((Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05)).Test().BeGreaterThanOrEqualTo(4.5);
        }
    }

    private static void AssertRenderedAddition(Window window, TextEditor editor)
    {
        using var frame = window.CaptureRenderedFrame();
        var view = editor.TextArea.TextView;
        view.VisualLinesValid.Test().BeTrue();
        var added = view.VisualLines.First(line => view.Document.GetText(line.FirstDocumentLine).StartsWith("+    public static", StringComparison.Ordinal));
        var position = view.TranslatePoint(new Point(view.Bounds.Width - 12, added.VisualTop - view.VerticalOffset + added.Height / 2), window)!.Value;
        using var pixels = frame!.Lock();
        (pixels.Format == PixelFormat.Rgba8888 || pixels.Format == PixelFormat.Bgra8888).Test().BeTrue();
        var x = (int)(position.X * frame.PixelSize.Width / window.ClientSize.Width);
        var y = (int)(position.Y * frame.PixelSize.Height / window.ClientSize.Height);
        var address = pixels.Address + y * pixels.RowBytes + x * 4;
        var first = System.Runtime.InteropServices.Marshal.ReadByte(address);
        var green = System.Runtime.InteropServices.Marshal.ReadByte(address + 1);
        var third = System.Runtime.InteropServices.Marshal.ReadByte(address + 2);
        var actual = pixels.Format == PixelFormat.Rgba8888 ? Color.FromRgb(first, green, third) : Color.FromRgb(third, green, first);
        var expected = ((ISolidColorBrush)window.FindResource(window.ActualThemeVariant, "Diff.AddBackground")!).Color;
        actual.ToString().Test().Be(expected.ToString());
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value) { var s = value / 255d; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    private sealed class MemoryPreferences : IThemePreferenceStore
    {
        public string? Value { get; private set; }
        public bool SaveSucceeds { get; init; } = true;
        public string? Load() => Value;
        public bool Save(string themeId) { if (!SaveSucceeds) return false; Value = themeId; return true; }
    }

    private sealed class FixtureInspector : IInspectionService
    {
        public int DiffCalls { get; private set; }
        public Task<RepositorySnapshot> InspectAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RepositorySnapshot(Root, "src/ChangeSummary.cs", [
                new FileChange("src/ChangeSummary.cs", null, ".", "M"), new FileChange("docs/README.md", null, "?", "?") ]));
        public Task<FileDiff> DiffAsync(string repository, string path, Comparison comparison, CancellationToken cancellationToken = default)
        {
            DiffCalls++;
            if (comparison == Comparison.Staged) return Task.FromResult(new FileDiff(path, comparison, "", false, []));
            var lines = new[]
            {
                new DiffLine(DiffLineKind.Context, 1, 1, "namespace Treesi;"), new DiffLine(DiffLineKind.Context, 2, 2, ""),
                new DiffLine(DiffLineKind.Context, 3, 3, "public static class ChangeSummary"), new DiffLine(DiffLineKind.Context, 4, 4, "{"),
                new DiffLine(DiffLineKind.Removed, 5, null, "    public static string Describe(int files) => $\"Changes: {files}\";"),
                new DiffLine(DiffLineKind.Added, null, 5, "    public static string Describe(int files)"),
                new DiffLine(DiffLineKind.Added, null, 6, "    {"), new DiffLine(DiffLineKind.Added, null, 7, "        ArgumentOutOfRangeException.ThrowIfNegative(files);"),
                new DiffLine(DiffLineKind.Added, null, 8, "        return files == 1 ? \"1 changed file\" : $\"{files} changed files\";"),
                new DiffLine(DiffLineKind.Added, null, 9, "    }"), new DiffLine(DiffLineKind.Context, 6, 10, "}")
            };
            return Task.FromResult(new FileDiff(path, comparison, Patch, false, [new DiffHunk("@@ -1,7 +1,10 @@", lines)]));
        }
    }
}
