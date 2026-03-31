using System;
using System.Windows;
using System.Windows.Media;
using ResourceRouter.App.State;

namespace ResourceRouter.App.Behaviors;

public sealed class DampedDragBehavior : IDisposable
{
    private Window? _window;
    private bool _isDragging;
    private double _mouseOffsetY;
    
    private double _targetTop;
    private double _effectiveTargetTop;
    private double _velocity;
    private double _lastDelta;
    private double _overrideStiffness = -1;
    private double _overrideDamping = -1;

    public Func<double>? MaxNormalTopProvider { get; set; }

    public double Stiffness { get; set; } = AppInteractionDefaults.DampedDrag.Stiffness;

    public double Damping { get; set; } = AppInteractionDefaults.DampedDrag.Damping;

    public void Attach(Window window)
    {
        _window = window;
        _effectiveTargetTop = window.Top;
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
        _effectiveTargetTop = _window.Top;
        _velocity = 0;
        _lastDelta = 0;
        _overrideStiffness = -1;
        _overrideDamping = -1;
    }

    public void UpdateTarget(Point mouseScreenPoint)
    {
        if (!_isDragging || _window is null)
        {
            return;
        }

        var unadjustedNewTargetTop = mouseScreenPoint.Y - _mouseOffsetY;
        var maxNormalTop = GetMaxNormalTop();

        var delta = unadjustedNewTargetTop - _targetTop;
        _lastDelta = delta;

        if (unadjustedNewTargetTop > maxNormalTop)
        {
            if (delta > 0)
            {
                var maxOvershoot = _window.Height / 2.0;
                var currentDepth = _effectiveTargetTop - maxNormalTop;
                var friction = currentDepth >= maxOvershoot ? 0.0 : (1.0 - (currentDepth / maxOvershoot));

                _effectiveTargetTop += delta * friction;
                if (_effectiveTargetTop > maxNormalTop + maxOvershoot)
                {
                    _effectiveTargetTop = maxNormalTop + maxOvershoot;
                }
            }
            else
            {
                _effectiveTargetTop += delta;
                if (_effectiveTargetTop < maxNormalTop)
                {
                    _effectiveTargetTop = maxNormalTop;
                }
            }

            _mouseOffsetY = mouseScreenPoint.Y - _effectiveTargetTop;
            _targetTop = _effectiveTargetTop;
        }
        else
        {
            _effectiveTargetTop = unadjustedNewTargetTop;
            _targetTop = unadjustedNewTargetTop;
        }
    }

    public void EndDrag()
    {
        _isDragging = false;
        
        if (_window != null)
        {
            var maxNormalTop = GetMaxNormalTop();
            if (_effectiveTargetTop > maxNormalTop)
            {
                _effectiveTargetTop = maxNormalTop;
                
                if (_lastDelta < 0)
                {
                    _overrideStiffness = 0.5;
                    _overrideDamping = 0.4;
                }
                else
                {
                    _overrideStiffness = -1; 
                    _overrideDamping = -1;
                }
            }
            else 
            {
                _overrideStiffness = -1; 
                _overrideDamping = -1;
            }
        }
    }

    public void SyncToWindowTop()
    {
        if (_window is null)
        {
            return;
        }

        _targetTop = _window.Top;
        _effectiveTargetTop = _window.Top;
        _velocity = 0;
        _lastDelta = 0;
        _overrideStiffness = -1;
        _overrideDamping = -1;
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

        if (!_isDragging && Math.Abs(_velocity) < 0.01 && Math.Abs(_effectiveTargetTop - _window.Top) < 0.5)
        {
            _overrideStiffness = -1;
            _overrideDamping = -1;
            _effectiveTargetTop = _window.Top;
            return;
        }

        var activeStiffness = _overrideStiffness > 0 ? _overrideStiffness : Stiffness;
        var activeDamping = _overrideDamping > 0 ? _overrideDamping : Damping;

        var force = (_effectiveTargetTop - _window.Top) * activeStiffness;
        _velocity = (_velocity + force) * activeDamping;

        var maxNormalTop = GetMaxNormalTop();
        var absoluteMax = maxNormalTop + (_window.Height / 2.0);

        var nextTop = ClampToWorkArea(_window.Top + _velocity, absoluteMax);
        _window.Top = nextTop;
    }

    private double GetMaxNormalTop()
    {
        return MaxNormalTopProvider?.Invoke() ?? (SystemParameters.WorkArea.Bottom - (_window?.Height ?? 0));
    }

    private static double ClampToWorkArea(double top, double absoluteMaxTop)
    {
        var workAreaTop = SystemParameters.WorkArea.Top;
        return Math.Clamp(top, workAreaTop, Math.Max(workAreaTop, absoluteMaxTop));
    }
}