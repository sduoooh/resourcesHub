using System;
using System.Windows;

namespace ResourceRouter.App.State;

public sealed class EdgeBarLayoutPolicy
{
    private readonly EdgeBarLayoutTokens _tokens;

    public EdgeBarLayoutPolicy(EdgeBarLayoutTokens tokens)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    public EdgeBarHostLayout ComputeHostLayout(Rect workArea, bool panelOpen, double hostHeight)
    {
        var hostWidth = panelOpen ? _tokens.ExpandedHostWidthDip : _tokens.CollapsedHostWidthDip;
        var sensorAlignment = panelOpen ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        var sensorOpacity = panelOpen ? _tokens.ExpandedSensorOpacity : _tokens.CollapsedSensorOpacity;

        return new EdgeBarHostLayout
        {
            HostWidthDip = hostWidth,
            HostHeightDip = hostHeight,
            HostLeftDip = workArea.Right - hostWidth,
            SensorStripWidthDip = _tokens.SensorStripWidthDip,
            SensorAlignment = sensorAlignment,
            SensorOpacity = sensorOpacity
        };
    }

    public double ComputeInitialTop(Rect workArea, double windowHeight)
    {
        var available = workArea.Height - windowHeight;
        return workArea.Top + Math.Max(0, available * 0.5);
    }

    public Point ComputePopupAbsolutePosition(
        Rect workArea,
        Rect windowBounds,
        Rect sensorBoundsInWindow,
        Size popupSize)
    {
        if (popupSize.Width <= 0 || popupSize.Height <= 0)
        {
            return new Point(windowBounds.Left, windowBounds.Top);
        }

        var sideGap = Math.Max(0, (windowBounds.Width - sensorBoundsInWindow.Width) * 0.5);
        var anchorX = windowBounds.Left + sensorBoundsInWindow.Left - sideGap;
        
        // Directly calculate the Top-Left coordinate of the popup
        var desiredScreenLeft = anchorX - popupSize.Width;
        var desiredScreenTop = windowBounds.Top;

        var minLeft = workArea.Left;
        var maxLeft = Math.Max(minLeft, workArea.Right - popupSize.Width);
        var minTop = workArea.Top;
        var maxTop = Math.Max(minTop, workArea.Bottom - popupSize.Height);

        var clampedLeft = Math.Clamp(desiredScreenLeft, minLeft, maxLeft);
        var clampedTop = Math.Clamp(desiredScreenTop, minTop, maxTop);

        return new Point(clampedLeft, clampedTop);
    }
}

public sealed class EdgeBarHostLayout
{
    public double HostWidthDip { get; init; }

    public double HostHeightDip { get; init; }

    public double HostLeftDip { get; init; }

    public double SensorStripWidthDip { get; init; }

    public HorizontalAlignment SensorAlignment { get; init; }

    public double SensorOpacity { get; init; }
}