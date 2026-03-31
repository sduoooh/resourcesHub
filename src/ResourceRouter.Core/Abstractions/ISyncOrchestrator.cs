using System.Threading;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Abstractions;

public interface ISyncOrchestrator
{
    void EnqueueUpload(Resource resource, CancellationToken cancellationToken = default);
}