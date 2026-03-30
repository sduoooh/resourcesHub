namespace ResourceRouter.PluginSdk;

public sealed class TagMutationResult
{
    public bool Success { get; init; }

    public string Status { get; init; } = string.Empty;

    public string? ErrorMessage { get; init; }
}
