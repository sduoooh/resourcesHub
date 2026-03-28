using System;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Events;

public sealed class ResourceUpdatedEventArgs : EventArgs
{
    public required Resource Resource { get; init; }
}
