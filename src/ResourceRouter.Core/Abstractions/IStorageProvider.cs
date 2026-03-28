using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Abstractions;

public interface IStorageProvider
{
    Task<Resource> StoreRawAsync(
        RawDropData dropData,
        ResourceIngestOptions options,
        CancellationToken cancellationToken = default);
}