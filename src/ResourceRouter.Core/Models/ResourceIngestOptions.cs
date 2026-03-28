namespace ResourceRouter.Core.Models;

public sealed class ResourceIngestOptions
{
    public PrivacyLevel Privacy { get; init; } = PrivacyLevel.Private;

    public SyncPolicy SyncPolicy { get; init; } = SyncPolicy.LocalOnly;

    public ModelType ProcessingModel { get; init; } = ModelType.LocalSmall;

    public ResourceSource Source { get; init; } = ResourceSource.Unknown;

    public string PermissionPresetId { get; init; } = PermissionPreset.PrivatePresetId;

    public string? UserTitle { get; init; }

    public PersistencePolicy PersistencePolicy { get; init; } = PersistencePolicy.InPlace;
}