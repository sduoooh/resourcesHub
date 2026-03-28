using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Infrastructure.Sync;

public sealed class NoOpCloudSyncProvider : ICloudSyncProvider
{
    public Task UploadAsync(Resource resource, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}