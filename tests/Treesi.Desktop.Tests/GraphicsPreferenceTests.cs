using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Desktop.Appearance;

namespace Treesi.Desktop.Tests;

[TestFixture]
public sealed class GraphicsPreferenceTests
{
    private sealed class Store : IGraphicsPreferenceStore
    {
        public GraphicsMode Mode; public bool Writable = true; public int Writes;
        public GraphicsMode Load() => Mode;
        public bool Save(GraphicsMode mode) { Writes++; if (!Writable) return false; Mode = mode; return true; }
    }

    [Test]
    public void SoftwareSkipsHardwareDetectionAndChangesOnlyApplyAfterRestart()
    {
        var store = new Store(); var current = new GraphicsPreferences(store);
        current.UseSoftware(() => true).Test().BeFalse();
        current.UseSoftware(() => null).Test().BeFalse();
        current.UseSoftware(() => false).Test().BeTrue();
        current.Save(GraphicsMode.Software).Test().BeTrue();
        current.RequiresRestart.Test().BeTrue();
        current.UseSoftware(() => true).Test().BeFalse();
        var restarted = new GraphicsPreferences(store);
        restarted.UseSoftware(() => throw new InvalidOperationException("Hardware must not be probed in software mode.")).Test().BeTrue();
        restarted.RequiresRestart.Test().BeFalse();
        current.Save(GraphicsMode.Automatic).Test().BeTrue(); current.RequiresRestart.Test().BeFalse();
        store.Writable = false;
        current.Save(GraphicsMode.Software).Test().BeFalse(); current.Saved.Test().Be(GraphicsMode.Automatic);
    }

    [Test]
    public void StoredPreferenceIsBoundedAndUnknownValuesFallBackToAutomatic()
    {
        var directory = Path.Combine(Path.GetTempPath(), "treesi-graphics-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "graphics.json"); var store = new JsonGraphicsPreferenceStore(path);
        try
        {
            store.Load().Test().Be(GraphicsMode.Automatic);
            store.Save(GraphicsMode.Software).Test().BeTrue(); store.Load().Test().Be(GraphicsMode.Software);
            store.Save((GraphicsMode)99).Test().BeFalse(); store.Load().Test().Be(GraphicsMode.Software);
            foreach (var content in new[] { "invalid", "{\"Mode\":\"future-driver\"}", "{\"Mode\":4}", new string(' ', 4097) })
            { File.WriteAllText(path, content); store.Load().Test().Be(GraphicsMode.Automatic); }
            store.Save(GraphicsMode.Automatic).Test().BeTrue(); store.Load().Test().Be(GraphicsMode.Automatic);
            Directory.GetFiles(directory, "*.tmp").Length.Test().Be(0);
            var blocked = Path.Combine(directory, "blocked"); Directory.CreateDirectory(blocked);
            new JsonGraphicsPreferenceStore(blocked).Save(GraphicsMode.Software).Test().BeFalse();
            Directory.GetFiles(directory, "*.tmp").Length.Test().Be(0); Directory.Delete(blocked);
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(directory)) Directory.Delete(directory); }
    }

    [AvaloniaTest, TestCase("dark"), TestCase("light")]
    public void DialogDraftCanBeCancelledAndFailedSavesRemainVisible(string theme)
    {
        var store = new Store(); var preferences = new GraphicsPreferences(store);
        var dialog = new GraphicsSettingsWindow(preferences) { RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            dialog.Show(); Dispatcher.UIThread.RunJobs();
            var selector = dialog.FindControl<ComboBox>("GraphicsModeSelector")!;
            selector.SelectedIndex = 1;
            using var frame = dialog.CaptureRenderedFrame();
            store.Writes.Test().Be(0); preferences.RequiresRestart.Test().BeFalse();
            store.Writable = false;
            dialog.FindControl<Button>("SaveGraphicsSettingsButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialog.IsVisible.Test().BeTrue(); dialog.FindControl<TextBlock>("GraphicsSaveError")!.IsVisible.Test().BeTrue();
            preferences.Saved.Test().Be(GraphicsMode.Automatic); dialog.PreferenceSaved.Test().BeFalse();
            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();
            dialog.IsVisible.Test().BeFalse(); preferences.Saved.Test().Be(GraphicsMode.Automatic);
        }
        finally { dialog.Close(); }
    }

    [AvaloniaTest]
    public void DialogPersistsTheSelectionWithoutChangingTheActiveMode()
    {
        var store = new Store(); var preferences = new GraphicsPreferences(store);
        var dialog = new GraphicsSettingsWindow(preferences);
        try
        {
            dialog.Show(); dialog.FindControl<ComboBox>("GraphicsModeSelector")!.SelectedIndex = 1;
            dialog.FindControl<Button>("SaveGraphicsSettingsButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialog.PreferenceSaved.Test().BeTrue(); dialog.IsVisible.Test().BeFalse();
            preferences.Startup.Test().Be(GraphicsMode.Automatic); preferences.RequiresRestart.Test().BeTrue();
            new GraphicsPreferences(store).Startup.Test().Be(GraphicsMode.Software);
        }
        finally { dialog.Close(); }
    }
}
