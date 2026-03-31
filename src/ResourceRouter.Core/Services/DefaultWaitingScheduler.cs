using System;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Abstractions;

namespace ResourceRouter.Core.Services;

public sealed class DefaultWaitingScheduler : IWaitingScheduler
{
    private readonly IAppLogger? _logger;

    public DefaultWaitingScheduler(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    public void Schedule(
        Guid resourceId,
        TimeSpan delay,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task> onElapsedAsync)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                await onElapsedAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                _logger?.LogInfo($"等待态已取消: {resourceId}");
            }
        }, CancellationToken.None);
    }
}