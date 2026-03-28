using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Services.NoOp;

public sealed class NoOpResourceFeatureExtractor : IResourceFeatureExtractor
{
    public Task<string?> ExtractFromDropAsync(RawDropData dropData, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }

    public Task<string?> ExtractFromResourceAsync(Resource resource, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }
}
