using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Abstractions;

public interface IResourceMetadataFacetPolicy
{
    ResourceMetadataFacet Read(Resource resource);

    bool Apply(Resource resource, ResourceMetadataFacet nextFacet);
}