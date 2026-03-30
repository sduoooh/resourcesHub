using System;

namespace ResourceRouter.App.State;

public sealed class DropIngressTimingPolicy
{
    public TimeSpan SuppressDuringDragOut { get; init; } = AppInteractionDefaults.DropIngress.SuppressDuringDragOut;

    public TimeSpan SuppressAfterDragOut { get; init; } = AppInteractionDefaults.DropIngress.SuppressAfterDragOut;
}