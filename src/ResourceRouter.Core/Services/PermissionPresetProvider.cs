using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Services;

public static class PermissionPresetProvider
{
    public static PermissionPreset Resolve(string? presetId)
    {
        if (!string.IsNullOrWhiteSpace(presetId) && PermissionPreset.BuiltIn.TryGetValue(presetId, out var preset))
        {
            return preset;
        }

        return PermissionPreset.BuiltIn[PermissionPreset.PrivatePresetId];
    }

    public static ResourceIngestOptions ToIngestOptions(PermissionPreset preset, ResourceSource source)
    {
        return new ResourceIngestOptions
        {
            PermissionPresetId = preset.Id,
            Privacy = preset.Privacy,
            SyncPolicy = preset.SyncPolicy,
            ProcessingModel = preset.ProcessingModel,
            Source = source
        };
    }
}