using System;
using System.Threading;
using System.Threading.Tasks;

namespace ResourceRouter.PluginSdk;

public interface IProcessingCapabilityApi
{
    Task<OcrResult> RunOcrAsync(string filePath, CancellationToken cancellationToken = default);

    Task<AudioTranscriptionResult> RunAudioTranscriptionAsync(string filePath, CancellationToken cancellationToken = default);

    Task<FeatureSubmissionResult> SubmitStructuredFeaturesAsync(
        StructuredFeatureSet featureSet,
        CancellationToken cancellationToken = default);

    Task<TagMutationResult> AddTagAsync(
        Guid resourceId,
        string tag,
        TagCategory category,
        CancellationToken cancellationToken = default);

    Task<TagMutationResult> RemoveTagAsync(
        Guid resourceId,
        string tag,
        TagCategory? category = null,
        CancellationToken cancellationToken = default);
}