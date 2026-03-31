using System;
using System.Threading;
using System.Threading.Tasks;

namespace ResourceRouter.Core.Abstractions;

public interface IWaitingScheduler
{
    void Schedule(
        Guid resourceId,
        TimeSpan delay,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task> onElapsedAsync);
}