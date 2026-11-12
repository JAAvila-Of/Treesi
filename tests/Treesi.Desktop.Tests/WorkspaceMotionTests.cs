using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Threading;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Desktop.Appearance;

namespace Treesi.Desktop.Tests;

[TestFixture]
public sealed class WorkspaceMotionTests
{
    [AvaloniaTest]
    public async Task RevealsCompleteAndCancellationRestoresTheBaseAppearance()
    {
        var content = new Border { Background = Brushes.Teal, Width = 100, Height = 80 };
        var window = new Window { Content = content, Width = 200, Height = 160 };
        using var motion = new WorkspaceMotion(window) { Reduced = false };
        try
        {
            window.Show(); using var frame = window.CaptureRenderedFrame();
            motion.Reveal(content);
            await motion.Pending.WaitAsync(TimeSpan.FromSeconds(5));
            content.Opacity.Test().Be(1d);
            (content.RenderTransform?.Value.M32 ?? 0).Test().Be(0d);
            motion.Reveal(content);
            var cancelled = motion.Pending;
            motion.Reveal(content);
            var latest = motion.Pending;
            motion.Reduced = true;
            await Task.WhenAll(cancelled, latest).WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            content.Opacity.Test().Be(1d);
            window.Classes.Contains("motion-enabled").Test().BeFalse();
            var finished = motion.Pending;
            motion.Reveal(content);
            ReferenceEquals(finished, motion.Pending).Test().BeTrue();
            motion.Dispose(); motion.Reveal(content);
            motion.Pending.IsCompletedSuccessfully.Test().BeTrue();
        }
        finally { window.Close(); }
    }

    [Test]
    public void ReducedMotionPreferenceRoundTripsAndRejectsOversizedOrInvalidData()
    {
        var directory = Path.Combine(Path.GetTempPath(), "treesi-motion-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "motion.json");
        var store = new JsonMotionPreferenceStore(path);
        try
        {
            store.Load().Test().BeFalse();
            store.Save(true).Test().BeTrue(); store.Load().Test().BeTrue();
            store.Save(false).Test().BeTrue(); store.Load().Test().BeFalse();
            File.WriteAllText(path, "invalid"); store.Load().Test().BeFalse();
            File.WriteAllText(path, new string(' ', 4097)); store.Load().Test().BeFalse();
            Directory.GetFiles(directory, "*.tmp").Length.Test().Be(0);
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }
}
