using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Media;

namespace Treesi.Desktop.Appearance;

public static class DesktopStyling
{
    public static void Install(Avalonia.Application application)
    {
        var fluent = new FluentTheme();
        fluent.Palettes[ThemeVariant.Light] = new ColorPaletteResources { Accent = Color.Parse("#177B65") };
        fluent.Palettes[ThemeVariant.Dark] = new ColorPaletteResources { Accent = Color.Parse("#74DCC0") };
        application.Styles.Add(fluent);
        application.Styles.Add(new StyleInclude(new Uri("avares://Treesi.Desktop/"))
        { Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml") });
        application.Styles.Add(new StyleInclude(new Uri("avares://Treesi.Desktop/"))
        { Source = new Uri("avares://Treesi.Desktop/Appearance/WorkspaceStyles.axaml") });
    }
}
