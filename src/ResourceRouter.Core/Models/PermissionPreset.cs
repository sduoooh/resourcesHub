using System;
using System.Collections.Generic;

namespace ResourceRouter.Core.Models;

public sealed class PermissionPreset
{
    public const string PrivatePresetId = "private-default";
    public const string PublicPresetId = "public-default";

    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required PrivacyLevel Privacy { get; init; }

    public required SyncPolicy SyncPolicy { get; init; }

    public required ModelType ProcessingModel { get; init; }

    public bool AllowCloudSync { get; init; }

    public static IReadOnlyDictionary<string, PermissionPreset> BuiltIn { get; } =
        new Dictionary<string, PermissionPreset>(StringComparer.OrdinalIgnoreCase)
        {
            [PrivatePresetId] = new()
            {
                Id = PrivatePresetId,
                DisplayName = "隐私",
                Privacy = PrivacyLevel.Private,
                SyncPolicy = SyncPolicy.LocalOnly,
                ProcessingModel = ModelType.LocalSmall,
                AllowCloudSync = false
            },
            [PublicPresetId] = new()
            {
                Id = PublicPresetId,
                DisplayName = "公开",
                Privacy = PrivacyLevel.Public,
                SyncPolicy = SyncPolicy.CloudDefault,
                ProcessingModel = ModelType.CloudAI,
                AllowCloudSync = true
            }
        };
}