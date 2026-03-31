using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Infrastructure.Storage;

public sealed class RawPayloadMaterializationContext
{
    public required string ResourceDirectory { get; init; }

    public required RawDropData DropData { get; init; }
}

public interface IRawPayloadMaterializer
{
    bool CanHandle(RawPayloadMaterializationContext context);

    Task<string> MaterializeAsync(RawPayloadMaterializationContext context, CancellationToken cancellationToken);
}
