namespace ResourceRouter.PluginSdk;

public sealed class AudioTranscriptionResult
{
    public bool Success { get; init; }

    public string Transcript { get; init; } = string.Empty;

    public string Engine { get; init; } = string.Empty;

    public string? Language { get; init; }

    public string? ErrorMessage { get; init; }
}