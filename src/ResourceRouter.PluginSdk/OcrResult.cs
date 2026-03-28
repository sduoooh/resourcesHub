namespace ResourceRouter.PluginSdk;

public sealed class OcrResult
{
    public bool Success { get; init; }

    public string Text { get; init; } = string.Empty;

    public string Engine { get; init; } = string.Empty;

    public string? ErrorMessage { get; init; }
}