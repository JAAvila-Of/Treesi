using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace Treesi.Desktop;

public sealed partial class MainWindow
{
    private bool _filesPanelVisible = true;
    private bool _focusMode;

    private void InitializePresentation()
    {
        Required<Button>("ToggleFilesButton").Click += (_, _) => ToggleFilesPanel();
        Required<Button>("FocusWorkspaceButton").Click += (_, _) => SetFocusMode(!_focusMode);
        SizeChanged += (_, _) => ApplyPresentation();
        ApplyPresentation();
    }

    private void ToggleFilesPanel()
    {
        if (_focusMode)
        {
            _focusMode = false;
            _filesPanelVisible = true;
        }
        else _filesPanelVisible = !_filesPanelVisible;
        ApplyPresentation();
    }

    private void SetFocusMode(bool enabled)
    {
        _focusMode = enabled;
        ApplyPresentation();
    }

    private void ApplyPresentation()
    {
        var panel = Required<Control>("FilesPanel");
        var header = Required<Control>("WorkspaceHeader");
        var moveFocus = (_focusMode && header.IsKeyboardFocusWithin)
            || ((!_filesPanelVisible || _focusMode) && (panel.IsKeyboardFocusWithin || _graphVisible && Required<Control>("GraphSidebar").IsKeyboardFocusWithin));
        var visible = _filesPanelVisible && !_focusMode;
        panel.IsVisible = visible;
        Required<Control>("FilesSeparator").IsVisible = visible;
        var columns = Required<Grid>("WorkspaceColumns").ColumnDefinitions;
        var sidebarWidth = (ClientSize.Width > 0 ? ClientSize.Width : Width) < 1000 ? 180 : 210;
        columns[0].Width = new GridLength(visible ? sidebarWidth : 0);
        columns[1].Width = new GridLength(visible ? 1 : 0);
        Required<Grid>("RepositoryTabBar").ColumnDefinitions[0].Width = new GridLength(sidebarWidth);
        Required<Grid>("WorkspaceHeader").ColumnDefinitions[0].Width = new GridLength(sidebarWidth);
        var graphColumns = Required<Grid>("GraphColumns").ColumnDefinitions;
        graphColumns[0].Width = new GridLength(visible ? sidebarWidth : 0); graphColumns[1].Width = new GridLength(visible ? 1 : 0);
        Required<Control>("GraphSidebar").IsVisible = visible;
        Required<Control>("GraphSeparator").IsVisible = visible;
        header.IsVisible = !_focusMode;
        var focusButton = Required<Button>("FocusWorkspaceButton");
        focusButton.Content = _focusMode ? "Exit focus" : "Focus";
        AutomationProperties.SetName(focusButton, _focusMode ? "Exit focus mode" : "Enter focus mode");
        AutomationProperties.SetName(Required<Button>("ToggleFilesButton"), visible ? "Hide file panel" : "Show file panel");
        if (moveFocus)
        {
            if (_graphVisible) ListRegion(Required<ListBox>("GraphCommits"), Required<TextBox>("GraphStartInput")).Focus();
            else if (!ActiveEditor.TextArea.Focus()) Required<Button>("ToggleFilesButton").Focus();
        }
    }
}
