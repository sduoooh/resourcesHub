using System;
using System.Collections.Generic;

namespace ResourceRouter.PluginSdk;

public sealed class ConvertOptions
{
    public Guid ResourceId { get; init; }

    public string OutputDirectory { get; init; } = string.Empty;

    public bool PreferTextOutput { get; init; } = true;

    public bool EnableOcr { get; init; }

    public bool EnableAudioTranscription { get; init; }

    public IProcessingCapabilityApi? CapabilityApi { get; init; }

    public IReadOnlyDictionary<string, string> PluginOptions { get; init; } = new Dictionary<string, string>();
}