using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Application;
using Treesi.Desktop.Appearance;
using Treesi.Domain;

namespace Treesi.Desktop.Tests;

[TestFixture]
public sealed class AccessibilityTests
{
    private static readonly string Root = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "workspace", "accessible-review");
    private static T Get<T>(Window window, string name) where T : Control => window.FindControl<T>(name)!;
    private static void Click(Window window, string name) => Get<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void PressKey(Window window, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        Dispatcher.UIThread.RunJobs();
        var raw = RawInputModifiers.None;
        if ((modifiers & KeyModifiers.Shift) != 0) raw |= RawInputModifiers.Shift;
        if ((modifiers & KeyModifiers.Control) != 0) raw |= RawInputModifiers.Control;
        if ((modifiers & KeyModifiers.Alt) != 0) raw |= RawInputModifiers.Alt;
        window.KeyPress(key, raw, PhysicalKey.None, null); window.KeyRelease(key, raw, PhysicalKey.None, null); Dispatcher.UIThread.RunJobs();
    }
    private static MainWindow Create(Inspector inspector, string theme = "dark") => new(inspector, null,
        new ThemeController(Avalonia.Application.Current!, new Preferences(theme)), revisions: new Revisions(), history: new History())
    { Width = 900, Height = 560 };
    private static AutomationPeer Peer(TextEditor editor) => ControlAutomationPeer.CreatePeerForElement(editor.TextArea)!;

    [AvaloniaTest]
    public async Task CodePeersExposeActualReadOnlyTextAndNeverAcceptWrites()
    {
        var inspector = new Inspector(); var window = Create(inspector);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root);
            var editor = Get<TextEditor>(window, "DiffEditor"); var peer = Peer(editor); var value = (IValueProvider)peer;
            peer.GetAutomationControlType().Test().Be(AutomationControlType.Edit);
            peer.GetAutomationId().Test().Be("UnifiedDiffText"); value.IsReadOnly.Test().BeTrue(); value.Value.Test().Be(editor.Text);
            peer.GetName()!.Contains("review.cs", StringComparison.Ordinal).Test().BeTrue();
            peer.GetName()!.Contains("old needle", StringComparison.Ordinal).Test().BeFalse();
            Action set = () => value.SetValue("modified"); set.Test().Throw<InvalidOperationException>();
            var before = editor.Text; window.CaptureRenderedFrame()?.Dispose(); editor.TextArea.Focus().Test().BeTrue(); window.KeyTextInput("blocked input"); editor.Text.Test().Be(before);
            Get<TabStrip>(window, "DiffModeSelector").SelectedIndex = 1;
            foreach (var name in new[] { "BeforeEditor", "AfterEditor" })
            {
                var side = Get<TextEditor>(window, name); ((IValueProvider)Peer(side)).Value.Test().Be(side.Text);
                ((IValueProvider)Peer(side)).IsReadOnly.Test().BeTrue();
            }
            inspector.Empty = true; Get<ComboBox>(window, "ComparisonSelector").SelectedIndex = 1; await window.PendingInspection;
            value.Value.Test().Be(""); peer.GetName()!.Contains("No text comparison", StringComparison.Ordinal).Test().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase("dark")]
    [TestCase("light")]
    public async Task F6CyclesVisibleRegionsAndTabLeavesTheReadOnlyCode(string theme)
    {
        var inspector = new Inspector(); var window = Create(inspector, theme);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root);
            Get<Button>(window, "OpenFolderButton").Focus(NavigationMethod.Tab);
            PressKey(window, Key.F6); Get<ListBox>(window, "ChangedFiles").IsKeyboardFocusWithin.Test().BeTrue();
            PressKey(window, Key.F6); Get<TextBox>(window, "DiffSearch").IsFocused.Test().BeTrue();
            PressKey(window, Key.F6); var editor = Get<TextEditor>(window, "DiffEditor"); editor.IsKeyboardFocusWithin.Test().BeTrue();
            using var frame = window.CaptureRenderedFrame();
            var brush = Get<Border>(window, "DiffSurface").BorderBrush as ISolidColorBrush;
            brush!.Color.Test().Be(((ISolidColorBrush)window.FindResource(window.ActualThemeVariant, "Accent.Primary")!).Color);
            PressKey(window, Key.Tab); editor.IsKeyboardFocusWithin.Test().BeFalse();
            editor.TextArea.Focus(NavigationMethod.Tab); PressKey(window, Key.F6); Get<CheckBox>(window, "ReduceMotionToggle").IsFocused.Test().BeTrue();
            PressKey(window, Key.F6, KeyModifiers.Shift); editor.IsKeyboardFocusWithin.Test().BeTrue();
            PressKey(window, Key.B, KeyModifiers.Control); Get<Control>(window, "FilesPanel").IsVisible.Test().BeFalse();
            Get<Button>(window, "OpenFolderButton").Focus(); PressKey(window, Key.F6); Get<TextBox>(window, "DiffSearch").IsFocused.Test().BeTrue();
            inspector.Calls.Test().Be(1);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task SplitAndHomeRegionNavigationSkipsHiddenControlsAndKeepsFocusVisible()
    {
        var window = Create(new Inspector());
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root);
            var unified = Get<TextEditor>(window, "DiffEditor"); window.CaptureRenderedFrame()?.Dispose(); unified.TextArea.Focus().Test().BeTrue();
            Get<TabStrip>(window, "DiffModeSelector").SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            (Get<TextEditor>(window, "AfterEditor").IsKeyboardFocusWithin || Get<TabStrip>(window, "DiffModeSelector").IsKeyboardFocusWithin).Test().BeTrue();
            unified.IsKeyboardFocusWithin.Test().BeFalse();
            Get<TextBox>(window, "DiffSearch").Focus(); PressKey(window, Key.F6); Get<TextEditor>(window, "BeforeEditor").IsKeyboardFocusWithin.Test().BeTrue();
            PressKey(window, Key.F6); Get<TextEditor>(window, "AfterEditor").IsKeyboardFocusWithin.Test().BeTrue();
            Get<TabStrip>(window, "DiffModeSelector").SelectedIndex = 0;
            (unified.IsKeyboardFocusWithin || Get<TabStrip>(window, "DiffModeSelector").IsKeyboardFocusWithin).Test().BeTrue();
            Get<TextEditor>(window, "AfterEditor").IsKeyboardFocusWithin.Test().BeFalse();
            Click(window, "RecentWorkspacesButton"); Get<Button>(window, "OpenFolderButton").Focus(); PressKey(window, Key.F6);
            Get<ListBox>(window, "RecentRepositories").IsKeyboardFocusWithin.Test().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task OptionDraftCyclesTabBlocksBackgroundShortcutsAndEscapeReturnsFocus()
    {
        var inspector = new Inspector(); var window = Create(inspector);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root); var calls = inspector.Calls;
            Click(window, "DiffOptionsButton");
            for (var index = 0; index < 16; index++) { PressKey(window, Key.Tab); Get<Control>(window, "DiffOptionsPanel").IsKeyboardFocusWithin.Test().BeTrue(); }
            for (var index = 0; index < 16; index++) { PressKey(window, Key.Tab, KeyModifiers.Shift); Get<Control>(window, "DiffOptionsPanel").IsKeyboardFocusWithin.Test().BeTrue(); }
            PressKey(window, Key.R, KeyModifiers.Control); PressKey(window, Key.B, KeyModifiers.Control); PressKey(window, Key.F6); PressKey(window, Key.Down, KeyModifiers.Alt);
            inspector.Calls.Test().Be(calls); Get<Control>(window, "FilesPanel").IsVisible.Test().BeTrue();
            Get<TextBox>(window, "ContextLinesInput").Text = "bad"; Click(window, "ApplyDiffOptionsButton");
            Get<TextBox>(window, "ContextLinesInput").IsFocused.Test().BeTrue();
            AutomationProperties.GetLiveSetting(Get<Control>(window, "DiffOptionsError")).Test().Be(AutomationLiveSetting.Polite);
            PressKey(window, Key.Escape); Get<Button>(window, "DiffOptionsButton").IsFocused.Test().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task RevisionAndCommitPanelsKeepTheirFocusAndDisappearOnHomeNavigation()
    {
        var window = Create(new Inspector());
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root); Click(window, "CompareRevisionsButton");
            for (var index = 0; index < 12; index++) { PressKey(window, Key.Tab); Get<Control>(window, "RevisionPanel").IsKeyboardFocusWithin.Test().BeTrue(); }
            Click(window, "BrowseRevisionReferencesButton"); await window.PendingInspection; Dispatcher.UIThread.RunJobs(); Get<ListBox>(window, "RevisionReferencesList").IsKeyboardFocusWithin.Test().BeTrue();
            Click(window, "BackToRevisionDraftButton"); Get<TextBox>(window, "BaseRevisionInput").IsFocused.Test().BeTrue();
            PressKey(window, Key.Escape); Get<Button>(window, "CompareRevisionsButton").IsFocused.Test().BeTrue();
            Click(window, "HistoryButton"); await window.PendingInspection; Get<ListBox>(window, "HistoryCommits").SelectedIndex = 0; await window.PendingInspection;
            Click(window, "RevisionEndpointsButton");
            for (var index = 0; index < 12; index++) { PressKey(window, Key.Tab); Get<Control>(window, "CommitDetailsPanel").IsKeyboardFocusWithin.Test().BeTrue(); }
            PressKey(window, Key.Escape); Get<Button>(window, "RevisionEndpointsButton").IsFocused.Test().BeTrue();
            Click(window, "RevisionEndpointsButton"); Click(window, "RecentWorkspacesButton"); Get<Control>(window, "CommitDetailsPanel").IsVisible.Test().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task F3AndShiftEnterSearchInBothDirectionsWithoutChangingThePatch()
    {
        var inspector = new Inspector(); var window = Create(inspector);
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root); var editor = Get<TextEditor>(window, "DiffEditor"); var text = editor.Text;
            Get<TextBox>(window, "DiffSearch").Text = "needle"; editor.Select(0, 0); editor.TextArea.Focus();
            PressKey(window, Key.F3); var first = editor.SelectionStart; editor.SelectedText.Test().Be("needle");
            PressKey(window, Key.F3); (editor.SelectionStart > first).Test().BeTrue();
            PressKey(window, Key.F3, KeyModifiers.Shift); editor.SelectionStart.Test().Be(first);
            Get<TextBox>(window, "DiffSearch").Focus(); PressKey(window, Key.Enter, KeyModifiers.Shift); (editor.SelectionStart > first).Test().BeTrue();
            editor.Text.Test().Be(text); inspector.Calls.Test().Be(1);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task AccessibleNamesTrackFullContextWhitespaceAndPinnedSidesWithoutQuotingCode()
    {
        var window = Create(new Inspector());
        try
        {
            window.Show(); await window.OpenRepositoryAsync(Root); Click(window, "DiffOptionsButton");
            Get<CheckBox>(window, "FullContextToggle").IsChecked = true;
            Get<ComboBox>(window, "WhitespaceSelector").SelectedIndex = (int)WhitespaceMode.IgnoreTrailingSpace;
            Click(window, "ApplyDiffOptionsButton"); await window.PendingInspection;
            var name = Peer(Get<TextEditor>(window, "DiffEditor")).GetName()!;
            name.Contains("full file context", StringComparison.Ordinal).Test().BeTrue(); name.Contains("Ignore trailing space", StringComparison.Ordinal).Test().BeTrue();
            name.Contains("changes may be hidden", StringComparison.Ordinal).Test().BeTrue();
            Click(window, "CompareRevisionsButton"); Click(window, "ApplyRevisionButton"); await window.PendingInspection;
            Peer(Get<TextEditor>(window, "BeforeEditor")).GetName()!.Contains("BASE aaaaaaaaaaaa", StringComparison.Ordinal).Test().BeTrue();
            Peer(Get<TextEditor>(window, "AfterEditor")).GetName()!.Contains("TARGET bbbbbbbbbbbb", StringComparison.Ordinal).Test().BeTrue();
        }
        finally { window.Close(); }
    }

    [Test]
    public void MetadataPreviewsNeverCutAJoinedEmojiOrCombiningSequence()
    {
        foreach (var suffix in new[] { "👩🏽‍💻", "e\u0301" })
        {
            var subject = new string('x', 255) + suffix + " more";
            new HistoryCommitItem(new(new('a', 40), subject, "Example", "date")).Subject.Test().Be(new string('x', 255) + "…");
        }
    }

    private sealed class Preferences(string theme) : IThemePreferenceStore { public string? Load() => theme; public bool Save(string themeId) => true; }
    private sealed class Inspector : IInspectionService
    {
        public int Calls { get; private set; }
        public bool Empty { get; set; }
        public Task<RepositorySnapshot> InspectAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(new RepositorySnapshot(path, "review.cs", [new("review.cs", null, ".", "M")]));
        public Task<FileDiff> DiffAsync(string repository, string path, Comparison comparison, CancellationToken cancellationToken = default, DiffOptions? options = null)
        { Calls++; return Task.FromResult(Empty ? new FileDiff(path, comparison, "", false, [], Options: options) : Diff(options ?? DiffOptions.Default) with { Comparison = comparison }); }
        public static FileDiff Diff(DiffOptions options) => new("review.cs", Comparison.WorkingTree,
            "diff --git a/review.cs b/review.cs\n--- a/review.cs\n+++ b/review.cs\n@@ -1,4 +1,4 @@\n // needle first\n-var value = \"old needle\";\n+var value = \"new needle\";\n // needle last\n return value;\n", false,
            [new("@@ -1,4 +1,4 @@", [new(DiffLineKind.Context, 1, 1, "// needle first"), new(DiffLineKind.Removed, 2, null, "var value = \"old needle\";"), new(DiffLineKind.Added, null, 2, "var value = \"new needle\";"), new(DiffLineKind.Context, 3, 3, "// needle last"), new(DiffLineKind.Context, 4, 4, "return value;")])], Options: options);
    }
    private sealed class Revisions : IRevisionService
    {
        public Task<RevisionReferences> ReferencesAsync(string repository, CancellationToken cancellationToken = default) => Task.FromResult(new RevisionReferences([new("HEAD", new('b', 40), "head")], false));
        public Task<RevisionEndpoints> ResolveAsync(string repository, RevisionSelection selection, CancellationToken cancellationToken = default) => Task.FromResult(new RevisionEndpoints(repository, selection.Base, new('a', 40), selection.Target, new('b', 40), new('a', 40), selection.Mode));
        public Task<RevisionComparison> CompareAsync(RevisionEndpoints endpoints, CancellationToken cancellationToken = default) => Task.FromResult(new RevisionComparison(endpoints, [new("review.cs", "M")]));
        public Task<FileDiff> DiffAsync(RevisionEndpoints endpoints, string path, DiffOptions? options = null, CancellationToken cancellationToken = default) => Task.FromResult(Inspector.Diff(options ?? DiffOptions.Default) with { Comparison = Comparison.Revisions, Revisions = endpoints });
    }
    private sealed class History : IHistoryService
    {
        public Task<HistoryPage> PageAsync(string repository, HistoryQuery query, CancellationToken cancellationToken = default) => Task.FromResult(new HistoryPage(repository, new('b', 40), null, 0, 30, [new(new('b', 40), "test: synthetic commit", "Example", "2026-01-01")], null));
        public Task<CommitDetail> CommitAsync(string repository, string revision, CancellationToken cancellationToken = default) => Task.FromResult(new CommitDetail(repository, revision, [new('a', 40)], "Example", "Example", "Untrusted message"));
    }
}
