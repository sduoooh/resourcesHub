using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ResourceRouter.App.Interop;
using ResourceRouter.App.State;

namespace ResourceRouter.App.Behaviors;

public sealed class ProximityFadeBehavior : IDisposable
{
    private readonly DispatcherTimer _timer;
    private Window? _window;

    public ProximityFadeBehavior()
    {
        _timer = new DispatcherTimer { Interval = AppInteractionDefaults.ProximityFade.TickInterval };
        _timer.Tick += OnTick;
    }

    public double ActivationDistance { get; set; } = AppInteractionDefaults.ProximityFade.ActivationDistanceDip;

    public double MaxOpacity { get; set; } = AppInteractionDefaults.ProximityFade.MaxOpacity;

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
        var cursorDipX = ToDipX(cursor.X);
        var rightEdge = SystemParameters.WorkArea.Right;
        var distance = Math.Max(0, rightEdge - cursorDipX);

        var ratio = Math.Clamp((ActivationDistance - distance) / ActivationDistance, 0, 1);
        var opacity = ratio * MaxOpacity;
        OpacityChanged?.Invoke(this, opacity);
    }

    private double ToDipX(double devicePixelX)
    {
        if (_window is null)
        {
            return devicePixelX;
        }

        var source = PresentationSource.FromVisual(_window);
        if (source?.CompositionTarget is null)
        {
            return devicePixelX;
        }

        var dipPoint = source.CompositionTarget.TransformFromDevice.Transform(new Point(devicePixelX, 0));
        return dipPoint.X;
    }
}