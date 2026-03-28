using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Abstractions;

public interface IResourceGovernanceProvider
{
    ResourceGovernancePolicy GetPolicy(RawDropData dropData, ResourceSource source);

    ResourceGovernancePolicy GetPolicy(Resource resource);
}
