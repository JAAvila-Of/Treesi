using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Treesi.Desktop.Appearance;

public sealed class WorkspaceMotion(Control owner) : IDisposable
{
    private CancellationTokenSource? _running;
    private bool _reduced, _disposed;
    public Task Pending { get; private set; } = Task.CompletedTask;
    public bool Reduced
    {
        get => _reduced;
        set
        {
            _reduced = value;
            owner.Classes.Set("motion-enabled", !value && !_disposed);
            if (value) Stop();
        }
    }

    public void Reveal(Control target)
    {
        Stop();
        if (_reduced || _disposed || !target.IsVisible) return;
        var cancellation = _running = new CancellationTokenSource();
        Pending = AnimateAsync(target, cancellation);
    }

    private static async Task AnimateAsync(Control target, CancellationTokenSource cancellation)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(180),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters =
                {
                    new Setter(Visual.OpacityProperty, 0.88d),
                    new Setter(TranslateTransform.YProperty, 4d)
                } },
                new KeyFrame { Cue = new Cue(1), Setters =
                {
                    new Setter(Visual.OpacityProperty, 1d),
                    new Setter(TranslateTransform.YProperty, 0d)
                } }
            }
        };
        try { await animation.RunAsync(target, cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    public void Stop()
    {
        _running?.Cancel(); _running?.Dispose(); _running = null;
    }

    public void Dispose() { _disposed = true; Stop(); owner.Classes.Remove("motion-enabled"); }
}
