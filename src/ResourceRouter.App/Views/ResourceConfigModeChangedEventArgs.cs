using System;
using ResourceRouter.Core.Models;

namespace ResourceRouter.App.Views;

public sealed class ResourceConfigModeChangedEventArgs : EventArgs
{
    public required Resource Resource { get; init; }

    public required bool IsOpen { get; init; }
}
