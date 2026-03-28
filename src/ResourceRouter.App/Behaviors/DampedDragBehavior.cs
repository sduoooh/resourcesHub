using System;
using System.Windows;
using System.Windows.Media;

namespace ResourceRouter.App.Behaviors;

public sealed class DampedDragBehavior : IDisposable
{
    private Window? _window;
    private bool _isDragging;
    private double _mouseOffsetY;
    private double _targetTop;
    private double _velocity;

    public double Stiffness { get; set; } = 0.2;

    public double Damping { get; set; } = 0.75;

    public void Attach(Window window)
    {
        _window = window;
        CompositionTarget.Rendering += OnRendering;
    }

    public void BeginDrag(Point mouseScreenPoint)
    {
        if (_window is null)
        {
            return;
        }

        _isDragging = true;
        _mouseOffsetY = mouseScreenPoint.Y - _window.Top;
        _targetTop = _window.Top;
        _velocity = 0;
    }

    public void UpdateTarget(Point mouseScreenPoint)
    {
        if (!_isDragging)
        {
            return;
        }

        _targetTop = mouseScreenPoint.Y - _mouseOffsetY;
    }

    public void EndDrag()
    {
        _isDragging = false;
        _velocity = 0;
    }

    public void Dispose()
    {
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_window is null)
        {
            return;
        }

        if (!_isDragging && Math.Abs(_velocity) < 0.01)
        {
            return;
        }

        var force = (_targetTop - _window.Top) * Stiffness;
        _velocity = (_velocity + force) * Damping;

        var nextTop = ClampToWorkArea(_window.Top + _velocity, _window.Height);
        _window.Top = nextTop;
    }

    private static double ClampToWorkArea(double top, double windowHeight)
    {
        var workAreaTop = SystemParameters.WorkArea.Top;
        var workAreaBottom = SystemParameters.WorkArea.Bottom - windowHeight;
        return Math.Clamp(top, workAreaTop, Math.Max(workAreaTop, workAreaBottom));
    }
}