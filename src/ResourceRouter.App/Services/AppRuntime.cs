using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;
using ResourceRouter.Core.Services;
using ResourceRouter.Infrastructure.Logging;
using ResourceRouter.Infrastructure.Search;
using ResourceRouter.Infrastructure.Storage;
using ResourceRouter.Infrastructure.Sync;
using ResourceRouter.App.Interop;

namespace ResourceRouter.App.Services;

public sealed class AppRuntime : IDisposable
{
    private const string FrameworkBasicDocumentKey = "framework.basic";
    private const string FrameworkNativeDocumentKey = "framework.native";
    private const string PluginSettingsDocumentKey = "plugins.settings";
    private static readonly Guid FrameworkBasicResourceId = Guid.Parse("f344dd5b-2af9-4382-af74-d9c9460136ee");
    private static readonly Guid FrameworkNativeResourceId = Guid.Parse("cb2ceaf4-518f-4413-b66f-70f3688b4ebe");
    private static readonly Guid PluginSettingsResourceId = Guid.Parse("2db68f03-2f78-4680-b5bc-9eb4f77ef32c");
    private static readonly JsonSerializerOptions ConfigResourceJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly FileLogger _logger;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(45) };
    private readonly SemaphoreSlim _configMutationLock = new(1, 1);
    private ICloudSyncProvider _cloudSyncProvider = new NoOpCloudSyncProvider();

    public AppRuntime(FileLogger logger)
    {
        _logger = logger;
    }

    public AppConfig Config { get; private set; } = new();

    public ConfigStore ConfigStore { get; private set; } = default!;

    public PluginHost PluginHost { get; private set; } = default!;

    public IResourceStore ResourceStore { get; private set; } = default!;

    public ISearchIndex SearchIndex { get; private set; } = default!;

    public ResourceManager ResourceManager { get; private set; } = default!;

    public PipelineEngine PipelineEngine { get; private set; } = default!;

    public IResourceMetadataFacetPolicy MetadataFacetPolicy { get; private set; } = default!;

    public event Func<ResourceConfigChangeHookContext, Task>? OnResourceConfigChanged;

    public IAppLogger Logger => _logger;

    public async Task InitializeAsync()
    {
        LocalPathProvider.EnsureAll();

        var configStore = new ConfigStore(logger: _logger);
        var loadedConfig = await configStore.LoadAsync().ConfigureAwait(false);
        var config = NormalizeUnsupportedMechanismConfig(loadedConfig, notifyUser: false, out var normalized);
        if (normalized)
        {
            await configStore.SaveAsync(config).ConfigureAwait(false);
        }

        Config = config;

        var pluginHost = new PluginHost(_logger);

        _logger.LogInfo("插件优先模式：框架不再装配内置 converter/provider；处理后资源仅由外置插件提供。未命中插件时回落原始资源。");

        if (!string.IsNullOrWhiteSpace(config.PluginDirectory))
        {
            pluginHost.LoadPlugins(config.PluginDirectory);
        }
        else
        {
            pluginHost.LoadPlugins(LocalPathProvider.PluginsDirectory);
        }

        var resourceStore = new SqliteResourceStore();
        var searchIndex = new SearchEngine(resourceStore);
        var resourceManager = new ResourceManager(resourceStore, searchIndex);
        var metadataFacetPolicy = new DefaultResourceMetadataFacetPolicy();

        IAIProvider aiProvider = new NoOpAIProvider();
        ICloudSyncProvider cloudSyncProvider = new NoOpCloudSyncProvider();
        IThumbnailProvider thumbnailProvider = new NoOpThumbnailProvider();

        var capabilityApi = new DefaultProcessingCapabilityApi(() => Config, resourceManager, _logger, _httpClient, metadataFacetPolicy);
        var processingConfigProvider = new RuntimeProcessingConfigurationProvider(() => Config, capabilityApi);

        var storageProvider = new LocalFileStorage(_logger);

        var healthMonitor = new ResourceRouter.Core.Services.NoOp.NoOpResourceHealthMonitor();
        var featureExtractor = new ResourceRouter.Core.Services.NoOp.NoOpResourceFeatureExtractor();
        var governanceProvider = new ResourceRouter.Core.Services.NoOp.NoOpResourceGovernanceProvider();

        var pipeline = new PipelineEngine(
            storageProvider,
            resourceManager,
            pluginHost,
            aiProvider,
            thumbnailProvider,
            cloudSyncProvider,
            healthMonitor,
            featureExtractor,
            governanceProvider,
            processingConfigProvider,
            metadataFacetPolicy,
            _logger,
            maxConcurrentProcessing: 2,
            maxConcurrentSync: 1);

        ConfigStore = configStore;
        PluginHost = pluginHost;
        ResourceStore = resourceStore;
        SearchIndex = searchIndex;
        ResourceManager = resourceManager;
        PipelineEngine = pipeline;
        MetadataFacetPolicy = metadataFacetPolicy;
        _cloudSyncProvider = cloudSyncProvider;

        await SyncConfigResourcesAsync(config).ConfigureAwait(false);
    }

    public async Task HandleResourceConfigChangedAsync(
        Resource resource,
        PrivacyLevel previousPrivacy,
        SyncPolicy previousSyncPolicy,
        ModelType previousProcessingModel,
        string previousPermissionPresetId,
        PersistencePolicy previousPersistencePolicy,
        CancellationToken cancellationToken = default)
    {
        var context = new ResourceConfigChangeHookContext
        {
            Resource = resource,
            PreviousPrivacy = previousPrivacy,
            PreviousSyncPolicy = previousSyncPolicy,
            PreviousProcessingModel = previousProcessingModel,
            PreviousPermissionPresetId = previousPermissionPresetId,
            PreviousPersistencePolicy = previousPersistencePolicy
        };

        if (previousPersistencePolicy != resource.PersistencePolicy)
        {
            await EnforcePersistencePolicyAsync(resource, cancellationToken).ConfigureAwait(false);
        }

        if (context.RequiresCloudUpload)
        {
            _ = UploadConfigChangedResourceAsync(resource, cancellationToken);
        }
        else if (context.RequiresCloudDeleteHint)
        {
            _logger.LogInfo($"资源 {resource.Id} 已切换为非云同步，后续可在 Hook 中接入云端删除逻辑。");
        }

        var handlers = OnResourceConfigChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (var callback in handlers.GetInvocationList().OfType<Func<ResourceConfigChangeHookContext, Task>>())
        {
            try
            {
                await callback(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"配置变更 Hook 执行失败: {ex.Message}");
            }
        }
    }

    private async Task EnforcePersistencePolicyAsync(Resource resource, CancellationToken cancellationToken)
    {
        if (resource.PersistencePolicy == PersistencePolicy.Unified)
        {
            if (!string.IsNullOrWhiteSpace(resource.SourceUri) && File.Exists(resource.SourceUri))
            {
                if (string.IsNullOrWhiteSpace(resource.InternalPath) || !File.Exists(resource.InternalPath))
                {
                    var resourceDir = Path.Combine(LocalPathProvider.RawDirectory, resource.Id.ToString("N"));
                    Directory.CreateDirectory(resourceDir);
                    var internalPath = Path.Combine(resourceDir, Path.GetFileName(resource.SourceUri));

                    await using (var src = File.OpenRead(resource.SourceUri))
                    await using (var dst = File.Create(internalPath))
                    {
                        await src.CopyToAsync(dst, cancellationToken).ConfigureAwait(false);
                    }

                    resource.InternalPath = internalPath;
                }

                // In Unified mode for fixed resources, the original path is discarded.
                resource.SourceUri = null;
                resource.SourceLastModifiedAt = null;
                await ResourceManager.UpdateAsync(resource, cancellationToken).ConfigureAwait(false);
            }
        }
        else if (resource.PersistencePolicy == PersistencePolicy.Backup)
        {
            if (!string.IsNullOrWhiteSpace(resource.SourceUri) && File.Exists(resource.SourceUri))
            {
                if (string.IsNullOrWhiteSpace(resource.InternalPath) || !File.Exists(resource.InternalPath))
                {
                    var resourceDir = Path.Combine(LocalPathProvider.RawDirectory, resource.Id.ToString("N"));
                    Directory.CreateDirectory(resourceDir);
                    var internalPath = Path.Combine(resourceDir, Path.GetFileName(resource.SourceUri));

                    await using (var src = File.OpenRead(resource.SourceUri))
                    await using (var dst = File.Create(internalPath))
                    {
                        await src.CopyToAsync(dst, cancellationToken).ConfigureAwait(false);
                    }

                    resource.InternalPath = internalPath;
                    await ResourceManager.UpdateAsync(resource, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task UploadConfigChangedResourceAsync(Resource resource, CancellationToken cancellationToken)
    {
        try
        {
            await _cloudSyncProvider.UploadAsync(resource, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"配置变更触发云上传失败: {ex.Message}");
        }
    }

    public async Task SaveConfigAsync(AppConfig config)
    {
        await _configMutationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await PersistConfigAsync(config, notifyUser: true).ConfigureAwait(false);
        }
        finally
        {
            _configMutationLock.Release();
        }
    }

    public IReadOnlyList<ProcessedRouteOption> GetProcessedRouteOptions(Resource resource)
    {
        return PluginHost.ResolveRoutes(resource.Source, resource.MimeType);
    }

    private async Task<(AppConfig Config, bool Normalized)> PersistConfigAsync(AppConfig config, bool notifyUser)
    {
        var normalizedConfig = NormalizeUnsupportedMechanismConfig(config, notifyUser, out var normalized);
        await ConfigStore.SaveAsync(normalizedConfig).ConfigureAwait(false);
        Config = normalizedConfig;
        _cloudSyncProvider = new NoOpCloudSyncProvider();
        await SyncConfigResourcesAsync(normalizedConfig).ConfigureAwait(false);
        return (normalizedConfig, normalized);
    }

    private async Task SyncConfigResourcesAsync(AppConfig config)
    {
        try
        {
            var projectionDir = Path.Combine(LocalPathProvider.RootDirectory, "config-resources");
            Directory.CreateDirectory(projectionDir);

            var documents = BuildConfigResourceDocuments(config);
            foreach (var doc in documents)
            {
                var filePath = Path.Combine(projectionDir, doc.FileName);
                await File.WriteAllTextAsync(filePath, doc.JsonContent).ConfigureAwait(false);

                var existing = await ResourceManager.GetByIdAsync(doc.Id).ConfigureAwait(false);
                var createdAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow;
                var fileInfo = new FileInfo(filePath);

                var resource = new Resource
                {
                    Id = doc.Id,
                    CreatedAt = createdAt,
                    SourceUri = filePath,
                    OriginalFileName = Path.GetFileName(filePath),
                    MimeType = "application/json",
                    FileSize = fileInfo.Exists ? fileInfo.Length : 0,
                    Source = ResourceSource.Manual,
                    ProcessedFilePath = filePath,
                    ProcessedText = doc.JsonContent,
                    ThumbnailPath = existing?.ThumbnailPath,
                    Summary = existing?.Summary,
                    ConditionTags = doc.ConditionTags,
                    TitleOverride = doc.Title,
                    Annotations = existing?.Annotations,
                    PropertyTags = doc.PropertyTags,
                    Privacy = PrivacyLevel.Private,
                    SyncPolicy = SyncPolicy.LocalOnly,
                    SyncTargetDevices = Array.Empty<string>(),
                    ProcessingModel = ModelType.None,
                    PermissionPresetId = PermissionPreset.PrivatePresetId,
                    State = ResourceState.Ready,
                    WaitingExpiresAt = null,
                    LastError = null
                };

                await ResourceManager.AddAsync(resource).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"配置资源投影失败: {ex.Message}");
        }
    }

    private AppConfig NormalizeUnsupportedMechanismConfig(AppConfig input, bool notifyUser, out bool normalized)
    {
        var hasInternalCapabilitiesEnabledWhileInternalDisabled =
            !input.EnableInternalMechanisms && HasInternalMechanismConfigEnabled(input);
        var hasRemoteEnabled = input.EnableRemoteMechanism;
        var hasHealthEnabled = HasAnyEnabled(input.HealthMonitoringByType) || HasAnyEnabled(input.HealthMonitoringBySource);
        var hasFeatureEnabled = HasAnyEnabled(input.FeatureizationByType) || HasAnyEnabled(input.FeatureizationBySource);
        var hasDedupEnabled = HasAnyEnabled(input.DedupByType) || HasAnyEnabled(input.DedupBySource);

        normalized =
            hasInternalCapabilitiesEnabledWhileInternalDisabled ||
            hasRemoteEnabled ||
            hasHealthEnabled ||
            hasFeatureEnabled ||
            hasDedupEnabled;
        if (!normalized)
        {
            return input;
        }

        var enabledItems = new List<string>();
        if (hasInternalCapabilitiesEnabledWhileInternalDisabled)
        {
            enabledItems.Add("内部能力");
        }

        if (hasRemoteEnabled)
        {
            enabledItems.Add("Remote");
        }

        if (hasHealthEnabled)
        {
            enabledItems.Add("健康监测");
        }

        if (hasFeatureEnabled)
        {
            enabledItems.Add("特征化");
        }

        if (hasDedupEnabled)
        {
            enabledItems.Add("去重");
        }

        var details = string.Join("、", enabledItems);
        var message = $"{details} 当前仅保留接口，尚未实现。已自动关闭对应开关。";

        _logger.LogWarning(message);
        if (notifyUser)
        {
            MessageBox.Show(message, "Resource Router", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        return new AppConfig
        {
            DefaultPrivacy = input.DefaultPrivacy,
            DefaultSyncPolicy = input.DefaultSyncPolicy,
            DefaultProcessingModel = input.EnableInternalMechanisms
                ? input.DefaultProcessingModel
                : ModelType.None,
            OllamaEndpoint = input.OllamaEndpoint,
            CloudAiProvider = input.EnableInternalMechanisms
                ? input.CloudAiProvider
                : NativeCapabilityProviders.CloudAI.None,
            CloudAiEndpoint = input.CloudAiEndpoint,
            CloudAiApiKey = input.CloudAiApiKey,
            CloudAiModel = input.CloudAiModel,
            CloudEndpoint = input.CloudEndpoint,
            CloudApiKey = input.CloudApiKey,
            PluginDirectory = input.PluginDirectory,
            DefaultPermissionPresetId = input.DefaultPermissionPresetId,
            EnableInternalMechanisms = input.EnableInternalMechanisms,
            EnableAI = input.EnableAI,
            EnableOcr = input.EnableInternalMechanisms && input.EnableOcr,
            OcrProvider = input.OcrProvider,
            OcrCliPath = input.OcrCliPath,
            OcrEndpoint = input.OcrEndpoint,
            OcrModel = input.OcrModel,
            OcrApiKey = input.OcrApiKey,
            EnableAudioTranscription = input.EnableInternalMechanisms && input.EnableAudioTranscription,
            AudioTranscriptionProvider = input.AudioTranscriptionProvider,
            AudioTranscriptionCliPath = input.AudioTranscriptionCliPath,
            AudioTranscriptionEndpoint = input.AudioTranscriptionEndpoint,
            AudioTranscriptionModel = input.AudioTranscriptionModel,
            AudioTranscriptionApiKey = input.AudioTranscriptionApiKey,
            CloudSyncProvider = input.EnableInternalMechanisms
                ? input.CloudSyncProvider
                : NativeCapabilityProviders.CloudSync.None,
            RemoteProvider = input.RemoteProvider,
            RemoteEndpoint = input.RemoteEndpoint,
            EnableRemoteMechanism = false,
            ThumbnailProviderMode = input.EnableInternalMechanisms
                ? input.ThumbnailProviderMode
                : NativeCapabilityProviders.Thumbnail.None,
            HealthMonitoringByType = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            FeatureizationByType = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            DedupByType = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            HealthMonitoringBySource = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            FeatureizationBySource = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            DedupBySource = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            PluginSettings = input.PluginSettings.ToDictionary(
                kv => kv.Key,
                kv => new Dictionary<string, string>(kv.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private static bool HasAnyEnabled(IReadOnlyDictionary<string, bool> switches)
    {
        return switches.Any(static kv => kv.Value);
    }

    private static IReadOnlyList<ConfigResourceDocumentData> BuildConfigResourceDocuments(AppConfig config)
    {
        var basic = new FrameworkBasicConfigResource
        {
            DefaultPrivacy = config.DefaultPrivacy,
            DefaultSyncPolicy = config.DefaultSyncPolicy,
            DefaultProcessingModel = config.DefaultProcessingModel,
            EnableAI = config.EnableAI,
            OllamaEndpoint = config.OllamaEndpoint,
            PluginDirectory = config.PluginDirectory,
            DefaultPermissionPresetId = config.DefaultPermissionPresetId
        };

        var native = new FrameworkNativeConfigResource
        {
            EnableInternalMechanisms = config.EnableInternalMechanisms,
            EnableOcr = config.EnableOcr,
            OcrProvider = config.OcrProvider,
            OcrCliPath = config.OcrCliPath,
            OcrEndpoint = config.OcrEndpoint,
            OcrModel = config.OcrModel,
            OcrApiKey = config.OcrApiKey,
            EnableAudioTranscription = config.EnableAudioTranscription,
            AudioTranscriptionProvider = config.AudioTranscriptionProvider,
            AudioTranscriptionCliPath = config.AudioTranscriptionCliPath,
            AudioTranscriptionEndpoint = config.AudioTranscriptionEndpoint,
            AudioTranscriptionModel = config.AudioTranscriptionModel,
            AudioTranscriptionApiKey = config.AudioTranscriptionApiKey,
            CloudAiProvider = config.CloudAiProvider,
            CloudAiEndpoint = config.CloudAiEndpoint,
            CloudAiModel = config.CloudAiModel,
            CloudAiApiKey = config.CloudAiApiKey ?? config.CloudApiKey,
            CloudSyncProvider = config.CloudSyncProvider,
            CloudEndpoint = config.CloudEndpoint,
            RemoteProvider = config.RemoteProvider,
            RemoteEndpoint = config.RemoteEndpoint,
            EnableRemoteMechanism = config.EnableRemoteMechanism,
            ThumbnailProviderMode = config.ThumbnailProviderMode,
            HealthMonitoringByType = new Dictionary<string, bool>(config.HealthMonitoringByType, StringComparer.OrdinalIgnoreCase),
            FeatureizationByType = new Dictionary<string, bool>(config.FeatureizationByType, StringComparer.OrdinalIgnoreCase),
            DedupByType = new Dictionary<string, bool>(config.DedupByType, StringComparer.OrdinalIgnoreCase),
            HealthMonitoringBySource = new Dictionary<string, bool>(config.HealthMonitoringBySource, StringComparer.OrdinalIgnoreCase),
            FeatureizationBySource = new Dictionary<string, bool>(config.FeatureizationBySource, StringComparer.OrdinalIgnoreCase),
            DedupBySource = new Dictionary<string, bool>(config.DedupBySource, StringComparer.OrdinalIgnoreCase)
        };

        var plugin = new PluginSettingsConfigResource
        {
            PluginSettings = config.PluginSettings.ToDictionary(
                kv => kv.Key,
                kv => new Dictionary<string, string>(kv.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase)
        };

        return new[]
        {
            new ConfigResourceDocumentData
            {
                DocumentKey = FrameworkBasicDocumentKey,
                Id = FrameworkBasicResourceId,
                FileName = "framework.basic.json",
                Title = "框架基础配置",
                ConditionTags = new[] { "config" },
                PropertyTags = new[] { "framework", "basic" },
                JsonContent = JsonSerializer.Serialize(basic, ConfigResourceJsonOptions)
            },
            new ConfigResourceDocumentData
            {
                DocumentKey = FrameworkNativeDocumentKey,
                Id = FrameworkNativeResourceId,
                FileName = "framework.native.json",
                Title = "框架原生能力配置",
                ConditionTags = new[] { "config" },
                PropertyTags = new[] { "framework", "native" },
                JsonContent = JsonSerializer.Serialize(native, ConfigResourceJsonOptions)
            },
            new ConfigResourceDocumentData
            {
                DocumentKey = PluginSettingsDocumentKey,
                Id = PluginSettingsResourceId,
                FileName = "plugins.settings.json",
                Title = "插件配置集合",
                ConditionTags = new[] { "config" },
                PropertyTags = new[] { "plugin", "settings" },
                JsonContent = JsonSerializer.Serialize(plugin, ConfigResourceJsonOptions)
            }
        };
    }

    private static bool HasInternalMechanismConfigEnabled(AppConfig input)
    {
        var cloudAiEnabled = !string.Equals(
            string.IsNullOrWhiteSpace(input.CloudAiProvider) ? NativeCapabilityProviders.CloudAI.Auto : input.CloudAiProvider,
            NativeCapabilityProviders.CloudAI.None,
            StringComparison.OrdinalIgnoreCase);

        var cloudSyncEnabled = !string.Equals(
            string.IsNullOrWhiteSpace(input.CloudSyncProvider) ? NativeCapabilityProviders.CloudSync.Auto : input.CloudSyncProvider,
            NativeCapabilityProviders.CloudSync.None,
            StringComparison.OrdinalIgnoreCase);

        var thumbnailEnabled = !string.Equals(
            string.IsNullOrWhiteSpace(input.ThumbnailProviderMode) ? NativeCapabilityProviders.Thumbnail.Auto : input.ThumbnailProviderMode,
            NativeCapabilityProviders.Thumbnail.None,
            StringComparison.OrdinalIgnoreCase);

        return
            input.DefaultProcessingModel != ModelType.None ||
            input.EnableOcr ||
            input.EnableAudioTranscription ||
            cloudAiEnabled ||
            cloudSyncEnabled ||
            thumbnailEnabled;
    }

    private sealed class ConfigResourceDocumentData
    {
        public required string DocumentKey { get; init; }

        public required Guid Id { get; init; }

        public required string FileName { get; init; }

        public required string Title { get; init; }

        public required string[] ConditionTags { get; init; }

        public required string[] PropertyTags { get; init; }

        public required string JsonContent { get; init; }
    }

    private sealed class FrameworkBasicConfigResource
    {
        public PrivacyLevel DefaultPrivacy { get; init; }

        public SyncPolicy DefaultSyncPolicy { get; init; }

        public ModelType DefaultProcessingModel { get; init; }

        public bool EnableAI { get; init; }

        public string OllamaEndpoint { get; init; } = "http://localhost:11434/api/generate";

        public string? PluginDirectory { get; init; }

        public string? DefaultPermissionPresetId { get; init; }
    }

    private sealed class FrameworkNativeConfigResource
    {
        public bool EnableInternalMechanisms { get; init; }

        public bool EnableOcr { get; init; }

        public string OcrProvider { get; init; } = NativeCapabilityProviders.Ocr.Auto;

        public string? OcrCliPath { get; init; }

        public string? OcrEndpoint { get; init; }

        public string OcrModel { get; init; } = "eng+chi_sim";

        public string? OcrApiKey { get; init; }

        public bool EnableAudioTranscription { get; init; }

        public string AudioTranscriptionProvider { get; init; } = NativeCapabilityProviders.AudioTranscription.Auto;

        public string? AudioTranscriptionCliPath { get; init; }

        public string? AudioTranscriptionEndpoint { get; init; }

        public string AudioTranscriptionModel { get; init; } = "whisper-1";

        public string? AudioTranscriptionApiKey { get; init; }

        public string CloudAiProvider { get; init; } = NativeCapabilityProviders.CloudAI.Auto;

        public string? CloudAiEndpoint { get; init; }

        public string CloudAiModel { get; init; } = "gpt-4o-mini";

        public string? CloudAiApiKey { get; init; }

        public string CloudSyncProvider { get; init; } = NativeCapabilityProviders.CloudSync.Auto;

        public string? CloudEndpoint { get; init; }

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
    }

    private sealed class NoOpAIProvider : IAIProvider
    {
        public Task<AIResult> AnalyzeAsync(Resource resource, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(AIResult.Empty);
        }
    }

    private sealed class NoOpThumbnailProvider : IThumbnailProvider
    {
        public Task<string?> GenerateAsync(Resource resource, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class PluginSettingsConfigResource
    {
        public Dictionary<string, Dictionary<string, string>> PluginSettings { get; init; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _configMutationLock.Dispose();
        _httpClient.Dispose();
    }
}