using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Services.NoOp;

public sealed class NoOpResourceGovernanceProvider : IResourceGovernanceProvider
{
    public ResourceGovernancePolicy GetPolicy(RawDropData dropData, ResourceSource source)
    {
        return new ResourceGovernancePolicy 
        { 
            EnableHealthMonitoring = true,
            EnableFeatureization = true,
            EnableDedup = true,
            EnableRemote = true
        };
    }

    public ResourceGovernancePolicy GetPolicy(Resource resource)
    {
        return new ResourceGovernancePolicy 
        { 
            EnableHealthMonitoring = true,
            EnableFeatureization = true,
            EnableDedup = true,
            EnableRemote = true
        };
    }
}
