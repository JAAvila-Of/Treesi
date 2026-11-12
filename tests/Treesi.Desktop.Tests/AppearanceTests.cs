using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
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
    private static readonly string Root = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "workspace", "treesi");
    private const string Patch = "diff --git a/src/ChangeSummary.cs b/src/ChangeSummary.cs\nindex 20f9d32..fe89263 100644\n--- a/src/ChangeSummary.cs\n+++ b/src/ChangeSummary.cs\n@@ -1,7 +1,10 @@\n namespace Treesi;\n \n public static class ChangeSummary\n {\n-    public static string Describe(int files) => $\"Changes: {files}\";\n+    public static string Describe(int files)\n+    {\n+        ArgumentOutOfRangeException.ThrowIfNegative(files);\n+        return files == 1 ? \"1 changed file\" : $\"{files} changed files\";\n+    }\n }\n";

    [AvaloniaTest]
    public async Task CodeCanvasDominatesBothLayoutsAndThemesAtCompactAndWideSizes()
    {
        var themes = new ThemeController(Avalonia.Application.Current!, new MemoryPreferences());
        var window = new MainWindow(new FixtureInspector(), null, themes);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root);
            window.FindControl<CheckBox>("ReduceMotionToggle")!.IsChecked = true;
            window.FindControl<TextBlock>("FileTitle")!.Text = new string('x', 240) + ".cs";
            window.FindControl<TextBlock>("ComparisonDescription")!.Text = string.Concat(Enumerable.Repeat("nested/directory/", 40));
            foreach (var size in new[] { new Size(900, 560), new Size(1180, 740) })
                foreach (var theme in new[] { ThemeCatalog.Dark, ThemeCatalog.Light })
                    foreach (var mode in new[] { 0, 1 })
                    {
                        window.Width = size.Width; window.Height = size.Height;
                        themes.Apply(theme);
                        window.FindControl<TabStrip>("DiffModeSelector")!.SelectedIndex = mode;
                        using var frame = window.CaptureRenderedFrame();
                        var canvas = window.FindControl<Control>("DiffSurface")!;
                        var body = window.FindControl<Grid>("WorkspaceColumns")!;
                        (canvas.Bounds.Height / body.Bounds.Height >= 0.80).Test().BeTrue();
                        (canvas.Bounds.Width * canvas.Bounds.Height / (body.Bounds.Width * body.Bounds.Height) >= 0.60).Test().BeTrue();
                        (canvas.Bounds.Height >= 370).Test().BeTrue();
                        foreach (var name in new[] { "ComparisonSelector", "DiffModeSelector", "ToggleFilesButton", "BlockPosition", "PreviousBlockButton", "NextBlockButton", "DiffSearch", "FindNextButton", "FocusWorkspaceButton" })
                        {
                            var control = window.FindControl<Control>(name)!;
                            var position = control.TranslatePoint(default, window)!.Value;
                            (position.X >= 0 && position.X + control.Bounds.Width <= window.ClientSize.Width + 1).Test().BeTrue();
                            (position.Y >= 0 && position.Y + control.Bounds.Height <= window.ClientSize.Height + 1).Test().BeTrue();
                        }
                    }
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task FocusAndPanelShortcutsPreserveSelectionAndReturnFocusToVisibleContent()
    {
        var reader = new FixtureInspector();
        var themes = new ThemeController(Avalonia.Application.Current!, new MemoryPreferences());
        var window = new MainWindow(reader, null, themes) { Width = 900, Height = 560 };
        void Key(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root);
            var list = window.FindControl<ListBox>("ChangedFiles")!;
            var filter = window.FindControl<TextBox>("FileFilter")!;
            filter.Text = "ChangeSummary";
            await window.PendingInspection;
            var calls = reader.DiffCalls;
            var selected = list.SelectedItem;
            var editor = window.FindControl<TextEditor>("DiffEditor")!;
            editor.Select(editor.Text.IndexOf("ThrowIfNegative", StringComparison.Ordinal), "ThrowIfNegative".Length);
            var search = window.FindControl<TextBox>("DiffSearch")!;
            search.Text = "ThrowIfNegative";
            using var initialFrame = window.CaptureRenderedFrame();
            filter.Focus().Test().BeTrue();
            Key(Avalonia.Input.Key.B, KeyModifiers.Control);
            window.FindControl<Control>("FilesPanel")!.IsVisible.Test().BeFalse();
            editor.IsKeyboardFocusWithin.Test().BeTrue();
            SaveFrame(window, "workspace-files-hidden.png");
            Key(Avalonia.Input.Key.F, KeyModifiers.Control | KeyModifiers.Shift);
            window.FindControl<Control>("WorkspaceHeader")!.IsVisible.Test().BeFalse();
            SaveFrame(window, "workspace-focus-compact.png");
            Key(Avalonia.Input.Key.F, KeyModifiers.Control);
            search.IsFocused.Test().BeTrue();
            Key(Avalonia.Input.Key.Escape);
            window.FindControl<Control>("WorkspaceHeader")!.IsVisible.Test().BeTrue();
            window.FindControl<Control>("FilesPanel")!.IsVisible.Test().BeFalse();
            window.FindControl<Button>("ToggleFilesButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.FindControl<Control>("FilesPanel")!.IsVisible.Test().BeTrue();
            window.FindControl<Button>("FocusWorkspaceButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Key(Avalonia.Input.Key.Escape);
            window.FindControl<Control>("FilesPanel")!.IsVisible.Test().BeTrue();
            list.SelectedItem.Test().Be(selected);
            filter.Text.Test().Be("ChangeSummary"); search.Text.Test().Be("ThrowIfNegative");
            editor.SelectedText.Test().Be("ThrowIfNegative"); reader.DiffCalls.Test().Be(calls);
            filter.Text = "missing";
            await window.PendingInspection;
            filter.Focus().Test().BeTrue();
            Key(Avalonia.Input.Key.B, KeyModifiers.Control);
            window.FindControl<Button>("ToggleFilesButton")!.IsFocused.Test().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task MotionPreferenceAndWorkspaceActionsPreserveTheLoadedDiff()
    {
        var reader = new FixtureInspector(); var preferences = new MotionPreferences();
        var themes = new ThemeController(Avalonia.Application.Current!, new MemoryPreferences());
        var window = new MainWindow(reader, null, themes, preferences);
        var directory = Environment.GetEnvironmentVariable("TREESI_MOTION_OUTPUT");
        var frameNumber = 0;
        var captures = new List<(string Name, double Seconds)>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        async Task Settle()
        {
            if (directory is not { Length: > 0 }) { await window.PendingMotion.WaitAsync(TimeSpan.FromSeconds(5)); return; }
            Directory.CreateDirectory(directory);
            for (var frameIndex = 0; frameIndex < 24; frameIndex++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.GetLastRenderedFrame();
                var name = $"frame-{frameNumber++:D4}.png";
                captures.Add((name, clock.Elapsed.TotalSeconds));
                frame!.Save(Path.Combine(directory, name), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                await Task.Delay(33);
            }
        }
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root); await Settle();
            window.FindControl<ProgressBar>("BusyIndicator")!.IsVisible.Test().BeFalse();
            var calls = reader.DiffCalls;
            var button = window.FindControl<Button>("FindNextButton")!;
            var point = button.TranslatePoint(new Point(12, 12), window)!.Value;
            window.MouseMove(point); await Settle();
            window.MouseMove(new Point(800, 64));
            window.FindControl<TabStrip>("DiffModeSelector")!.SelectedIndex = 1; await Settle();
            window.FindControl<TextBox>("DiffSearch")!.Text = "ThrowIfNegative";
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Settle();
            themes.Apply(ThemeCatalog.Light); await Settle();
            window.FindControl<CheckBox>("ReduceMotionToggle")!.IsChecked = true; await Settle();
            preferences.Value.Test().BeTrue(); window.Classes.Contains("motion-enabled").Test().BeFalse();
            window.FindControl<TabStrip>("DiffModeSelector")!.SelectedIndex = 0; await Settle();
            if (directory is { Length: > 0 })
            {
                var timeline = new List<string> { "ffconcat version 1.0" };
                for (var i = 0; i < captures.Count; i++)
                {
                    timeline.Add($"file '{captures[i].Name}'");
                    var seconds = i + 1 < captures.Count ? captures[i + 1].Seconds - captures[i].Seconds : 0.033;
                    timeline.Add("duration " + seconds.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
                }
                timeline.Add($"file '{captures[^1].Name}'");
                File.WriteAllLines(Path.Combine(directory, "frames.ffconcat"), timeline);
            }
            window.FindControl<TextEditor>("DiffEditor")!.Text.Test().Be(Patch);
            window.PendingMotion.IsCompletedSuccessfully.Test().BeTrue();
            window.Width = 900; window.FindControl<TabStrip>("DiffModeSelector")!.SelectedIndex = 1;
            using var compact = window.CaptureRenderedFrame();
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var editor = window.FindControl<TextEditor>("AfterEditor")!;
            (editor.HorizontalOffset > 0).Test().BeTrue();
            editor.SelectedText.Test().Be("ThrowIfNegative");
            reader.DiffCalls.Test().Be(calls);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void MotionPreferenceLoadsAndSaveFailuresKeepTheSessionUsable()
    {
        var preferences = new MotionPreferences(true) { SaveSucceeds = false };
        var window = new MainWindow(new FixtureInspector(), null,
            new ThemeController(Avalonia.Application.Current!, new MemoryPreferences()), preferences);
        try
        {
            window.Show();
            var toggle = window.FindControl<CheckBox>("ReduceMotionToggle")!;
            (toggle.IsChecked == true).Test().BeTrue();
            window.Classes.Contains("motion-enabled").Test().BeFalse();
            toggle.IsChecked = false;
            window.Classes.Contains("motion-enabled").Test().BeTrue();
            window.FindControl<TextBlock>("OperationStatus")!.Text.Test().Contain("could not be saved");
            preferences.Value.Test().BeTrue();
        }
        finally { window.Close(); }
    }

    private sealed class MotionPreferences(bool initial = false) : IMotionPreferenceStore
    {
        public bool Value { get; private set; } = initial;
        public bool SaveSucceeds { get; init; } = true;
        public bool Load() => Value;
        public bool Save(bool reduced) { if (SaveSucceeds) Value = reduced; return SaveSucceeds; }
    }

    [AvaloniaTest]
    public async Task SegmentedLayoutRespondsToHeadlessPointerInput()
    {
        var window = new MainWindow(new FixtureInspector(), null,
            new ThemeController(Avalonia.Application.Current!, new MemoryPreferences()), new MotionPreferences(true));
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root);
            using var frame = window.CaptureRenderedFrame();
            var split = window.FindControl<TabStripItem>("SplitModeButton")!;
            var point = split.TranslatePoint(new Point(split.Bounds.Width / 2, split.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            window.FindControl<TabStrip>("DiffModeSelector")!.SelectedIndex.Test().Be(1);
            window.FindControl<Control>("SplitView")!.IsVisible.Test().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task SplitModePreservesLoadedDataAndRendersBothThemes()
    {
        var reader = new FixtureInspector();
        var themes = new ThemeController(Avalonia.Application.Current!, new MemoryPreferences());
        var window = new MainWindow(reader, null, themes);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root);
            var calls = reader.DiffCalls;
            window.FindControl<TabStrip>("DiffModeSelector")!.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            var before = window.FindControl<TextEditor>("BeforeEditor")!;
            var after = window.FindControl<TextEditor>("AfterEditor")!;
            before.Text.Contains("Changes: {files}", StringComparison.Ordinal).Test().BeTrue();
            before.Text.Contains("ThrowIfNegative", StringComparison.Ordinal).Test().BeFalse();
            after.Text.Contains("ThrowIfNegative", StringComparison.Ordinal).Test().BeTrue();
            before.Document.LineCount.Test().Be(after.Document.LineCount);
            window.FindControl<TextEditor>("DiffEditor")!.IsVisible.Test().BeFalse();
            SaveFrame(window, "split-dark.png");
            themes.Apply(ThemeCatalog.Light); Dispatcher.UIThread.RunJobs();
            SaveFrame(window, "split-light.png");
            window.Width = 900; window.Height = 560; Dispatcher.UIThread.RunJobs();
            foreach (var name in new[] { "DiffModeSelector", "PreviousBlockButton", "NextBlockButton", "BeforeEditor", "AfterEditor" })
            {
                var control = window.FindControl<Control>(name)!;
                var position = control.TranslatePoint(default, window)!.Value;
                (position.X >= 0 && position.X + control.Bounds.Width <= window.ClientSize.Width + 1).Test().BeTrue();
                (position.Y >= 0 && position.Y + control.Bounds.Height <= window.ClientSize.Height + 1).Test().BeTrue();
            }
            SaveFrame(window, "split-light-compact.png");
            window.FindControl<TabStrip>("DiffModeSelector")!.SelectedIndex = 0;
            window.FindControl<TextEditor>("DiffEditor")!.Text.Test().Be(Patch);
            reader.DiffCalls.Test().Be(calls);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task SplitSearchFindsRemovedTextAndEmptyComparisonsClearBothPanels()
    {
        var themes = new ThemeController(Avalonia.Application.Current!, new MemoryPreferences());
        var window = new MainWindow(new FixtureInspector(), null, themes);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root);
            window.FindControl<TabStrip>("DiffModeSelector")!.SelectedIndex = 1;
            window.FindControl<TextBox>("DiffSearch")!.Text = "Changes: {files}";
            window.FindControl<Button>("FindNextButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.FindControl<TextEditor>("BeforeEditor")!.SelectedText.Test().Be("Changes: {files}");
            window.FindControl<ComboBox>("ComparisonSelector")!.SelectedIndex = 1;
            await window.PendingInspection;
            window.FindControl<Control>("SplitView")!.IsVisible.Test().BeFalse();
            window.FindControl<TextEditor>("BeforeEditor")!.Text.Test().Be("");
            window.FindControl<TextEditor>("AfterEditor")!.Text.Test().Be("");
            window.FindControl<Button>("NextBlockButton")!.IsEnabled.Test().BeFalse();
            window.FindControl<TabStrip>("DiffModeSelector")!.IsEnabled.Test().BeFalse();
        }
        finally { window.Close(); }
    }

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
    public void EmptyHomeRendersInBothThemesWithoutNativeWindows()
    {
        var themes = new ThemeController(Avalonia.Application.Current!, new MemoryPreferences());
        var window = new MainWindow(new FixtureInspector(), null, themes);
        try
        {
            window.Show();
            window.FindControl<Control>("HomeSurface")!.IsVisible.Test().BeTrue();
            window.FindControl<Control>("NoRecentWorkspaces")!.IsVisible.Test().BeTrue();
            window.FindControl<TextEditor>("DiffEditor")!.IsVisible.Test().BeFalse();
            SaveFrame(window, "home-empty-dark.png");
            themes.Apply(ThemeCatalog.Light).Test().BeTrue();
            Dispatcher.UIThread.RunJobs();
            SaveFrame(window, "home-empty-light.png");
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
        window.FindControl<CheckBox>("ReduceMotionToggle")!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
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
