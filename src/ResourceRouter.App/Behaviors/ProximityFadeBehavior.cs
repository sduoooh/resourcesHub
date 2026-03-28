using System;
using System.Windows;
using System.Windows.Threading;
using ResourceRouter.App.Interop;

namespace ResourceRouter.App.Behaviors;

public sealed class ProximityFadeBehavior : IDisposable
{
    private readonly DispatcherTimer _timer;
    private Window? _window;

    public ProximityFadeBehavior()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += OnTick;
    }

    public double ActivationDistance { get; set; } = 100;

    public double MaxOpacity { get; set; } = 0.6;

    public event EventHandler<double>? OpacityChanged;

    public void Attach(Window window)
    {
        _window = window;
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_window is null)
        {
            return;
        }

        var cursor = Win32Helpers.GetCursorScreenPosition();
        var rightEdge = SystemParameters.WorkArea.Right;
        var distance = Math.Max(0, rightEdge - cursor.X);

        var ratio = Math.Clamp((ActivationDistance - distance) / ActivationDistance, 0, 1);
        var opacity = ratio * MaxOpacity;
        OpacityChanged?.Invoke(this, opacity);
    }
}