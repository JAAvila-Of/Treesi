using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Treesi.Desktop.Appearance;

namespace Treesi.RenderingLab;

internal sealed class DragSourceApplication : Avalonia.Application
{
    public override void Initialize() => DesktopStyling.Install(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            IStorageFile? file = null;
            var button = new Button { Content = "Drag fixture file", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            AutomationProperties.SetAutomationId(button, "DragFixtureFile");
            var status = new TextBlock { Text = "Preparing fixture" };
            AutomationProperties.SetAutomationId(status, "DragSourceStatus");
            var grid = new Grid { RowDefinitions = new("Auto,*"), Margin = new Thickness(16), RowSpacing = 12 };
            Grid.SetRow(button, 1); grid.Children.Add(status); grid.Children.Add(button);
            var window = new Window { Title = "Treesi isolated drag source", Width = 260, Height = 200, CanResize = false, Content = grid };
            button.AddHandler(InputElement.PointerPressedEvent, async (_, args) =>
            {
                if (file is null || !args.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
                var data = new DataTransfer(); data.Add(DataTransferItem.CreateFile(file));
                var effect = await DragDrop.DoDragDropAsync(args, data, DragDropEffects.Copy);
                status.Text = effect == DragDropEffects.Copy ? "Review requested" : "Drop cancelled";
            }, RoutingStrategies.Bubble, handledEventsToo: true);
            window.Opened += async (_, _) =>
            {
                file = await window.StorageProvider.TryGetFileFromPathAsync(desktop.Args![0]);
                button.IsEnabled = file is not null;
                status.Text = file is null ? "Fixture unavailable" : "Ready";
            };
            window.Closed += (_, _) => file?.Dispose();
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
