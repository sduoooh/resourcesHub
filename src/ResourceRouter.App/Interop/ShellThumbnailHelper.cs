using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ResourceRouter.App.Interop;

internal static class ShellThumbnailHelper
{
    private const uint SiigbfBiggerSizeOk = 0x1;
    private const uint SiigbfThumbnailOnly = 0x8;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string path,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory shellItem);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    public static Bitmap? TryGetThumbnailBitmap(string filePath, int size)
    {
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(filePath, IntPtr.Zero, iid, out var factory);

            var nativeSize = new NativeSize { Width = size, Height = size };
            factory.GetImage(nativeSize, SiigbfThumbnailOnly | SiigbfBiggerSizeOk, out var hBitmap);

            if (hBitmap == IntPtr.Zero)
            {
                return null;
            }

            using var tempBitmap = Image.FromHbitmap(hBitmap);
            var cloned = new Bitmap(tempBitmap);
            DeleteObject(hBitmap);
            return cloned;
        }
        catch
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    private interface IShellItemImageFactory
    {
        void GetImage(NativeSize size, uint flags, out IntPtr phbm);
    }
}