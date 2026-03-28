using System;

namespace ResourceRouter.App.Views;

public sealed class TagToggleEventArgs : EventArgs
{
    public required string Tag { get; init; }

    public bool IsSelected { get; init; }
}
