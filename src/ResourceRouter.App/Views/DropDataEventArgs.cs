using System;
using System.Windows;

namespace ResourceRouter.App.Views;

public sealed class DropDataEventArgs : EventArgs
{
    public required IDataObject DataObject { get; init; }
}