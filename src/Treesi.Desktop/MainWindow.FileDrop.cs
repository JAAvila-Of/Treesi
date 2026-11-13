using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Treesi.Desktop;

public sealed partial class MainWindow
{
    static MainWindow()
    {
        // File drops must be intercepted before editor instance handlers consume or alter their effects.
        // Class handlers capture no window and are scoped to controls belonging to a Treesi workspace.
        DragDrop.DragEnterEvent.AddClassHandler<Control>((control, args) =>
        { if (!args.Handled && TopLevel.GetTopLevel(control) is MainWindow window) window.PreviewFileDrop(control, args); });
        DragDrop.DragOverEvent.AddClassHandler<Control>((control, args) =>
        { if (!args.Handled && TopLevel.GetTopLevel(control) is MainWindow window) window.PreviewFileDrop(control, args); });
        DragDrop.DropEvent.AddClassHandler<Control>((control, args) =>
        { if (!args.Handled && TopLevel.GetTopLevel(control) is MainWindow window) window.OpenFileDrop(control, args); });
    }

    private void InitializeFileDrop()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => Required<Control>("FileDropIndicator").IsVisible = false, RoutingStrategies.Bubble, handledEventsToo: true);
        Deactivated += (_, _) => Required<Control>("FileDropIndicator").IsVisible = false;
    }

    private bool FileDropBlocked => _closed || !IsEffectivelyEnabled
        || ActiveReviewPanel is { } panel && panel != Required<Control>("OpenRecoveryNotice");

    private static bool? HasFileDropFormat(DragEventArgs args)
    {
        try { return args.DataTransfer.Formats.Count > 32 ? null : args.DataTransfer.Contains(DataFormat.File); }
        catch (Exception error) when (IsDropProviderError(error)) { return null; }
    }

    private static bool IsDropProviderError(Exception error) => error is IOException or UnauthorizedAccessException
        or ArgumentException or InvalidOperationException or NotSupportedException or COMException;

    private void PreviewFileDrop(object? sender, DragEventArgs args)
    {
        var fileFormat = HasFileDropFormat(args);
        var allowed = fileFormat == true && !FileDropBlocked && (args.DragEffects & DragDropEffects.Copy) != 0;
        Required<Control>("FileDropIndicator").IsVisible = allowed;
        if (fileFormat == false) return;
        args.DragEffects = allowed ? DragDropEffects.Copy : DragDropEffects.None;
        args.Handled = true;
    }

    private void OpenFileDrop(object? sender, DragEventArgs args)
    {
        Required<Control>("FileDropIndicator").IsVisible = false;
        var fileFormat = HasFileDropFormat(args);
        if (fileFormat == false) return;
        var copyAllowed = (args.DragEffects & DragDropEffects.Copy) != 0;
        args.Handled = true;
        args.DragEffects = DragDropEffects.None;
        if (FileDropBlocked) { ShowDropNotice("Close the open panel before dropping a file or folder."); return; }
        if (fileFormat is null || !copyAllowed) { ShowDropNotice("Drop one local file or folder to open a read-only review."); return; }
        try
        {
            // Resolve the borrowed payload synchronously; retain only the path across asynchronous inspection.
            var items = args.DataTransfer.Items;
            if (items.Count != 1) { ShowDropNotice("Drop one file or folder at a time."); return; }
            var item = items[0].TryGetFile();
            var path = item?.TryGetLocalPath();
            if (path is null || path.Length > 4096 || path.Contains('\0') || !Path.IsPathFullyQualified(path) || item!.Path.IsUnc)
            { ShowDropNotice("Only local file-system files and folders can be opened by dropping."); return; }
            args.DragEffects = DragDropEffects.Copy;
            PendingInspection = OpenRepositoryAsync(path);
        }
        catch (Exception error) when (IsDropProviderError(error))
        { ShowDropNotice("The dropped item is no longer available. Try again or use Open file."); }
    }

    private void ShowDropNotice(string message)
    {
        _status.Text = message;
        if (Required<Control>("HomeSurface").IsVisible) ShowHomeNotice(message);
    }
}
