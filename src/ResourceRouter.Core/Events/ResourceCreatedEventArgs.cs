using System;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Events;

public sealed class ResourceCreatedEventArgs : EventArgs
{
    public required Resource Resource { get; init; }
}
