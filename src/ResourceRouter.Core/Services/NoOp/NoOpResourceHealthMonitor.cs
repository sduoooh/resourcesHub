using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Services.NoOp;

public sealed class NoOpResourceHealthMonitor : IResourceHealthMonitor
{
    public Task<ResourceHealthReport> ProbeDropAsync(RawDropData dropData, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ResourceHealthReport { IsHealthy = true });
    }

    public Task<ResourceHealthReport> ProbeResourceAsync(Resource resource, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ResourceHealthReport { IsHealthy = true });
    }
}
