using System;
using System.Windows;

namespace ResourceRouter.App.Interop.Ole;

internal sealed class OleDropTargetAdapter : IOleDropTarget
{
    private readonly Action? _onDragEnter;
    private readonly Action? _onDragLeave;
    private readonly Action<IDataObject>? _onDrop;

    public OleDropTargetAdapter(Action? onDragEnter, Action? onDragLeave, Action<IDataObject>? onDrop)
    {
        _onDragEnter = onDragEnter;
        _onDragLeave = onDragLeave;
        _onDrop = onDrop;
    }

    public int DragEnter(
        System.Runtime.InteropServices.ComTypes.IDataObject dataObject,
        uint grfKeyState,
        PointL point,
        ref uint effect)
    {
        _onDragEnter?.Invoke();
        effect = OleComInterop.DropEffectCopy;
        return 0;
    }

    public int DragOver(uint grfKeyState, PointL point, ref uint effect)
    {
        effect = OleComInterop.DropEffectCopy;
        return 0;
    }

    public int DragLeave()
    {
        _onDragLeave?.Invoke();
        return 0;
    }

    public int Drop(
        System.Runtime.InteropServices.ComTypes.IDataObject dataObject,
        uint grfKeyState,
        PointL point,
        ref uint effect)
    {
        effect = OleComInterop.DropEffectCopy;

        var wpfDataObject = new DataObject(dataObject);
        _onDrop?.Invoke(wpfDataObject);
        return 0;
    }
}