using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Abstractions;

public interface IAIProvider
{
    Task<AIResult> AnalyzeAsync(Resource resource, CancellationToken cancellationToken = default);
}