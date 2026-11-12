using Avalonia.Automation;
using Avalonia.Controls;
using Treesi.Domain;

namespace Treesi.Desktop;

public sealed partial class MainWindow
{
    private DiffOptions _diffOptions = DiffOptions.Default;

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
        Required<Button>("ResetDiffOptionsButton").Click += (_, _) => SetOptionsDraft(DiffOptions.Default);
        Required<Button>("ApplyDiffOptionsButton").Click += (_, _) => ApplyComparisonOptions();
        UpdateOptionsIndicator();
    }

    private void SetOptionsDraft(DiffOptions options)
    {
        Required<NumericUpDown>("ContextLinesInput").Value = options.ContextLines;
        Required<ComboBox>("WhitespaceSelector").SelectedIndex = (int)options.Whitespace;
        Required<Control>("DiffOptionsError").IsVisible = false;
    }

    private void OpenComparisonOptions()
    {
        SetOptionsDraft(_diffOptions);
        Required<Control>("DiffOptionsPanel").IsVisible = true;
        Required<NumericUpDown>("ContextLinesInput").Focus();
        _motion.Reveal(Required<Control>("DiffOptionsPanel"));
    }

    private void CloseComparisonOptions(bool focus = true)
    {
        Required<Control>("DiffOptionsPanel").IsVisible = false;
        if (focus) Required<Button>("DiffOptionsButton").Focus();
    }

    private void ApplyComparisonOptions()
    {
        var value = Required<NumericUpDown>("ContextLinesInput").Value;
        var whitespace = (WhitespaceMode)Required<ComboBox>("WhitespaceSelector").SelectedIndex;
        if (value is null || value < 0 || value > DiffOptions.MaximumContextLines || value != decimal.Truncate(value.Value) || !Enum.IsDefined(whitespace))
        {
            var error = Required<TextBlock>("DiffOptionsError");
            error.Text = "Choose a whole context value from 0 to 50 and a whitespace mode."; error.IsVisible = true;
            return;
        }
        var options = new DiffOptions(decimal.ToInt32(value.Value), whitespace);
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
        var notice = Required<Control>("WhitespaceNotice");
        notice.IsVisible = filtered;
        ToolTip.SetTip(notice, description + " Differences may be hidden; return to Exact to verify.");
        AutomationProperties.SetName(notice, description + " Differences may be hidden.");
        ToolTip.SetTip(Required<Button>("DiffOptionsButton"), description);
    }
}
