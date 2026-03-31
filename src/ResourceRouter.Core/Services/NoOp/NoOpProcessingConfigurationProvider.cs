using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;
using ResourceRouter.PluginSdk;

namespace ResourceRouter.Core.Services.NoOp;

public sealed class NoOpProcessingConfigurationProvider : IProcessingConfigurationProvider
{
    public IProcessingCapabilityApi CapabilityApi { get; } = new NoOpProcessingCapabilityApi();

    public ProcessingConfigurationSnapshot Resolve(Resource resource, IFormatConverter? converter)
    {
        return new ProcessingConfigurationSnapshot
        {
            EnableOcr = false,
            EnableAudioTranscription = false,
            CapabilityApi = CapabilityApi,
            PluginOptions = new Dictionary<string, string>()
        };
    }
}

public sealed class NoOpProcessingCapabilityApi : IProcessingCapabilityApi
{
    public Task<OcrResult> RunOcrAsync(string filePath, CancellationToken cancellationToken = default) => Task.FromResult(new OcrResult { Success = false, ErrorMessage = "" });
    public Task<AudioTranscriptionResult> RunAudioTranscriptionAsync(string filePath, CancellationToken cancellationToken = default) => Task.FromResult(new AudioTranscriptionResult { Success = false, ErrorMessage = "" });
    public Task<FeatureSubmissionResult> SubmitStructuredFeaturesAsync(StructuredFeatureSet featureSet, CancellationToken cancellationToken = default) => Task.FromResult(new FeatureSubmissionResult { Success = false, ErrorMessage = "" });
    public Task<TagMutationResult> AddTagAsync(Guid resourceId, string tag, TagCategory category, CancellationToken cancellationToken = default) =>
        Task.FromResult(new TagMutationResult { Success = false, Status = "disabled", ErrorMessage = "Capability API is disabled." });

    public Task<TagMutationResult> RemoveTagAsync(Guid resourceId, string tag, TagCategory? category = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new TagMutationResult { Success = false, Status = "disabled", ErrorMessage = "Capability API is disabled." });
}
