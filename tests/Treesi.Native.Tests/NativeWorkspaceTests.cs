using System.Diagnostics;
using JAAvila.FluentOperations;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;
using Treesi.TestSupport;

namespace Treesi.Native.Tests;

[TestFixture]
[NonParallelizable]
public sealed class NativeWorkspaceTests
{
    private WindowsDriver? _driver;
    private SyntheticRepository? _repository;

    [OneTimeSetUp]
    public void RequireIsolatedDesktop() => IsolatedExecution.Require("native-ui");

    [SetUp]
    public void LaunchOnlyTheFixtureApplication()
    {
        var executable = Environment.GetEnvironmentVariable("TREESI_NATIVE_APP")
            ?? throw new InvalidOperationException("TREESI_NATIVE_APP must name the published Treesi executable.");
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            throw new InvalidOperationException("The published Treesi executable does not exist.");
        _repository = new SyntheticRepository(revisionHistory: true);
        var options = new AppiumOptions { PlatformName = "Windows", AutomationName = "Wincore", App = executable };
        options.AddAdditionalAppiumOption("appArguments", $"\"{_repository.SamplePath}\"");
        options.AddAdditionalAppiumOption("shouldCloseApp", true);
        _driver = new WindowsDriver(new Uri("http://127.0.0.1:4723"), options, TimeSpan.FromSeconds(60));
        WaitUntil(() => Element("FileTitle").Text == "sample.txt" && Element("DiffSearch").Enabled);
    }

    [TearDown]
    public void CloseTheOwnedSession()
    {
        try
        {
            if (_driver is not null && TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
            {
                var directory = Environment.GetEnvironmentVariable("TREESI_NATIVE_RESULTS");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    File.WriteAllText(Path.Combine(directory, TestContext.CurrentContext.Test.Name + ".xml"), _driver.PageSource);
                    _driver.GetScreenshot().SaveAsFile(Path.Combine(directory, TestContext.CurrentContext.Test.Name + ".png"));
                }
            }
        }
        finally
        {
            try { _driver?.Quit(); }
            finally { _driver?.Dispose(); _driver = null; _repository?.Dispose(); _repository = null; }
        }
    }

    [Test]
    public void DirectFileOpenExposesAccessibleWorkspaceAndChangeCounts()
    {
        WaitUntil(() => Element("AddedCount").Text == "+1");
        Element("RemovedCount").Text.Test().Be("−1");
        Element("ChangedFiles").Displayed.Test().BeTrue();
        Element("DiffSearch").Enabled.Test().BeTrue();
    }

    [Test]
    public void NativeThemeSelectorCanSwitchBothThemes()
    {
        SelectCombo("ThemeSelector", "Light");
        WaitUntil(() => ComboText("ThemeSelector").Contains("Light", StringComparison.Ordinal));
        SelectCombo("ThemeSelector", "Dark");
        WaitUntil(() => ComboText("ThemeSelector").Contains("Dark", StringComparison.Ordinal));
        Element("FileTitle").Text.Test().Be("sample.txt");
    }

    [Test]
    public void NativeFilterUpdatesAndRestoresTheSelection()
    {
        var filter = Element("FileFilter"); filter.SendKeys("missing-file");
        WaitUntil(() => Element("EmptyTitle").Text == "No matching files.");
        filter.Clear();
        WaitUntil(() => Element("FileTitle").Text == "sample.txt" && Element("DiffSearch").Enabled);
        Element("AddedCount").Text.Test().Be("+1");
    }

    [Test]
    public void NativeComparisonAndSearchWorkThroughAccessibility()
    {
        SelectCombo("ComparisonSelector", "Index vs HEAD");
        WaitUntil(() => Element("EmptyTitle").Text == "No differences here.");
        SelectCombo("ComparisonSelector", "Working tree vs Index");
        WaitUntil(() => Element("DiffSearch").Enabled);
        Element("DiffSearch").SendKeys("changed"); Element("FindNextButton").Click();
        WaitUntil(() => Element("OperationStatus").Text == "Match selected in diff");
        Element("FileTitle").Text.Test().Be("sample.txt");
    }

    private AppiumElement Element(string id) => _driver!.FindElement(new ByAccessibilityId(id));

    [Test]
    public void NativeWorkspaceGroupsCanBeCreatedRenamedAndRemovedWithoutChangingRepositoryFiles()
    {
        var original = File.ReadAllText(_repository!.SamplePath);
        var name = "Review " + Guid.NewGuid().ToString("N")[..8];
        Element("WorkspacePickerButton").Click();
        Element("NewWorkspaceGroupButton").Click();
        EnterText("WorkspaceNameInput", name); Element("SaveWorkspaceNameButton").Click();
        WaitUntil(() => Element("WorkspacePickerCaption").Text == name && Element("AddCurrentWorkspaceRepositoryButton").Enabled);
        Element("AddCurrentWorkspaceRepositoryButton").Click();
        WaitUntil(() => Element("OpenWorkspaceRepositoryButton").Enabled);
        EnterText("WorkspaceNameInput", name + " renamed"); Element("SaveWorkspaceNameButton").Click();
        WaitUntil(() => Element("WorkspacePickerCaption").Text == name + " renamed");
        Element("OpenWorkspaceRepositoryButton").Click();
        WaitUntil(() => Element("DiffSearch").Enabled && Element("FileTitle").Text == "sample.txt");
        Element("WorkspacePickerButton").Click(); Element("DeleteWorkspaceGroupButton").Click();
        WaitUntil(() => Element("WorkspacePickerCaption").Text == "Local repositories");
        Element("CloseWorkspacePickerButton").Click();
        File.ReadAllText(_repository.SamplePath).Test().Be(original);
        Element("FileTitle").Text.Test().Be("sample.txt");
    }

    [Test]
    public void NativeRepositoryTabReturnsFromHomeAndClosesWithoutDeletingFiles()
    {
        var path = _repository!.SamplePath;
        var original = File.ReadAllText(path);
        var name = Path.GetFileName(Path.GetDirectoryName(path)!);
        Element("RepositoryTabs").Displayed.Test().BeTrue();
        Element("NewRepositoryTabButton").Click();
        WaitUntil(() => Element("ReturnToWorkspaceButton").Displayed);
        _driver!.FindElements(By.Name(name)).First(item => item.Displayed).Click();
        WaitUntil(() => Element("FileTitle").Text == "sample.txt" && Element("DiffSearch").Enabled);
        _driver.FindElement(By.Name("Close repository tab: " + name)).Click();
        WaitUntil(() => !_driver.FindElements(new ByAccessibilityId("ReturnToWorkspaceButton")).Any(item => item.Displayed) && Element("HomeOpenRepositoryButton").Displayed);
        File.ReadAllText(path).Test().Be(original);
        Directory.Exists(Path.Combine(Path.GetDirectoryName(path)!, ".git")).Test().BeTrue();
    }

    [Test]
    public void NativeLocalRevisionComparisonUsesCommittedDataAndCanReturnToWorkingChanges()
    {
        Element("CompareRevisionsButton").Click();
        Element("BrowseRevisionReferencesButton").Click();
        WaitUntil(() => Element("UseReferenceAsBaseButton").Enabled);
        Element("BackToRevisionDraftButton").Click();
        var from = Element("BaseRevisionInput"); from.Clear(); from.SendKeys("HEAD~1");
        var to = Element("TargetRevisionInput"); to.Clear(); to.SendKeys("HEAD");
        Element("ApplyRevisionButton").Click();
        WaitUntil(() => Element("DiffSearch").Enabled && Element("RevisionEndpointsButton").Displayed);
        EnterText("DiffSearch", "historical"); Element("FindNextButton").Click();
        WaitUntil(() => Element("OperationStatus").Text == "Match selected in diff");
        Element("RevisionEndpointsButton").Click(); Element("ReturnToWorkingButton").Click();
        WaitUntil(() => Element("ComparisonSelector").Displayed && Element("DiffSearch").Enabled);
        EnterText("DiffSearch", "changed"); Element("FindNextButton").Click();
        WaitUntil(() => Element("OperationStatus").Text == "Match selected in diff");
        Element("FileTitle").Text.Test().Be("sample.txt");
    }

    [Test]
    public void NativeComparisonOptionsApplyContextAndKeepWhitespaceFilteringVisible()
    {
        Element("DiffOptionsButton").Click();
        var context = Element("ContextLinesInput"); context.Clear(); context.SendKeys("10");
        SelectCombo("WhitespaceSelector", "Ignore trailing space");
        Element("ApplyDiffOptionsButton").Click();
        WaitUntil(() => Element("DiffSearch").Enabled && Element("WhitespaceWarningText").Displayed);
        Element("ComparisonDescription").Text.Contains("Context 10", StringComparison.Ordinal).Test().BeTrue();
        Element("DiffOptionsButton").Click(); Element("ResetDiffOptionsButton").Click(); Element("CancelDiffOptionsButton").Click();
        Element("WhitespaceWarningText").Displayed.Test().BeTrue();
        Element("DiffOptionsButton").Click(); Element("ResetDiffOptionsButton").Click(); Element("ApplyDiffOptionsButton").Click();
        WaitUntil(() => Element("DiffSearch").Enabled && Element("ComparisonDescription").Text.Contains("Context 3", StringComparison.Ordinal));
        _driver!.FindElements(new ByAccessibilityId("WhitespaceWarningText")).Any(item => item.Displayed).Test().BeFalse();
        Element("FileTitle").Text.Test().Be("sample.txt");
    }

    [Test]
    public void NativeRecentsResumeTheSelectionAndForgetOnlyTheShortcut()
    {
        Element("SplitModeButton").Click();
        Element("RecentWorkspacesButton").Click();
        WaitUntil(() => Element("ResumeWorkspaceButton").Displayed && Element("ResumeWorkspaceButton").Enabled);
        Element("ResumeWorkspaceButton").Click();
        WaitUntil(() => Element("DiffSearch").Displayed && Element("DiffSearch").Enabled);
        Element("FileTitle").Text.Test().Be("sample.txt");
        Element("NextBlockButton").Click();
        WaitUntil(() => Element("OperationStatus").Text == "Change block 1 of 1");
        Element("RecentWorkspacesButton").Click();
        WaitUntil(() => Element("RemoveRecentButton").Displayed);
        Element("RemoveRecentButton").Click();
        File.Exists(_repository!.SamplePath).Test().BeTrue();
        Element("ReturnToWorkspaceButton").Click();
        WaitUntil(() => Element("DiffSearch").Displayed);
        Element("FileTitle").Text.Test().Be("sample.txt");
    }

    [Test]
    public void NativeFocusAndFilePanelRestoreTheWorkingSelection()
    {
        bool Visible(string id) => _driver!.FindElements(new ByAccessibilityId(id)).Any(item => item.Displayed);
        Element("ToggleFilesButton").Click();
        WaitUntil(() => !Visible("ChangedFiles"));
        Element("FocusWorkspaceButton").Click();
        WaitUntil(() => !Visible("OpenFolderButton"));
        Element("DiffSearch").SendKeys("changed"); Element("FindNextButton").Click();
        WaitUntil(() => Element("OperationStatus").Text == "Match selected in diff");
        Element("FocusWorkspaceButton").Click();
        WaitUntil(() => Visible("OpenFolderButton"));
        Visible("ChangedFiles").Test().BeFalse();
        Element("ToggleFilesButton").Click();
        WaitUntil(() => Visible("ChangedFiles"));
        Element("FileTitle").Text.Test().Be("sample.txt");
    }

    [Test]
    public void NativeSplitLayoutExposesBlockNavigationAndSearch()
    {
        Element("SplitModeButton").Click();
        Element("NextBlockButton").Click();
        WaitUntil(() => Element("OperationStatus").Text == "Change block 1 of 1");
        Element("DiffSearch").SendKeys("changed"); Element("FindNextButton").Click();
        WaitUntil(() => Element("OperationStatus").Text == "Match selected in diff");
        Element("ReduceMotionToggle").Click();
        Element("UnifiedModeButton").Click();
        Element("FileTitle").Text.Test().Be("sample.txt");
        Element("ReduceMotionToggle").Click();
    }
    private string ComboText(string id) => Element(id).Text + " " + Element(id).GetAttribute("Value");
    [Test]
    public void NativeHistoryReviewsLocalAndInitialCommitsAndReturnsToWorkingChanges()
    {
        Element("HistoryButton").Click();
        WaitUntil(() => _driver!.FindElements(By.Name("test: create historical change")).Any(item => item.Displayed));
        _driver!.FindElements(By.Name("test: create historical change")).First(item => item.Displayed).Click();
        WaitUntil(() => Element("RevisionEndpointsButton").Displayed && Element("DiffSearch").Enabled);
        EnterText("DiffSearch", "historical"); Element("FindNextButton").Click();
        WaitUntil(() => Element("OperationStatus").Text == "Match selected in diff");
        Element("RevisionEndpointsButton").Click();
        WaitUntil(() => Element("CommitMessage").Displayed);
        Element("CommitMessage").GetAttribute("Value").Contains("test: create historical change", StringComparison.Ordinal).Test().BeTrue();
        Element("CloseCommitDetailsButton").Click();
        _driver.FindElements(By.Name("test: create isolated fixture")).First(item => item.Displayed).Click();
        WaitUntil(() => Element("RevisionEndpointsButton").Text.Contains("Empty tree", StringComparison.Ordinal) && Element("DiffSearch").Enabled);
        EnterText("DiffSearch", "original"); Element("FindNextButton").Click();
        WaitUntil(() => Element("OperationStatus").Text == "Match selected in diff");
        Element("RevisionEndpointsButton").Click(); Element("CommitWorkingButton").Click();
        WaitUntil(() => Element("ComparisonSelector").Displayed && Element("DiffSearch").Enabled);
        Element("FileTitle").Text.Test().Be("sample.txt");
    }

    [Test]
    public void NativeGraphFindsInitialCommitReviewsItAndReturnsToSelection()
    {
        Element("GraphViewButton").Click();
        WaitUntil(() => Element("GraphReviewButton").Enabled && Element("GraphNotice").Text.Contains("2 commits", StringComparison.Ordinal));
        EnterText("GraphFindInput", "test: create isolated fixture"); Element("GraphFindInput").SendKeys(Keys.Enter);
        Element("GraphDetailsButton").Click();
        WaitUntil(() => Element("GraphCommitTitle").Text == "test: create isolated fixture");
        Element("ReviewGraphCommitButton").Click();
        WaitUntil(() => Element("RevisionEndpointsButton").Text.Contains("Empty tree", StringComparison.Ordinal) && Element("DiffSearch").Enabled);
        EnterText("DiffSearch", "original"); Element("FindNextButton").Click();
        WaitUntil(() => Element("OperationStatus").Text == "Match selected in diff");
        Element("BackToGraphButton").Click();
        WaitUntil(() => Element("GraphCommitTitle").Displayed && Element("GraphCommitTitle").Text == "test: create isolated fixture");
        Element("GraphNotice").Text.Contains("2 commits", StringComparison.Ordinal).Test().BeTrue();
    }

    [Test]
    public void NativeWorktreeDiscoveryOpensLinkedChangesAndReturnsToExistingTab()
    {
        var linked = _repository!.AddLinkedWorktree();
        Element("WorkspacePickerButton").Click(); Element("DiscoverWorktreesButton").Click();
        WaitUntil(() => Element("WorktreeNotice").Text.Contains("2 registered locations", StringComparison.Ordinal));
        _driver!.FindElements(By.Name("linked-review")).First(item => item.Displayed).Click();
        Element("OpenWorktreeButton").Click();
        WaitUntil(() => Element("DiffSearch").Displayed && Element("DiffSearch").Enabled && Element("FileTitle").Text == "sample.txt");
        EnterText("DiffSearch", "linked change"); Element("FindNextButton").Click();
        WaitUntil(() => Element("OperationStatus").Text == "Match selected in diff");
        Element("WorkspacePickerButton").Click(); Element("DiscoverWorktreesButton").Click();
        WaitUntil(() => Element("WorktreeNotice").Text.Contains("2 registered locations", StringComparison.Ordinal));
        _driver.FindElements(By.Name(Path.GetFileName(_repository.Root))).First(item => item.Displayed).Click();
        Element("OpenWorktreeButton").Click();
        WaitUntil(() => Element("DiffSearch").Displayed && Element("DiffSearch").Enabled);
        EnterText("DiffSearch", "changed"); Element("FindNextButton").Click();
        WaitUntil(() => Element("OperationStatus").Text == "Match selected in diff");
    }

    private void SelectCombo(string id, string option)
    {
        Element(id).Click();
        WaitUntil(() => _driver!.FindElements(By.Name(option)).Any(item => item.Displayed));
        _driver!.FindElements(By.Name(option)).First(item => item.Displayed).Click();
    }

    [Test]
    public void NativeDisplayOptionsApplyWithoutDiscardingTheSearchOrFileSelection()
    {
        EnterText("DiffSearch", "changed"); Element("FindNextButton").Click();
        WaitUntil(() => Element("OperationStatus").Text == "Match selected in diff");
        Element("DiffOptionsButton").Click();
        Element("CodeColorsToggle").Click(); Element("InlineChangesToggle").Click(); Element("ApplyDiffOptionsButton").Click();
        WaitUntil(() => Element("DiffSearch").Enabled);
        Element("DiffSearch").GetAttribute("Value").Test().Be("changed");
        Element("FileTitle").Text.Test().Be("sample.txt");
        Element("DiffOptionsButton").Click(); Element("ResetDiffOptionsButton").Click(); Element("ApplyDiffOptionsButton").Click();
        Element("DiffSearch").GetAttribute("Value").Test().Be("changed");
    }

    [Test]
    public void NativeFullContextExpandsAndContractsTheCurrentComparison()
    {
        Element("DiffOptionsButton").Click(); Element("FullContextToggle").Click();
        WaitUntil(() => !Element("ContextLinesInput").Enabled);
        Element("ApplyDiffOptionsButton").Click();
        WaitUntil(() => Element("DiffSearch").Enabled && Element("ComparisonDescription").Text.Contains("Full file context", StringComparison.Ordinal));
        EnterText("DiffSearch", "first"); Element("FindNextButton").Click();
        WaitUntil(() => Element("OperationStatus").Text == "Match selected in diff");
        Element("DiffOptionsButton").Click(); Element("FullContextToggle").Click();
        WaitUntil(() => Element("ContextLinesInput").Enabled);
        Element("ApplyDiffOptionsButton").Click();
        WaitUntil(() => Element("DiffSearch").Enabled && Element("ComparisonDescription").Text.Contains("Context 3", StringComparison.Ordinal));
        Element("DiffSearch").GetAttribute("Value").Test().Be("first"); Element("FileTitle").Text.Test().Be("sample.txt");
    }

    [Test]
    public void NativeCodePeersExposeReadOnlyValuesForUnifiedAndSplitViews()
    {
        var unified = Element("UnifiedDiffText");
        var text = unified.GetAttribute("Value");
        text.Contains("+changed", StringComparison.Ordinal).Test().BeTrue();
        unified.GetAttribute("Value.IsReadOnly").Equals("true", StringComparison.OrdinalIgnoreCase).Test().BeTrue();
        unified.GetAttribute("Name").Contains("sample.txt", StringComparison.Ordinal).Test().BeTrue();
        Element("SplitModeButton").Click();
        WaitUntil(() => Element("BeforeDiffText").Displayed && Element("AfterDiffText").Displayed);
        Element("BeforeDiffText").GetAttribute("Value").Contains("-historical", StringComparison.Ordinal).Test().BeTrue();
        Element("AfterDiffText").GetAttribute("Value").Contains("+changed", StringComparison.Ordinal).Test().BeTrue();
        Element("AfterDiffText").GetAttribute("Value.IsReadOnly").Equals("true", StringComparison.OrdinalIgnoreCase).Test().BeTrue();
    }

    [Test]
    public void NativeKeyboardMovesRegionsAndKeepsOptionDraftFocusInsideItsPanel()
    {
        Element("DiffSearch").SendKeys(Keys.F6);
        WaitUntil(() => Element("UnifiedDiffText").GetAttribute("HasKeyboardFocus").Equals("true", StringComparison.OrdinalIgnoreCase));
        Element("UnifiedDiffText").SendKeys(Keys.F6);
        WaitUntil(() => Element("ReduceMotionToggle").GetAttribute("HasKeyboardFocus").Equals("true", StringComparison.OrdinalIgnoreCase));
        Element("DiffOptionsButton").Click();
        Element("ContextLinesInput").SendKeys(string.Concat(Enumerable.Repeat(Keys.Tab, 16)));
        var focused = _driver!.SwitchTo().ActiveElement();
        new[] { "FullContextToggle", "ContextLinesInput", "WhitespaceSelector", "CodeColorsToggle", "InlineChangesToggle", "ResetDiffOptionsButton", "CancelDiffOptionsButton", "ApplyDiffOptionsButton" }
            .Contains(focused.GetAttribute("AutomationId"), StringComparer.Ordinal).Test().BeTrue();
        focused.SendKeys(Keys.Escape);
        WaitUntil(() => Element("DiffOptionsButton").GetAttribute("HasKeyboardFocus").Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    private void EnterText(string id, string value)
    {
        var input = Element(id); input.Click(); input.Clear(); input.SendKeys(value);
        WaitUntil(() => input.GetAttribute("Value") == value);
    }

    private static void WaitUntil(Func<bool> predicate)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(20))
        {
            try { if (predicate()) return; }
            catch (WebDriverException) { }
            Thread.Sleep(100);
        }
        throw new TimeoutException("The native workspace did not reach the expected accessible state.");
    }
}
