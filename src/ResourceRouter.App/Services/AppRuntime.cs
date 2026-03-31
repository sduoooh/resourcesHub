using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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
        var resourceManager = new ResourceManager(resourceStore);
        BindSearchProjection(resourceManager, searchIndex);
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
    }

    private void BindSearchProjection(ResourceManager resourceManager, ISearchIndex searchIndex)
    {
        resourceManager.OnResourceCreated += async (_, args) =>
        {
            try
            {
                await searchIndex.IndexAsync(args.Resource).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"资源索引创建投影失败: {ex.Message}");
            }
        };

        resourceManager.OnResourceUpdated += async (_, args) =>
        {
            try
            {
                await searchIndex.IndexAsync(args.Resource).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"资源索引更新投影失败: {ex.Message}");
            }
        };
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
                    var resourceDir = LocalPathProvider.GetRawResourceDirectory(resource.Id);
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
                    var resourceDir = LocalPathProvider.GetRawResourceDirectory(resource.Id);
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
        return (normalizedConfig, normalized);
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

    public void Dispose()
    {
        _configMutationLock.Dispose();
        _httpClient.Dispose();
    }
}