using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
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
public sealed class SourceWorkspaceTests
{
    private static readonly string Root = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "workspace", "source-example");
    private static T Get<T>(Window window, string name) where T : Control => window.FindControl<T>(name)!;
    private static void Click(Window window, string name) => Get<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaTest]
    [TestCase("dark", false)]
    [TestCase("light", false)]
    [TestCase("dark", true)]
    [TestCase("light", true)]
    public async Task CodeAndInlineRangesRenderWithAccessibleContrastInBothLayouts(string theme, bool split)
    {
        var inspector = new Inspector(); var appearance = new ThemeController(Avalonia.Application.Current!, new Preferences(theme));
        var window = new MainWindow(inspector, null, appearance) { Width = 900, Height = 560 };
        try
        {
            window.Show(); Get<CheckBox>(window, "ReduceMotionToggle").IsChecked = true; await window.OpenRepositoryAsync(Root);
            Get<TabStrip>(window, "DiffModeSelector").SelectedIndex = split ? 1 : 0;
            Dispatcher.UIThread.RunJobs();
            SaveFrame(window, $"source-{theme}-{(split ? "split" : "unified")}.png");
            var editor = Get<TextEditor>(window, split ? "AfterEditor" : "DiffEditor");
            var elements = editor.TextArea.TextView.VisualLines.SelectMany(line => line.Elements).ToArray();
            elements.Any(element => (element.TextRunProperties.ForegroundBrush as ISolidColorBrush)?.Color == Color(window, "Code.Keyword")).Test().BeTrue();
            elements.Any(element => (element.TextRunProperties.BackgroundBrush as ISolidColorBrush)?.Color == Color(window, "Diff.AddInline")).Test().BeTrue();
            (CountPixels(window, Color(window, "Diff.AddInline")) > 10).Test().BeTrue();
            foreach (var foreground in new[] { "Text.Primary", "Code.Keyword", "Code.String", "Code.Number", "Code.Comment" })
                foreach (var background in new[] { "Diff.Canvas", "Diff.AddBackground", "Diff.RemoveBackground", "Diff.AddInline", "Diff.RemoveInline" })
                    (Contrast(Color(window, foreground), Color(window, background)) >= 4.5).Test().BeTrue();
            var body = Get<Control>(window, "WorkspaceFrame").Bounds; var canvas = Get<Control>(window, "DiffSurface").Bounds;
            (canvas.Height >= body.Height * .80 && canvas.Width * canvas.Height >= body.Width * body.Height * .60).Test().BeTrue();
            var calls = inspector.Calls;
            appearance.Apply(theme == "light" ? ThemeCatalog.Dark : ThemeCatalog.Light); window.CaptureRenderedFrame()?.Dispose();
            inspector.Calls.Test().Be(calls);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task DisplayDraftAppliesWithoutGitReloadOrLosingSelectionAndCancellationDiscardsIt()
    {
        var inspector = new Inspector(); var window = new MainWindow(inspector, null, new ThemeController(Avalonia.Application.Current!, new Preferences("dark")));
        window.Width = 900; window.Height = 560;
        try
        {
            window.Show(); Get<CheckBox>(window, "ReduceMotionToggle").IsChecked = true; await window.OpenRepositoryAsync(Root);
            var editor = Get<TextEditor>(window, "DiffEditor"); editor.Select(editor.Text.IndexOf("InspectAsync", StringComparison.Ordinal), 12);
            Get<TextBox>(window, "DiffSearch").Text = "InspectAsync"; var selection = editor.SelectedText; var calls = inspector.Calls;
            Click(window, "DiffOptionsButton"); Get<CheckBox>(window, "CodeColorsToggle").IsChecked = false; Get<CheckBox>(window, "InlineChangesToggle").IsChecked = false;
            Click(window, "CancelDiffOptionsButton"); Click(window, "DiffOptionsButton");
            Get<CheckBox>(window, "CodeColorsToggle").IsChecked.Test().Be(true); Get<CheckBox>(window, "InlineChangesToggle").IsChecked.Test().Be(true);
            Get<CheckBox>(window, "CodeColorsToggle").IsChecked = false; Get<CheckBox>(window, "InlineChangesToggle").IsChecked = false;
            foreach (var name in new[] { "CodeColorsToggle", "InlineChangesToggle", "ApplyDiffOptionsButton", "CancelDiffOptionsButton" })
            {
                var control = Get<Control>(window, name); window.CaptureRenderedFrame()?.Dispose(); var point = control.TranslatePoint(default, window)!.Value;
                (point.Y >= 0 && point.Y + control.Bounds.Height <= window.ClientSize.Height).Test().BeTrue();
            }
            SaveFrame(window, "source-options-dark.png");
            Click(window, "ApplyDiffOptionsButton"); window.CaptureRenderedFrame()?.Dispose();
            inspector.Calls.Test().Be(calls); editor.SelectedText.Test().Be(selection); Get<TextBox>(window, "DiffSearch").Text.Test().Be("InspectAsync");
            CountPixels(window, Color(window, "Diff.AddInline")).Test().Be(0);
            Click(window, "DiffOptionsButton"); Click(window, "ResetDiffOptionsButton"); Click(window, "ApplyDiffOptionsButton");
            inspector.Calls.Test().Be(calls); (CountPixels(window, Color(window, "Diff.AddInline")) > 10).Test().BeTrue();
        }
        finally { window.Close(); }
    }

    private static Color Color(Window window, string key) => ((ISolidColorBrush)window.FindResource(window.ActualThemeVariant, key)!).Color;
    private static double Contrast(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Channel(byte channel) { var value = channel / 255.0; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
            return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        }
        var a = Luminance(first); var b = Luminance(second); return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    private static int CountPixels(Window window, Color expected)
    {
        using var frame = window.CaptureRenderedFrame(); using var pixels = frame!.Lock();
        var data = new byte[pixels.RowBytes * frame.PixelSize.Height]; System.Runtime.InteropServices.Marshal.Copy(pixels.Address, data, 0, data.Length);
        var rgba = pixels.Format == PixelFormat.Rgba8888; var count = 0;
        for (var y = 0; y < frame.PixelSize.Height; y++)
            for (var x = 0; x < frame.PixelSize.Width; x++)
            {
                var offset = y * pixels.RowBytes + x * 4;
                if (data[offset + (rgba ? 0 : 2)] == expected.R && data[offset + 1] == expected.G && data[offset + (rgba ? 2 : 0)] == expected.B) count++;
            }
        return count;
    }
    private static void SaveFrame(Window window, string name)
    {
        using var frame = window.CaptureRenderedFrame(); frame.Test().NotBeNull();
        if (Environment.GetEnvironmentVariable("TREESI_RENDER_OUTPUT") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory); frame!.Save(Path.Combine(directory, name), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
    private sealed class Preferences(string theme) : IThemePreferenceStore { public string? Load() => theme; public bool Save(string themeId) => true; }
    private sealed class Inspector : IInspectionService
    {
        public int Calls { get; private set; }
        public Task<RepositorySnapshot> InspectAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(new RepositorySnapshot(path, "RevisionInspector.cs", [new("RevisionInspector.cs", null, ".", "M")]));
        public Task<FileDiff> DiffAsync(string repository, string path, Comparison comparison, CancellationToken cancellationToken = default, DiffOptions? options = null)
        {
            Calls++; var lines = new List<DiffLine>(); var oldRow = 1; var newRow = 1;
            void Context(string text) => lines.Add(new(DiffLineKind.Context, oldRow++, newRow++, text));
            void Change(string before, string after) { lines.Add(new(DiffLineKind.Removed, oldRow++, null, before)); lines.Add(new(DiffLineKind.Added, null, newRow++, after)); }
            Context("using System.Threading.Tasks;"); Context(""); Context("namespace Treesi.Example;"); Context("");
            Context("public sealed class RevisionInspector"); Context("{"); Context("    // Read local changes without opening an editor.");
            Context("    public async Task<Snapshot> InspectAsync(string path)"); Context("    {");
            Change("        var context = 3;", "        var context = 7;");
            Change("        var mode = \"working\";", "        var mode = \"revisions\";");
            Context(""); Context("        return await reader.ReadAsync(path, context, mode);"); Context("    }"); Context("}");
            var header = $"@@ -1,{oldRow - 1} +1,{newRow - 1} @@";
            var patch = "diff --git a/RevisionInspector.cs b/RevisionInspector.cs\n--- a/RevisionInspector.cs\n+++ b/RevisionInspector.cs\n" + header + "\n" +
                string.Join("\n", lines.Select(line => (line.Kind == DiffLineKind.Added ? "+" : line.Kind == DiffLineKind.Removed ? "-" : " ") + line.Text)) + "\n";
            return Task.FromResult(new FileDiff(path, comparison, patch, false, [new(header, lines)], Options: options ?? DiffOptions.Default));
        }
    }
}
