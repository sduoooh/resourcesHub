namespace ResourceRouter.Core.Models;

public sealed class ResourceGovernancePolicy
{
    public bool EnableHealthMonitoring { get; init; }

    public bool EnableFeatureization { get; init; }

    public bool EnableDedup { get; init; }

    public bool EnableRemote { get; init; }
}
