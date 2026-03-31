using System;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Services;

public sealed class DefaultSyncOrchestrator : ISyncOrchestrator, IDisposable
{
    private readonly ICloudSyncProvider _cloudSyncProvider;
    private readonly IAppLogger? _logger;
    private readonly SemaphoreSlim _syncSemaphore;

    public DefaultSyncOrchestrator(
        ICloudSyncProvider cloudSyncProvider,
        IAppLogger? logger = null,
        int maxConcurrentSync = 1)
    {
        _cloudSyncProvider = cloudSyncProvider;
        _logger = logger;
        var concurrency = Math.Max(1, maxConcurrentSync);
        _syncSemaphore = new SemaphoreSlim(concurrency, concurrency);
    }

    public void EnqueueUpload(Resource resource, CancellationToken cancellationToken = default)
    {
        _ = Task.Run(async () =>
        {
            await _syncSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _cloudSyncProvider.UploadAsync(resource, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"资源同步失败: {resource.Id}", ex);
            }
            finally
            {
                _syncSemaphore.Release();
            }
        }, CancellationToken.None);
    }

    public void Dispose()
    {
        _syncSemaphore.Dispose();
    }
}