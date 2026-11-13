using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Treesi.Desktop.Appearance;

namespace Treesi.Desktop;

public sealed partial class GraphicsSettingsWindow : Window
{
    public bool PreferenceSaved { get; private set; }

    public GraphicsSettingsWindow(GraphicsPreferences preferences)
    {
        AvaloniaXamlLoader.Load(this);
        var selector = this.FindControl<ComboBox>("GraphicsModeSelector")!;
        selector.SelectedIndex = (int)preferences.Saved;
        this.FindControl<TextBlock>("GraphicsCurrentMode")!.Text = "Current session: " +
            (preferences.Startup == GraphicsMode.Software ? "Software compatibility" : "Automatic");
        this.FindControl<Button>("CancelGraphicsSettingsButton")!.Click += (_, _) => Close();
        this.FindControl<Button>("SaveGraphicsSettingsButton")!.Click += (_, _) =>
        {
            if (selector.SelectedIndex is < 0 or > 1 || !preferences.Save((GraphicsMode)selector.SelectedIndex))
            { this.FindControl<TextBlock>("GraphicsSaveError")!.IsVisible = true; return; }
            PreferenceSaved = true;
            Close();
        };
        KeyDown += (_, args) => { if (args.Key == Key.Escape) { args.Handled = true; Close(); } };
        Opened += (_, _) => selector.Focus(NavigationMethod.Tab);
    }
}
