using System.Collections.Generic;
using ResourceRouter.PluginSdk;

namespace ResourceRouter.Core.Models;

public sealed class ProcessingConfigurationSnapshot
{
    public bool EnableOcr { get; init; }

    public bool EnableAudioTranscription { get; init; }

    public required IProcessingCapabilityApi CapabilityApi { get; init; }

    public IReadOnlyDictionary<string, string> PluginOptions { get; init; } =
        new Dictionary<string, string>();
}