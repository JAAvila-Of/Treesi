using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AvaloniaEdit;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Application;
using Treesi.Desktop.Appearance;
using Treesi.Domain;

namespace Treesi.Desktop.Tests;

[TestFixture]
public sealed class FileDropWorkspaceTests
{
    private static T Get<T>(MainWindow window, string name) where T : Control => window.FindControl<T>(name)!;
    private static Point Position(MainWindow window, string name)
    {
        using var frame = window.CaptureRenderedFrame();
        var control = Get<Control>(window, name);
        return control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    }
    private static void Send(MainWindow window, IDataTransfer data, RawDragEventType type, string target = "HomeSurface", DragDropEffects effects = DragDropEffects.Copy)
        => window.DragDrop(Position(window, target), type, data, effects);
    private static void Drop(MainWindow window, IDataTransfer data, string target = "HomeSurface", DragDropEffects effects = DragDropEffects.Copy)
    {
        Send(window, data, RawDragEventType.DragEnter, target, effects);
        Send(window, data, RawDragEventType.Drop, target, effects);
    }

    [AvaloniaTest, TestCase("dark"), TestCase("light")]
    public void HoverReadsNoPayloadOrRepositoryAndLeavesTheCanvasGeometryUnchanged(string theme)
    {
        using var fixture = new Fixture(); var window = fixture.Window(theme);
        try
        {
            window.Show(); var reads = 0;
            using var data = new DataTransfer(); var item = new DataTransferItem();
            item.Set(DataFormat.File, () => { reads++; throw new InvalidOperationException("Must not resolve payload while hovering."); }); data.Add(item);
            var bounds = Get<Control>(window, "HomeSurface").Bounds;
            Send(window, data, RawDragEventType.DragEnter);
            Get<Control>(window, "FileDropIndicator").IsVisible.Test().BeTrue();
            Send(window, data, RawDragEventType.DragOver);
            reads.Test().Be(0); fixture.Inspector.Paths.Count.Test().Be(0);
            Get<Control>(window, "HomeSurface").Bounds.Test().Be(bounds);
            using var frame = window.CaptureRenderedFrame(); frame.Test().NotBeNull();
            if (Environment.GetEnvironmentVariable("TREESI_RENDER_OUTPUT") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                frame!.Save(Path.Combine(directory, "file-drop-" + theme + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
            Send(window, data, RawDragEventType.DragLeave);
            Get<Control>(window, "FileDropIndicator").IsVisible.Test().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task FileAndFolderDropsUseTheExistingOpeningFlowAndDoNotInsertTextIntoSearch()
    {
        using var fixture = new Fixture(); var window = fixture.Window();
        try
        {
            window.Show(); using var folder = await fixture.Transfer(window, fixture.Root, folder: true);
            Drop(window, folder); await window.PendingInspection;
            fixture.Inspector.Paths.Single().Test().Be(fixture.Root);
            Get<TextBox>(window, "DiffSearch").Text = "existing search";
            var inserted = false;
            Get<TextBox>(window, "DiffSearch").AddHandler(Avalonia.Input.DragDrop.DropEvent, (_, _) => inserted = true);
            using var file = await fixture.Transfer(window, fixture.First);
            file.Items[0].Set(DataFormat.Text, "file://not-an-opening-command");
            Drop(window, file, "DiffSearch"); await window.PendingInspection;
            inserted.Test().BeFalse();
            fixture.Inspector.Paths.Last().Test().Be(fixture.First);
            Get<TextBlock>(window, "FileTitle").Text.Test().Be(Path.GetFileName(fixture.First));
            Get<Control>(window, "FileDropIndicator").IsVisible.Test().BeFalse();
            File.ReadAllText(fixture.First).Test().Be("original");
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task MultipleItemsTextAndMoveOnlyDoNotOpenRepositories()
    {
        using var fixture = new Fixture(); var window = fixture.Window();
        try
        {
            window.Show(); var reads = 0;
            using var multiple = new DataTransfer();
            for (var index = 0; index < 2; index++)
            {
                var item = new DataTransferItem();
                item.Set(DataFormat.File, () => { reads++; throw new InvalidOperationException(); }); multiple.Add(item);
            }
            Drop(window, multiple); reads.Test().Be(0);
            Get<TextBlock>(window, "HomeNotice").Text.Test().Be("Drop one file or folder at a time.");
            using var text = new DataTransfer(); text.Add(DataTransferItem.CreateText(fixture.First));
            Drop(window, text); fixture.Inspector.Paths.Count.Test().Be(0);
            using var file = await fixture.Transfer(window, fixture.First);
            Drop(window, file, effects: DragDropEffects.Move);
            fixture.Inspector.Paths.Count.Test().Be(0); File.Exists(fixture.First).Test().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task ProviderFailuresAndUnavailablePayloadsPreserveTheLoadedComparison()
    {
        using var fixture = new Fixture(); var window = fixture.Window();
        try
        {
            window.Show(); await window.OpenRepositoryAsync(fixture.First);
            var editor = Get<TextEditor>(window, "DiffEditor"); var previous = editor.Text;
            using var broken = new DataTransfer(); var item = new DataTransferItem();
            item.Set(DataFormat.File, () => throw new IOException("private provider details")); broken.Add(item);
            Drop(window, broken, "DiffEditor");
            fixture.Inspector.Paths.Count.Test().Be(1); editor.Text.Test().Be(previous);
            Get<TextBlock>(window, "OperationStatus").Text!.Contains("private", StringComparison.Ordinal).Test().BeFalse();
            using var missing = new DataTransfer(); var empty = new DataTransferItem();
            empty.Set(DataFormat.File, () => (IStorageItem?)null); missing.Add(empty);
            Drop(window, missing, "DiffEditor"); fixture.Inspector.Paths.Count.Test().Be(1);
            editor.Text.Test().Be(previous); Get<Control>(window, "FileDropIndicator").IsVisible.Test().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task DraftPanelsAreNotDiscardedAndInspectionFailureUsesRecovery()
    {
        using var fixture = new Fixture(); var window = fixture.Window();
        try
        {
            window.Show(); await window.OpenRepositoryAsync(fixture.First);
            using var data = await fixture.Transfer(window, fixture.Second);
            Get<Button>(window, "DiffOptionsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Get<TextBox>(window, "ContextLinesInput").Text = "12";
            Drop(window, data, "ContextLinesInput");
            fixture.Inspector.Paths.Count.Test().Be(1);
            Get<Control>(window, "DiffOptionsPanel").IsVisible.Test().BeTrue(); Get<TextBox>(window, "ContextLinesInput").Text.Test().Be("12");
            Get<Button>(window, "CancelDiffOptionsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var previous = Get<TextEditor>(window, "DiffEditor").Text;
            fixture.Inspector.Failure = new InspectionException("path_not_found", "The dropped item is unavailable.");
            Drop(window, data, "DiffEditor"); await window.PendingInspection;
            Get<TextEditor>(window, "DiffEditor").Text.Test().Be(previous);
            Get<Control>(window, "OpenRecoveryNotice").IsVisible.Test().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task ANewDropSupersedesAnUncooperativeOpening()
    {
        using var fixture = new Fixture(); var window = fixture.Window();
        try
        {
            window.Show(); fixture.Inspector.Delayed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using var first = await fixture.Transfer(window, fixture.First); Drop(window, first);
            var stale = window.PendingInspection; var pending = fixture.Inspector.Delayed; fixture.Inspector.Delayed = null;
            using var second = await fixture.Transfer(window, fixture.Second); Drop(window, second); await window.PendingInspection;
            pending.SetResult(fixture.Inspector.Snapshot(fixture.First)); await stale;
            fixture.Inspector.Paths.Count.Test().Be(2);
            Get<TextBlock>(window, "FileTitle").Text.Test().Be(Path.GetFileName(fixture.Second));
        }
        finally { window.Close(); }
    }

    private sealed class Themes(string theme) : IThemePreferenceStore
    { public string? Load() => theme; public bool Save(string themeId) => true; }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "treesi-drop-" + Guid.NewGuid().ToString("N"));
        public string First => Path.Combine(Root, "review # ñ.cs");
        public string Second => Path.Combine(Root, "another.cs");
        public Inspector Inspector { get; }
        public Fixture() { Directory.CreateDirectory(Root); File.WriteAllText(First, "original"); File.WriteAllText(Second, "original"); Inspector = new(Root); }
        public MainWindow Window(string theme = "dark") => new(Inspector, null, new ThemeController(Avalonia.Application.Current!, new Themes(theme))) { Width = 900, Height = 560 };
        public async Task<DataTransfer> Transfer(MainWindow window, string path, bool folder = false)
        {
            IStorageItem? item = folder ? await window.StorageProvider.TryGetFolderFromPathAsync(path) : await window.StorageProvider.TryGetFileFromPathAsync(path);
            item.Test().NotBeNull(); var data = new DataTransfer(); data.Add(DataTransferItem.CreateFile(item!)); return data;
        }
        public void Dispose() { File.Delete(First); File.Delete(Second); Directory.Delete(Root); }
    }

    private sealed class Inspector(string root) : IInspectionService
    {
        public List<string> Paths { get; } = [];
        public Exception? Failure; public TaskCompletionSource<RepositorySnapshot>? Delayed;
        public RepositorySnapshot Snapshot(string path) => new(root, path == root ? null : Path.GetFileName(path), [new("review # ñ.cs", null, ".", "M"), new("another.cs", null, ".", "M")]);
        public Task<RepositorySnapshot> InspectAsync(string path, CancellationToken cancellationToken = default)
        { Paths.Add(path); return Failure is { } error ? Task.FromException<RepositorySnapshot>(error) : Delayed?.Task ?? Task.FromResult(Snapshot(path)); }
        public Task<FileDiff> DiffAsync(string repository, string path, Comparison comparison, CancellationToken cancellationToken = default, DiffOptions? options = null) =>
            Task.FromResult(new FileDiff(path, comparison, "diff --git a/example b/example\n--- a/example\n+++ b/example\n@@ -1 +1 @@\n-old\n+new\n", false,
                [new("@@ -1 +1 @@", [new(DiffLineKind.Removed, 1, null, "old"), new(DiffLineKind.Added, null, 1, "new")])]));
    }
}
