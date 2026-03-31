using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Events;
using ResourceRouter.Core.Models;
using ResourceRouter.PluginSdk;

namespace ResourceRouter.Core.Services;

public sealed class PipelineEngine
{
    private readonly IStorageProvider _storageProvider;
    private readonly ResourceManager _resourceManager;
    private readonly IFormatConverterResolver _converterResolver;
    private readonly IAIProvider _aiProvider;
    private readonly IThumbnailProvider _thumbnailProvider;
    private readonly ICloudSyncProvider _cloudSyncProvider;
    private readonly IResourceHealthMonitor _healthMonitor;
    private readonly IResourceFeatureExtractor _featureExtractor;
    private readonly IResourceGovernanceProvider _governanceProvider;
    private readonly IProcessingConfigurationProvider _processingConfigurationProvider;
    private readonly IWaitingScheduler _waitingScheduler;
    private readonly ISyncOrchestrator _syncOrchestrator;
    private readonly IResourceMetadataFacetPolicy _metadataFacetPolicy;
    private readonly IAppLogger? _logger;
    private readonly SemaphoreSlim _processingSemaphore;

    private readonly ConcurrentDictionary<Guid, PendingResource> _waitingQueue = new();
    private readonly ConcurrentDictionary<Guid, byte> _suppressedResourceIds = new();

    public PipelineEngine(
        IStorageProvider storageProvider,
        ResourceManager resourceManager,
        IFormatConverterResolver converterResolver,
        IAIProvider aiProvider,
        IThumbnailProvider thumbnailProvider,
        ICloudSyncProvider cloudSyncProvider,
        IResourceHealthMonitor healthMonitor,
        IResourceFeatureExtractor featureExtractor,
        IResourceGovernanceProvider governanceProvider,
        IProcessingConfigurationProvider processingConfigurationProvider,
        IResourceMetadataFacetPolicy? metadataFacetPolicy = null,
        IAppLogger? logger = null,
        int maxConcurrentProcessing = 2,
        int maxConcurrentSync = 1,
        IWaitingScheduler? waitingScheduler = null,
        ISyncOrchestrator? syncOrchestrator = null)
    {
        _storageProvider = storageProvider;
        _resourceManager = resourceManager;
        _converterResolver = converterResolver;
        _aiProvider = aiProvider;
        _thumbnailProvider = thumbnailProvider;
        _cloudSyncProvider = cloudSyncProvider;
        _healthMonitor = healthMonitor;
        _featureExtractor = featureExtractor;
        _governanceProvider = governanceProvider;
        _processingConfigurationProvider = processingConfigurationProvider;
        _metadataFacetPolicy = metadataFacetPolicy ?? new DefaultResourceMetadataFacetPolicy();
        _logger = logger;
        _processingSemaphore = new SemaphoreSlim(Math.Max(1, maxConcurrentProcessing), Math.Max(1, maxConcurrentProcessing));
        _waitingScheduler = waitingScheduler ?? new DefaultWaitingScheduler(_logger);
        _syncOrchestrator = syncOrchestrator ?? new DefaultSyncOrchestrator(_cloudSyncProvider, _logger, maxConcurrentSync);
    }

    public event EventHandler<PendingResource>? OnResourceEnterWaiting;
    public event EventHandler<Resource>? OnResourceReady;
    public event EventHandler<ResourceErrorEventArgs>? OnResourceError;
    public event EventHandler<Resource>? OnOpenConfigDialog;

    public async Task<PendingResource> IngestResourceAsync(
        RawDropData dropData,
        ResourceIngestOptions options,
        TimeSpan? waitingDuration = null,
        CancellationToken cancellationToken = default)
    {
        var healthReport = await _healthMonitor.ProbeDropAsync(dropData, cancellationToken).ConfigureAwait(false);
        if (!healthReport.IsHealthy)
        {
            _logger?.LogWarning($"Drop health check reported unavailable, continue with degraded mode: {healthReport.Message}");
        }

        var resource = await _storageProvider.StoreRawAsync(dropData, options, cancellationToken).ConfigureAwait(false);
        resource.Health = new ResourceHealthStatus
        {
            LastCheckAt = DateTimeOffset.UtcNow,
            LastCheckPassed = healthReport.IsHealthy,
            LastCheckMessage = healthReport.Message
        };

        var policy = _governanceProvider.GetPolicy(dropData, resource.Source);
        if (!policy.EnableHealthMonitoring)
        {
            // Currently nothing immediately mutates from this boolean, placeholder for feature gating.
        }

        _ = await _featureExtractor.ExtractFromDropAsync(dropData, cancellationToken).ConfigureAwait(false);

        resource.State = ResourceState.Waiting;
        resource.WaitingExpiresAt = DateTimeOffset.UtcNow.Add(waitingDuration ?? TimeSpan.FromMinutes(3));

        await _resourceManager.AddAsync(resource, cancellationToken).ConfigureAwait(false);

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pending = new PendingResource
        {
            Resource = resource,
            ExpiresAt = resource.WaitingExpiresAt.Value,
            CancellationSource = linkedCts
        };

        _waitingQueue[resource.Id] = pending;
        OnResourceEnterWaiting?.Invoke(this, pending);

        StartWaitingCountdown(pending, waitingDuration ?? TimeSpan.FromMinutes(3));

        return pending;
    }

    public void UserEditPending(Guid resourceId)
    {
        if (IsSuppressed(resourceId))
        {
            return;
        }

        if (_waitingQueue.TryGetValue(resourceId, out var pending))
        {
            pending.CancellationSource.Cancel();
            OnOpenConfigDialog?.Invoke(this, pending.Resource);
        }
    }

    public void ResumePending(Guid resourceId, TimeSpan? remaining = null)
    {
        if (IsSuppressed(resourceId))
        {
            return;
        }

        if (!_waitingQueue.TryGetValue(resourceId, out var oldPending))
        {
            return;
        }

        var duration = remaining ?? (oldPending.ExpiresAt - DateTimeOffset.UtcNow);
        if (duration <= TimeSpan.Zero)
        {
            _ = ExecutePipelineAsync(oldPending.Resource);
            return;
        }

        var cts = new CancellationTokenSource();
        var resumed = new PendingResource
        {
            Resource = oldPending.Resource,
            ExpiresAt = DateTimeOffset.UtcNow.Add(duration),
            CancellationSource = cts
        };

        resumed.Resource.WaitingExpiresAt = resumed.ExpiresAt;
        _waitingQueue[resourceId] = resumed;
        _ = _resourceManager.UpdateAsync(resumed.Resource);
        OnResourceEnterWaiting?.Invoke(this, resumed);
        StartWaitingCountdown(resumed, duration);
    }

    public void SuppressResource(Guid resourceId)
    {
        _suppressedResourceIds[resourceId] = 0;

        if (_waitingQueue.TryRemove(resourceId, out var pending))
        {
            pending.CancellationSource.Cancel();
        }
    }

    public void ApplyPendingResourceConfiguration(Resource updatedResource)
    {
        if (IsSuppressed(updatedResource.Id))
        {
            return;
        }

        if (!_waitingQueue.TryGetValue(updatedResource.Id, out var pending))
        {
            return;
        }

        var changeSet = PendingResourceConfigurationChangeSet.FromResource(updatedResource, _metadataFacetPolicy);
        changeSet.ApplyTo(pending.Resource, _metadataFacetPolicy);
    }

    public async Task ExecutePipelineAsync(Resource resource, CancellationToken cancellationToken = default)
    {
        await _processingSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsSuppressed(resource.Id))
            {
                _waitingQueue.TryRemove(resource.Id, out _);
                return;
            }

            var finalHealthReport = await _healthMonitor.ProbeResourceAsync(resource, cancellationToken).ConfigureAwait(false);
            resource.Health = new ResourceHealthStatus
            {
                LastCheckAt = DateTimeOffset.UtcNow,
                LastCheckPassed = finalHealthReport.IsHealthy,
                LastCheckMessage = finalHealthReport.Message
            };

            if (!finalHealthReport.IsHealthy)
            {
                _logger?.LogWarning($"Resource health check reported unavailable, pipeline continues: {finalHealthReport.Message}");
            }

            resource.State = ResourceState.Processing;
            resource.WaitingExpiresAt = null;
            await _resourceManager.UpdateAsync(resource, cancellationToken).ConfigureAwait(false);

            if (IsSuppressed(resource.Id))
            {
                return;
            }

            var routes = _converterResolver.ResolveRoutes(resource.Source, resource.MimeType);
            if (!string.IsNullOrWhiteSpace(resource.ProcessedRouteId))
            {
                var hasPreferred = false;
                foreach (var route in routes)
                {
                    if (string.Equals(route.RouteId, resource.ProcessedRouteId, StringComparison.OrdinalIgnoreCase))
                    {
                        hasPreferred = true;
                        break;
                    }
                }

                if (!hasPreferred)
                {
                    resource.ProcessedRouteId = null;
                }
            }

            if (string.IsNullOrWhiteSpace(resource.ProcessedRouteId) && routes.Count > 0)
            {
                resource.ProcessedRouteId = routes[0].RouteId;
            }

            var converter = _converterResolver.Resolve(resource.Source, resource.MimeType, resource.ProcessedRouteId);
            var processingConfiguration = _processingConfigurationProvider.Resolve(resource, converter);
            var activePath = resource.GetActivePath();
            if (converter is not null && !string.IsNullOrWhiteSpace(activePath))
            {
                var convertOptions = new ConvertOptions
                {
                    ResourceId = resource.Id,
                    OutputDirectory = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ResourceRouter",
                        "processed",
                        resource.Id.ToString("N")),
                    PreferTextOutput = true,
                    EnableOcr = processingConfiguration.EnableOcr,
                    EnableAudioTranscription = processingConfiguration.EnableAudioTranscription,
                    CapabilityApi = processingConfiguration.CapabilityApi,
                    PluginOptions = processingConfiguration.PluginOptions
                };

                var conversion = await converter
                    .ConvertToFriendlyAsync(activePath, convertOptions, cancellationToken)
                    .ConfigureAwait(false);

                resource.ProcessedFilePath = conversion.ProcessedFilePath;
                resource.ProcessedText = conversion.ProcessedText;
            }
            else
            {
                resource.ProcessedFilePath = null;
                resource.ProcessedText = null;
                resource.ProcessedRouteId = null;
            }

            if (resource.ProcessingModel != ModelType.None)
            {
                var aiResult = await _aiProvider.AnalyzeAsync(resource, cancellationToken).ConfigureAwait(false);
                var currentFacet = _metadataFacetPolicy.Read(resource);
                var mergedPropertyTags = currentFacet.PropertyTags
                    .Concat(aiResult.Tags ?? Array.Empty<string>())
                    .Select(ResourceTagRules.Normalize)
                    .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                    .Select(static tag => tag!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                _metadataFacetPolicy.Apply(resource, new ResourceMetadataFacet
                {
                    TitleOverride = currentFacet.TitleOverride,
                    Annotations = currentFacet.Annotations,
                    Summary = aiResult.Summary,
                    ConditionTags = currentFacet.ConditionTags,
                    PropertyTags = mergedPropertyTags,
                    OriginalFileName = currentFacet.OriginalFileName,
                    MimeType = currentFacet.MimeType,
                    FileSize = currentFacet.FileSize,
                    Source = currentFacet.Source,
                    CreatedAt = currentFacet.CreatedAt,
                    ExtensionMetadata = currentFacet.ExtensionMetadata
                });
            }

            resource.ThumbnailPath = await _thumbnailProvider.GenerateAsync(resource, cancellationToken).ConfigureAwait(false);

            var finalPolicy = _governanceProvider.GetPolicy(resource);
            if (!finalPolicy.EnableRemote)
            {
                resource.SyncPolicy = SyncPolicy.LocalOnly;
            }

            _ = await _featureExtractor.ExtractFromResourceAsync(resource, cancellationToken).ConfigureAwait(false);

            if (IsSuppressed(resource.Id))
            {
                return;
            }

            resource.State = ResourceState.Ready;
            await _resourceManager.UpdateAsync(resource, cancellationToken).ConfigureAwait(false);

            if (resource.SyncPolicy == SyncPolicy.CloudDefault)
            {
                _syncOrchestrator.EnqueueUpload(resource, cancellationToken);
            }

            _waitingQueue.TryRemove(resource.Id, out _);
            OnResourceReady?.Invoke(this, resource);
        }
        catch (Exception ex)
        {
            if (IsSuppressed(resource.Id))
            {
                return;
            }

            resource.State = ResourceState.Error;
            resource.LastError = ex.Message;
            await _resourceManager.UpdateAsync(resource, cancellationToken).ConfigureAwait(false);

            _waitingQueue.TryRemove(resource.Id, out _);
            OnResourceError?.Invoke(this, new ResourceErrorEventArgs { Resource = resource, Exception = ex });
        }
        finally
        {
            _processingSemaphore.Release();
        }
    }

    private void StartWaitingCountdown(PendingResource pending, TimeSpan duration)
    {
        _waitingScheduler.Schedule(
            pending.Resource.Id,
            duration,
            pending.CancellationSource.Token,
            async cancellationToken =>
            {
                if (IsSuppressed(pending.Resource.Id))
                {
                    return;
                }

                await ExecutePipelineAsync(pending.Resource, cancellationToken).ConfigureAwait(false);
            });
    }

    private bool IsSuppressed(Guid resourceId)
    {
        return _suppressedResourceIds.ContainsKey(resourceId);
    }
}