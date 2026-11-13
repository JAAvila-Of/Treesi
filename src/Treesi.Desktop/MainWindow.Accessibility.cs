using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Treesi.Domain;

namespace Treesi.Desktop;

public sealed partial class MainWindow
{
    private Control? ActiveReviewPanel => new[] { "WorkspacePickerPanel", "CommitDetailsPanel", "RevisionPanel", "DiffOptionsPanel", "OpenRecoveryNotice" }
        .Select(Required<Control>).FirstOrDefault(panel => panel.IsVisible);

    private void InitializeAccessibility()
    {
        foreach (var name in new[] { "WorkspacePickerPanel", "CommitDetailsPanel", "RevisionPanel", "DiffOptionsPanel", "OpenRecoveryNotice" })
            KeyboardNavigation.SetTabNavigation(Required<Control>(name), KeyboardNavigationMode.Cycle);
        foreach (var (editor, id) in new[] { (_diff, "UnifiedDiffText"), (_before, "BeforeDiffText"), (_after, "AfterDiffText") })
        {
            AutomationProperties.SetAutomationId(editor.TextArea, id);
            AutomationProperties.SetHelpText(editor.TextArea, "Read-only untrusted comparison text. Plus marks additions; minus marks removals. Tab leaves the code. F6 changes region; Alt+Up/Down selects a change block.");
        }
        UpdateAccessibleComparison();
    }

    private bool HandleReviewNavigation(KeyEventArgs args)
    {
        if (ActiveReviewPanel is not null)
        {
            if (args.Key != Key.Escape && (args.Key is Key.F6 or Key.F3 || (args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0 && args.Key is Key.O or Key.R or Key.B or Key.F
                || (args.KeyModifiers & KeyModifiers.Alt) != 0 && args.Key is Key.Up or Key.Down))
            { args.Handled = true; return true; }
            return false;
        }
        if (args.Key == Key.F3 && (args.KeyModifiers & ~KeyModifiers.Shift) == 0)
        {
            if (_graphVisible) FindGraphCommit((args.KeyModifiers & KeyModifiers.Shift) != 0);
            else if (_search.IsEffectivelyEnabled) FindNext((args.KeyModifiers & KeyModifiers.Shift) != 0);
            args.Handled = true; return true;
        }
        if (args.Key != Key.F6 || (args.KeyModifiers & ~KeyModifiers.Shift) != 0) return false;
        var targets = ReviewRegions();
        if (targets.Count == 0) return false;
        var index = targets.FindIndex(target => target.IsKeyboardFocusWithin);
        var step = (args.KeyModifiers & KeyModifiers.Shift) != 0 ? -1 : 1;
        if (index < 0) index = step > 0 ? -1 : 0;
        targets[(index + step + targets.Count) % targets.Count].Focus(NavigationMethod.Tab);
        args.Handled = true; return true;
    }

    private List<Control> ReviewRegions()
    {
        var regions = new List<Control> { Required<Button>("OpenFolderButton") };
        if (_graphVisible)
        {
            regions.Add(ListRegion(Required<ListBox>("GraphReferences"), Required<TextBox>("GraphReferenceFilter")));
            regions.Add(Required<TextBox>("GraphFindInput"));
            regions.Add(ListRegion(Required<ListBox>("GraphCommits"), Required<TextBox>("GraphStartInput")));
            regions.Add(Required<CheckBox>("ReduceMotionToggle"));
            return regions.Where(control => control.IsEffectivelyVisible && control.IsEffectivelyEnabled && control.Focusable).ToList();
        }
        if (Required<Control>("HomeSurface").IsVisible)
            regions.Add(ListRegion(_recents, Required<Button>("HomeOpenRepositoryButton")));
        else
        {
            regions.Add(Required<Control>("HistorySidebar").IsVisible
                ? ListRegion(Required<ListBox>("HistoryCommits"), Required<Control>("HistoryStartInput")) : ListRegion(_files, _filter));
            regions.Add(_search);
            if (_currentDiff is not null)
            {
                if (_diffMode.SelectedIndex == 1) { regions.Add(_before.TextArea); regions.Add(_after.TextArea); }
                else regions.Add(_diff.TextArea);
            }
        }
        regions.Add(Required<CheckBox>("ReduceMotionToggle"));
        return regions.Where(control => control.IsEffectivelyVisible && control.IsEffectivelyEnabled && control.Focusable).ToList();
    }

    private static Control ListRegion(ListBox list, Control fallback) => list.ContainerFromIndex(Math.Max(0, list.SelectedIndex)) ?? fallback;

    private void FocusReferencesAfterLayout()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_closed && Required<Control>("RevisionPanel").IsVisible && Required<Control>("RevisionReferencePicker").IsVisible)
                ListRegion(Required<ListBox>("RevisionReferencesList"), Required<Button>("BackToRevisionDraftButton")).Focus(NavigationMethod.Tab);
        }, DispatcherPriority.Loaded);
    }

    private void UpdateAccessibleComparison()
    {
        var diff = _currentDiff;
        if (diff is null)
        {
            foreach (var editor in new[] { _diff, _before, _after }) AutomationProperties.SetName(editor.TextArea, "No text comparison loaded.");
            return;
        }
        var file = HistoryCommitItem.Preview(diff.Path, 256);
        var options = diff.Options ?? _diffOptions;
        var context = options.FullContext ? "full file context" : $"{options.ContextLines} context lines";
        var policy = options.Whitespace == WhitespaceMode.Exact ? "exact whitespace" : DescribeWhitespace(options.Whitespace) + "; changes may be hidden";
        var before = Required<TextBlock>("BeforeLabel").Text ?? "Before";
        var after = Required<TextBlock>("AfterLabel").Text ?? "After";
        var scope = $"{file}. {before} to {after}. {context}; {policy}.";
        AutomationProperties.SetName(_diff.TextArea, "Read-only unified diff. " + scope);
        AutomationProperties.SetName(_before.TextArea, $"Read-only before diff. {file}. {before}. {context}; {policy}.");
        AutomationProperties.SetName(_after.TextArea, $"Read-only after diff. {file}. {after}. {context}; {policy}.");
    }
}
