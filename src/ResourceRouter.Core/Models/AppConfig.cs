using System;
using System.Collections.Generic;

namespace ResourceRouter.Core.Models;

public sealed class AppConfig
{
    public PrivacyLevel DefaultPrivacy { get; init; } = PrivacyLevel.Private;

    public SyncPolicy DefaultSyncPolicy { get; init; } = SyncPolicy.LocalOnly;

    public ModelType DefaultProcessingModel { get; init; } = ModelType.LocalSmall;

    public string OllamaEndpoint { get; init; } = "http://localhost:11434/api/generate";

    public string CloudAiProvider { get; init; } = NativeCapabilityProviders.CloudAI.Auto;

    public string? CloudAiEndpoint { get; init; }

    public string? CloudAiApiKey { get; init; }

    public string CloudAiModel { get; init; } = "gpt-4o-mini";

    public string? CloudEndpoint { get; init; }

    public string? CloudApiKey { get; init; }

    public string? PluginDirectory { get; init; }

    public string DefaultPermissionPresetId { get; init; } = PermissionPreset.PrivatePresetId;

    public bool EnableInternalMechanisms { get; init; } = false;

    public bool EnableAI { get; init; } = true;

    public bool EnableOcr { get; init; } = false;

    public string OcrProvider { get; init; } = NativeCapabilityProviders.Ocr.Auto;

    public string? OcrCliPath { get; init; }

    public string? OcrEndpoint { get; init; }

    public string OcrModel { get; init; } = "eng+chi_sim";

    public string? OcrApiKey { get; init; }

    public bool EnableAudioTranscription { get; init; } = false;

    public string AudioTranscriptionProvider { get; init; } = NativeCapabilityProviders.AudioTranscription.Auto;

    public string? AudioTranscriptionCliPath { get; init; }

    public string? AudioTranscriptionEndpoint { get; init; }

    public string AudioTranscriptionModel { get; init; } = "whisper-1";

    public string? AudioTranscriptionApiKey { get; init; }

    public string CloudSyncProvider { get; init; } = NativeCapabilityProviders.CloudSync.Auto;

    public string RemoteProvider { get; init; } = NativeCapabilityProviders.Remote.Auto;

    public string? RemoteEndpoint { get; init; }

    public bool EnableRemoteMechanism { get; init; }

    public string ThumbnailProviderMode { get; init; } = NativeCapabilityProviders.Thumbnail.Auto;

    public Dictionary<string, bool> HealthMonitoringByType { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, bool> FeatureizationByType { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, bool> DedupByType { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, bool> HealthMonitoringBySource { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, bool> FeatureizationBySource { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, bool> DedupBySource { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Dictionary<string, string>> PluginSettings { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public static string GetDefaultConfigPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return System.IO.Path.Combine(root, "ResourceRouter", "config.json");
    }
}