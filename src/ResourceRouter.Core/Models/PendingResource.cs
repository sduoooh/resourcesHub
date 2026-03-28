using System;
using System.Threading;

namespace ResourceRouter.Core.Models;

public sealed class PendingResource
{
    public required Resource Resource { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public required CancellationTokenSource CancellationSource { get; init; }
}