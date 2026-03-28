using System;
using ResourceRouter.Core.Abstractions;

namespace ResourceRouter.App.Interop.Ole;

internal sealed class OleDropTargetRegistration : IDisposable
{
    private readonly IntPtr _windowHandle;
    private readonly IOleDropTarget _dropTarget;
    private readonly IAppLogger? _logger;
    private bool _isRegistered;

    private OleDropTargetRegistration(IntPtr windowHandle, IOleDropTarget dropTarget, IAppLogger? logger)
    {
        _windowHandle = windowHandle;
        _dropTarget = dropTarget;
        _logger = logger;
    }

    public static OleDropTargetRegistration? TryRegister(
        IntPtr windowHandle,
        Action? onDragEnter,
        Action? onDragLeave,
        Action<System.Windows.IDataObject>? onDrop,
        IAppLogger? logger)
    {
        OleComInterop.OleInitialize(IntPtr.Zero);

        var adapter = new OleDropTargetAdapter(onDragEnter, onDragLeave, onDrop);
        var registration = new OleDropTargetRegistration(windowHandle, adapter, logger);

        var hr = OleComInterop.RegisterDragDrop(windowHandle, adapter);
        if (hr != 0)
        {
            logger?.LogWarning($"RegisterDragDrop 失败，HRESULT: 0x{hr:X8}");
            return null;
        }

        registration._isRegistered = true;
        logger?.LogInfo("已启用 COM IDropTarget 增强管线。");
        return registration;
    }

    public void Dispose()
    {
        if (!_isRegistered)
        {
            return;
        }

        var hr = OleComInterop.RevokeDragDrop(_windowHandle);
        if (hr != 0)
        {
            _logger?.LogWarning($"RevokeDragDrop 返回异常 HRESULT: 0x{hr:X8}");
        }

        _isRegistered = false;
        GC.KeepAlive(_dropTarget);
    }
}