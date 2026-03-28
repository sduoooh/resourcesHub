using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Abstractions;

public interface IResourceFeatureExtractor
{
    Task<string?> ExtractFromDropAsync(RawDropData dropData, CancellationToken cancellationToken = default);

    Task<string?> ExtractFromResourceAsync(Resource resource, CancellationToken cancellationToken = default);
}
