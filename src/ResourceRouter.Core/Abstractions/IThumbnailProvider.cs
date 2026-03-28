using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Abstractions;

public interface IThumbnailProvider
{
    Task<string?> GenerateAsync(Resource resource, CancellationToken cancellationToken = default);
}