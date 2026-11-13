using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Application;
using Treesi.Desktop.Appearance;
using Treesi.Domain;

namespace Treesi.Desktop.Tests;

[TestFixture]
public sealed class WorktreeWorkspaceTests
{
    private static readonly string Root = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "workspace", "treesi");
    private static T Get<T>(Window window, string name) where T : Control => window.FindControl<T>(name)!;
    private static void Click(Window window, string name) => Get<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static MainWindow Create(Working working, Worktrees worktrees, string theme = "dark") => new(working, null,
        new ThemeController(Avalonia.Application.Current!, new Preferences(theme)), worktrees: worktrees)
    { Width = 900, Height = 560 };
    private static void Discover(Window window) { Click(window, "WorkspacePickerButton"); Click(window, "DiscoverWorktreesButton"); }

    [AvaloniaTest]
    [TestCase("dark")]
    [TestCase("light")]
    public async Task DiscoveryIsExplicitAndOpeningReusesIndependentTabs(string theme)
    {
        var working = new Working(); var worktrees = new Worktrees(); var window = Create(working, worktrees, theme);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root); worktrees.Reads.Test().Be(0);
            Click(window, "WorkspacePickerButton"); worktrees.Reads.Test().Be(0);
            Click(window, "DiscoverWorktreesButton"); await window.PendingInspection;
            worktrees.Reads.Test().Be(1); working.Reads.Test().Be(1);
            var list = Get<ListBox>(window, "RelatedWorktrees"); list.Items.Count.Test().Be(4);
            list.SelectedIndex = 2; Get<Button>(window, "OpenWorktreeButton").IsEnabled.Test().BeFalse();
            list.SelectedIndex = 3; Get<Button>(window, "OpenWorktreeButton").IsEnabled.Test().BeFalse();
            list.SelectedIndex = 1; Get<Button>(window, "OpenWorktreeButton").IsEnabled.Test().BeTrue();
            using var frame = window.CaptureRenderedFrame(); frame.Test().NotBeNull();
            foreach (var name in new[] { "WorktreesPanel", "OpenWorktreeButton", "WorktreeNotice" })
            {
                var control = Get<Control>(window, name); var point = control.TranslatePoint(default, window)!.Value;
                (point.X >= 0 && point.X + control.Bounds.Width <= window.ClientSize.Width + 1).Test().BeTrue();
                (point.Y >= 0 && point.Y + control.Bounds.Height <= window.ClientSize.Height + 1).Test().BeTrue();
            }
            if (Environment.GetEnvironmentVariable("TREESI_RENDER_OUTPUT") is { Length: > 0 } directory) { Directory.CreateDirectory(directory); frame!.Save(Path.Combine(directory, $"worktrees-{theme}-compact.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); }
            Click(window, "OpenWorktreeButton"); await window.PendingInspection;
            Get<TabStrip>(window, "RepositoryTabs").Items.Count.Test().Be(2); working.Reads.Test().Be(2);
            Discover(window); await window.PendingInspection; Get<ListBox>(window, "RelatedWorktrees").SelectedIndex = 0;
            Click(window, "OpenWorktreeButton"); await window.PendingInspection;
            working.Reads.Test().Be(2); Get<TabStrip>(window, "RepositoryTabs").Items.Count.Test().Be(2);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task ClosingOrChangingRootRejectsUncooperativeLateResults()
    {
        var worktrees = new Worktrees { Delayed = new() }; var window = Create(new(), worktrees);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root); Discover(window); var pending = window.PendingInspection;
            Click(window, "CloseWorktreesButton"); worktrees.Token.IsCancellationRequested.Test().BeTrue();
            worktrees.Delayed.SetResult(Worktrees.Result(Root)); await pending;
            Get<Control>(window, "WorktreesPanel").IsVisible.Test().BeFalse(); Get<ListBox>(window, "RelatedWorktrees").Items.Count.Test().Be(0);
            worktrees.Delayed = new(); Discover(window); pending = window.PendingInspection;
            await window.OpenRepositoryAsync(Root + "-other"); worktrees.Delayed.SetResult(Worktrees.Result(Root)); await pending;
            Get<Control>(window, "WorktreesPanel").IsVisible.Test().BeFalse(); Get<ListBox>(window, "RelatedWorktrees").Items.Count.Test().Be(0);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task FailedDiscoveryCanRetryAndEscapeReturnsFocus()
    {
        var worktrees = new Worktrees { Fail = true }; var window = Create(new(), worktrees);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root); Discover(window); await window.PendingInspection;
            Get<TextBlock>(window, "WorktreeNotice").Text.Test().Be("Fixture discovery failed.");
            Get<Button>(window, "OpenWorktreeButton").IsEnabled.Test().BeFalse();
            worktrees.Fail = false; Click(window, "RefreshWorktreesButton"); await window.PendingInspection;
            Get<ListBox>(window, "RelatedWorktrees").Items.Count.Test().Be(4);
            Get<Button>(window, "OpenWorktreeButton").Focus();
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null); window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Get<Button>(window, "RefreshWorktreesButton").IsFocused.Test().BeTrue();
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Get<Control>(window, "WorktreesPanel").IsVisible.Test().BeFalse();
            Get<Button>(window, "WorkspacePickerButton").IsFocused.Test().BeTrue();
        }
        finally { window.Close(); }
    }

    private sealed class Preferences(string theme) : IThemePreferenceStore { public string? Load() => theme; public bool Save(string themeId) => true; }
    private sealed class Working : IInspectionService
    {
        public int Reads { get; private set; }
        public Task<RepositorySnapshot> InspectAsync(string path, CancellationToken cancellationToken = default) { Reads++; return Task.FromResult(new RepositorySnapshot(path, null, [new("sample.txt", null, ".", "M")])); }
        public Task<FileDiff> DiffAsync(string repository, string path, Comparison comparison, CancellationToken cancellationToken = default, DiffOptions? options = null) => Task.FromResult(new FileDiff(path, comparison, "working", false, []));
    }
    private sealed class Worktrees : IWorktreeService
    {
        public int Reads { get; private set; }
        public bool Fail { get; set; }
        public CancellationToken Token { get; private set; }
        public TaskCompletionSource<RepositoryWorktrees>? Delayed { get; set; }
        public static RepositoryWorktrees Result(string root) => new(root, [new(Root, new string('a', 40), "refs/heads/main"), new(Root + "-feature", new string('b', 40), "refs/heads/feature/review", IsLocked: true, LockReason: "Portable location"), new(Root + "-bare", null, null, IsBare: true), new(Root + "-missing", new string('c', 40), null, IsDetached: true, IsPrunable: true, PruneReason: "Not available")]);
        public Task<RepositoryWorktrees> ListAsync(string repository, CancellationToken cancellationToken = default)
        {
            Reads++; Token = cancellationToken;
            return Fail ? Task.FromException<RepositoryWorktrees>(new InspectionException("git_failed", "Fixture discovery failed.")) : Delayed?.Task ?? Task.FromResult(Result(repository));
        }
    }
}
