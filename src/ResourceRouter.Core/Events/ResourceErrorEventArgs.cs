using System;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Events;

public sealed class ResourceErrorEventArgs : EventArgs
{
    public required Resource Resource { get; init; }

    public required Exception Exception { get; init; }
}