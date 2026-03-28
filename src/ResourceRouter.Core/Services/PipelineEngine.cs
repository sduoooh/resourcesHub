using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    private readonly IAppLogger? _logger;
    private readonly SemaphoreSlim _processingSemaphore;
    private readonly SemaphoreSlim _syncSemaphore;

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
        IAppLogger? logger = null,
        int maxConcurrentProcessing = 2,
        int maxConcurrentSync = 1)
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
        _logger = logger;
        _processingSemaphore = new SemaphoreSlim(Math.Max(1, maxConcurrentProcessing), Math.Max(1, maxConcurrentProcessing));
        _syncSemaphore = new SemaphoreSlim(Math.Max(1, maxConcurrentSync), Math.Max(1, maxConcurrentSync));
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
            throw new InvalidOperationException($"Health check failed: {healthReport.Message}");
        }

        var resource = await _storageProvider.StoreRawAsync(dropData, options, cancellationToken).ConfigureAwait(false);

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

        pending.Resource.PermissionPresetId = updatedResource.PermissionPresetId;
        pending.Resource.Privacy = updatedResource.Privacy;
        pending.Resource.SyncPolicy = updatedResource.SyncPolicy;
        pending.Resource.ProcessingModel = updatedResource.ProcessingModel;
        pending.Resource.PersistencePolicy = updatedResource.PersistencePolicy;
        pending.Resource.ProcessedRouteId = updatedResource.ProcessedRouteId;
        pending.Resource.UserTitle = updatedResource.UserTitle;
        pending.Resource.UserNotes = updatedResource.UserNotes;
        pending.Resource.UserTags = updatedResource.UserTags;
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
            if (!finalHealthReport.IsHealthy)
            {
                resource.State = ResourceState.Error;
                resource.LastError = $"Resource specific health check failed: {finalHealthReport.Message}";
                await _resourceManager.UpdateAsync(resource, cancellationToken).ConfigureAwait(false);
                _waitingQueue.TryRemove(resource.Id, out _);
                OnResourceError?.Invoke(this, new ResourceErrorEventArgs { Resource = resource, Exception = new InvalidOperationException(resource.LastError) });
                return;
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
                    EnableOcr = _processingConfigurationProvider.EnableOcr,
                    EnableAudioTranscription = _processingConfigurationProvider.EnableAudioTranscription,
                    CapabilityApi = _processingConfigurationProvider.CapabilityApi,
                    PluginOptions = _processingConfigurationProvider.GetPluginOptions(converter.Name, resource.MimeType)
                };

                var conversion = await converter
                    .ConvertToFriendlyAsync(activePath, convertOptions, cancellationToken)
                    .ConfigureAwait(false);

                resource.ProcessedFilePath = conversion.ProcessedFilePath ?? activePath;
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
                resource.Summary = aiResult.Summary;
                resource.AutoTags = aiResult.Tags;
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
                _ = Task.Run(async () =>
                {
                    await _syncSemaphore.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        await _cloudSyncProvider.UploadAsync(resource).ConfigureAwait(false);
                    }
                    catch (Exception syncEx)
                    {
                        _logger?.LogError($"资源同步失败: {resource.Id}", syncEx);
                    }
                    finally
                    {
                        _syncSemaphore.Release();
                    }
                });
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
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(duration, pending.CancellationSource.Token).ConfigureAwait(false);

                if (IsSuppressed(pending.Resource.Id))
                {
                    return;
                }

                await ExecutePipelineAsync(pending.Resource, pending.CancellationSource.Token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                _logger?.LogInfo($"等待态已取消: {pending.Resource.Id}");
            }
        }, CancellationToken.None);
    }

    private bool IsSuppressed(Guid resourceId)
    {
        return _suppressedResourceIds.ContainsKey(resourceId);
    }
}