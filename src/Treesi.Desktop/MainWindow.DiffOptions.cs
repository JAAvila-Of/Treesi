using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Treesi.Domain;

namespace Treesi.Desktop;

public sealed partial class MainWindow
{
    private DiffOptions _diffOptions = DiffOptions.Default;
    private DiffViewOptions _viewOptions = new();
    private DiffPresentation? _presentation;

    private static string DescribeWhitespace(WhitespaceMode mode) => mode switch
    {
        WhitespaceMode.IgnoreTrailingSpace => "Ignore trailing space",
        WhitespaceMode.IgnoreSpaceChange => "Ignore space changes",
        WhitespaceMode.IgnoreAllSpace => "Ignore all space",
        _ => "Exact"
    };

    private void InitializeDiffOptions()
    {
        Required<ComboBox>("WhitespaceSelector").ItemsSource = Enum.GetValues<WhitespaceMode>().Select(DescribeWhitespace).ToArray();
        Required<Button>("DiffOptionsButton").Click += (_, _) => OpenComparisonOptions();
        Required<Button>("CancelDiffOptionsButton").Click += (_, _) => CloseComparisonOptions();
        Required<Button>("ResetDiffOptionsButton").Click += (_, _) => SetOptionsDraft(DiffOptions.Default, new());
        Required<Button>("ApplyDiffOptionsButton").Click += (_, _) => ApplyComparisonOptions();
        UpdateOptionsIndicator();
    }

    private void SetOptionsDraft(DiffOptions options, DiffViewOptions? view = null)
    {
        Required<TextBox>("ContextLinesInput").Text = options.ContextLines.ToString(CultureInfo.InvariantCulture);
        Required<ComboBox>("WhitespaceSelector").SelectedIndex = (int)options.Whitespace;
        Required<Control>("DiffOptionsError").IsVisible = false;
        Required<CheckBox>("CodeColorsToggle").IsChecked = (view ?? _viewOptions).CodeColors;
        Required<CheckBox>("InlineChangesToggle").IsChecked = (view ?? _viewOptions).InlineChanges;
    }

    private void OpenComparisonOptions()
    {
        Required<Control>("CommitDetailsPanel").IsVisible = false;
        if (Required<Control>("RevisionPanel").IsVisible) CloseRevisionPicker(true, false);
        SetOptionsDraft(_diffOptions);
        Required<Control>("DiffOptionsPanel").IsVisible = true;
        var input = Required<TextBox>("ContextLinesInput");
        input.Focus(); input.SelectAll();
        _motion.Reveal(Required<Control>("DiffOptionsPanel"));
    }

    private void CloseComparisonOptions(bool focus = true)
    {
        Required<Control>("DiffOptionsPanel").IsVisible = false;
        if (focus) Required<Button>("DiffOptionsButton").Focus();
    }

    private void ApplyComparisonOptions()
    {
        var text = Required<TextBox>("ContextLinesInput").Text;
        var whitespace = (WhitespaceMode)Required<ComboBox>("WhitespaceSelector").SelectedIndex;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > DiffOptions.MaximumContextLines || !Enum.IsDefined(whitespace))
        {
            var error = Required<TextBlock>("DiffOptionsError");
            error.Text = "Choose a whole context value from 0 to 50 and a whitespace mode."; error.IsVisible = true;
            return;
        }
        var options = new DiffOptions(value, whitespace);
        _viewOptions = new(Required<CheckBox>("CodeColorsToggle").IsChecked == true, Required<CheckBox>("InlineChangesToggle").IsChecked == true);
        _decoration.SetViewOptions(_viewOptions); _beforeDecoration.SetViewOptions(_viewOptions); _afterDecoration.SetViewOptions(_viewOptions);
        UpdateOptionsIndicator();
        CloseComparisonOptions();
        if (_diffOptions == options) return;
        _diffOptions = options; UpdateOptionsIndicator();
        PendingInspection = LoadDiffAsync();
    }

    private void ResetWorkspaceDiffOptions()
    {
        _diffOptions = DiffOptions.Default;
        UpdateOptionsIndicator();
    }

    private void UpdateOptionsIndicator()
    {
        var filtered = _diffOptions.Whitespace != WhitespaceMode.Exact;
        var description = $"Context: {_diffOptions.ContextLines} lines. Whitespace: {DescribeWhitespace(_diffOptions.Whitespace)}.";
        description += $" Code colors: {(_viewOptions.CodeColors ? "on" : "off")}. Inline changes: {(_viewOptions.InlineChanges ? "on" : "off")}.";
        var notice = Required<Control>("WhitespaceNotice");
        notice.IsVisible = filtered;
        ToolTip.SetTip(notice, description + " Differences may be hidden; return to Exact to verify.");
        AutomationProperties.SetName(Required<TextBlock>("WhitespaceWarningText"), description + " Differences may be hidden.");
        ToolTip.SetTip(Required<Button>("DiffOptionsButton"), description);
    }
}
