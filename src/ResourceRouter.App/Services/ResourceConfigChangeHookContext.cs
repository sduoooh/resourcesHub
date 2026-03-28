using ResourceRouter.Core.Models;

namespace ResourceRouter.App.Services;

public sealed class ResourceConfigChangeHookContext
{
    public required Resource Resource { get; init; }

    public required PrivacyLevel PreviousPrivacy { get; init; }

    public required SyncPolicy PreviousSyncPolicy { get; init; }

    public required ModelType PreviousProcessingModel { get; init; }

    public required string PreviousPermissionPresetId { get; init; }

    public required PersistencePolicy PreviousPersistencePolicy { get; init; }

    public bool SyncPolicyChanged => PreviousSyncPolicy != Resource.SyncPolicy;

    public bool RequiresCloudUpload =>
        SyncPolicyChanged &&
        Resource.SyncPolicy == SyncPolicy.CloudDefault &&
        Resource.State == ResourceState.Ready;

    public bool RequiresCloudDeleteHint =>
        SyncPolicyChanged &&
        PreviousSyncPolicy == SyncPolicy.CloudDefault &&
        Resource.SyncPolicy != SyncPolicy.CloudDefault;
}
