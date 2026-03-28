using System;

namespace ResourceRouter.Core.Events;

public sealed class ResourceDeletedEventArgs : EventArgs
{
    public required Guid ResourceId { get; init; }
}
