namespace ResourceRouter.PluginSdk;

public sealed class FeatureSubmissionResult
{
    public bool Success { get; init; }

    public string Status { get; init; } = string.Empty;

    public string? ErrorMessage { get; init; }
}
