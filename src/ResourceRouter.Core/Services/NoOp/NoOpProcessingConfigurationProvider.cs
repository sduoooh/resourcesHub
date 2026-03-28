using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.PluginSdk;

namespace ResourceRouter.Core.Services.NoOp;

public sealed class NoOpProcessingConfigurationProvider : IProcessingConfigurationProvider
{
    public bool EnableOcr => false;
    public bool EnableAudioTranscription => false;
    public IProcessingCapabilityApi CapabilityApi { get; } = new NoOpProcessingCapabilityApi();
    public IReadOnlyDictionary<string, string> GetPluginOptions(string converterName, string mimeType) => new Dictionary<string, string>();
}

public sealed class NoOpProcessingCapabilityApi : IProcessingCapabilityApi
{
    public Task<OcrResult> RunOcrAsync(string filePath, CancellationToken cancellationToken = default) => Task.FromResult(new OcrResult { Success = false, ErrorMessage = "" });
    public Task<AudioTranscriptionResult> RunAudioTranscriptionAsync(string filePath, CancellationToken cancellationToken = default) => Task.FromResult(new AudioTranscriptionResult { Success = false, ErrorMessage = "" });
    public Task<FeatureSubmissionResult> SubmitStructuredFeaturesAsync(StructuredFeatureSet featureSet, CancellationToken cancellationToken = default) => Task.FromResult(new FeatureSubmissionResult { Success = false, ErrorMessage = "" });
}
