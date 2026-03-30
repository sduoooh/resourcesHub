using System;
using ResourceRouter.Core.Models;

namespace ResourceRouter.App.Views;

public sealed class ManualInputSubmittedEventArgs : EventArgs
{
    public required RawDropData RawDropData { get; init; }

    public string? TitleOverride { get; init; }
}