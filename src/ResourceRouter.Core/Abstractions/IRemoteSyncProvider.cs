using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Abstractions;

public interface IRemoteSyncProvider
{
    Task PushAsync(Resource resource, CancellationToken cancellationToken = default);
}
