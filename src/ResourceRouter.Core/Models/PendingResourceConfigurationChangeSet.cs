using ResourceRouter.Core.Abstractions;

namespace ResourceRouter.Core.Models;

public sealed class PendingResourceConfigurationChangeSet
{
    public required string PermissionPresetId { get; init; }

    public required PrivacyLevel Privacy { get; init; }

    public required SyncPolicy SyncPolicy { get; init; }

    public required ModelType ProcessingModel { get; init; }

    public required PersistencePolicy PersistencePolicy { get; init; }

    public string? ProcessedRouteId { get; init; }

    public required ResourceMetadataFacet MetadataFacet { get; init; }

    public static PendingResourceConfigurationChangeSet FromResource(
        Resource resource,
        IResourceMetadataFacetPolicy metadataFacetPolicy)
    {
        return new PendingResourceConfigurationChangeSet
        {
            PermissionPresetId = resource.PermissionPresetId,
            Privacy = resource.Privacy,
            SyncPolicy = resource.SyncPolicy,
            ProcessingModel = resource.ProcessingModel,
            PersistencePolicy = resource.PersistencePolicy,
            ProcessedRouteId = resource.ProcessedRouteId,
            MetadataFacet = metadataFacetPolicy.Read(resource)
        };
    }

    public void ApplyTo(Resource targetResource, IResourceMetadataFacetPolicy metadataFacetPolicy)
    {
        targetResource.PermissionPresetId = PermissionPresetId;
        targetResource.Privacy = Privacy;
        targetResource.SyncPolicy = SyncPolicy;
        targetResource.ProcessingModel = ProcessingModel;
        targetResource.PersistencePolicy = PersistencePolicy;
        targetResource.ProcessedRouteId = ProcessedRouteId;

        metadataFacetPolicy.Apply(targetResource, MetadataFacet);
    }
}