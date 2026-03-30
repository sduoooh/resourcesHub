namespace ResourceRouter.App.State;

public sealed class EdgeBarLayoutTokens
{
    public double SensorStripWidthDip { get; init; } = EdgeBarLayoutDefaults.SensorStripWidthDip;

    public double CollapsedHostWidthDip { get; init; } = EdgeBarLayoutDefaults.CollapsedHostWidthDip;

    public double ExpandedHostWidthDip { get; init; } = EdgeBarLayoutDefaults.ExpandedHostWidthDip;

    public double CollapsedSensorOpacity { get; init; } = EdgeBarLayoutDefaults.CollapsedSensorOpacity;

    public double ExpandedSensorOpacity { get; init; } = EdgeBarLayoutDefaults.ExpandedSensorOpacity;
}