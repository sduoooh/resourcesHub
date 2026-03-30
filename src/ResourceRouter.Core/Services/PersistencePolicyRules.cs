using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Services;

public static class PersistencePolicyRules
{
    public static bool ShouldForceUnified(RawDropKind rawKind)
    {
        return rawKind != RawDropKind.File;
    }

    public static bool CanConfigurePolicy(Resource resource)
    {
        return CanUsePolicy(resource, PersistencePolicy.InPlace)
               || CanUsePolicy(resource, PersistencePolicy.Backup);
    }

    public static bool CanUsePolicy(Resource resource, PersistencePolicy policy)
    {
        return policy switch
        {
            PersistencePolicy.Unified => true,
            PersistencePolicy.InPlace => !string.IsNullOrWhiteSpace(resource.SourceUri),
            PersistencePolicy.Backup => !string.IsNullOrWhiteSpace(resource.SourceUri),
            _ => false
        };
    }
}