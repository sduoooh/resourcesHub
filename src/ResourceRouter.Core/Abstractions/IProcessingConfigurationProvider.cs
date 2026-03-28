using System.Collections.Generic;
using ResourceRouter.PluginSdk;

namespace ResourceRouter.Core.Abstractions;

public interface IProcessingConfigurationProvider
{
    bool EnableOcr { get; }

    bool EnableAudioTranscription { get; }

    IProcessingCapabilityApi CapabilityApi { get; }

    IReadOnlyDictionary<string, string> GetPluginOptions(string converterName, string mimeType);
}