using System;
using ResourceRouter.Core.Models;

namespace ResourceRouter.App.Views;

public sealed class ResourceEventArgs : EventArgs
{
    public required Resource Resource { get; init; }
}