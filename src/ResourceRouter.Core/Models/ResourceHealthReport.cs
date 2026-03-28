namespace ResourceRouter.Core.Models;

public sealed class ResourceHealthReport
{
    public bool IsHealthy { get; init; } = true;

    public string? Message { get; init; }
}
