using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Abstractions;

public interface IResourceHealthMonitor
{
    Task<ResourceHealthReport> ProbeDropAsync(RawDropData dropData, CancellationToken cancellationToken = default);

    Task<ResourceHealthReport> ProbeResourceAsync(Resource resource, CancellationToken cancellationToken = default);
}
