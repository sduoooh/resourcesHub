using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Abstractions;

public interface ICloudSyncProvider
{
    Task UploadAsync(Resource resource, CancellationToken cancellationToken = default);
}