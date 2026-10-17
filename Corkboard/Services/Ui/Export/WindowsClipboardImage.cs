using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Corkboard.Services.Ui.Export;

/// <summary>
///     Windows 剪贴板放位图（CF_DIB）的兜底实现。
/// </summary>
/// <remarks>
///     Avalonia 自己的剪贴板在 Windows 上走 OLE 延迟渲染：<c>SetBitmapAsync</c> 返回之后数据还没真正写进剪贴板，
///     等粘贴时才去渲染，而我们的位图那时候已经 <c>Dispose</c> 了，粘出来就是空（用户报「复制图片没成功」）。
///     这里直接把 BGRA 像素按 DIB 写进剪贴板，不依赖 OLE 的所有权/延迟渲染，粘贴方（聊天窗口、Word、画图）都认。
/// </remarks>
internal static class WindowsClipboardImage
{
    private const uint CfDib = 8;
    private const uint GmemMoveable = 0x0002;
    private const int BitmapInfoHeaderSize = 40;

    /// <summary>把位图放进剪贴板。成功返回 true；调用方拿 false 时再走 Avalonia 的剪贴板。</summary>
    public static bool TrySet(Bitmap bitmap, IntPtr ownerWindow)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;
        if (width <= 0 || height <= 0)
            return false;

        byte[] pixels;
        int rowBytes;
        using (var png = new MemoryStream())
        {
            bitmap.Save(png, PngBitmapEncoderOptions.Default);
            png.Position = 0;
            using var decoded = SKBitmap.Decode(png);
            if (decoded is null)
                return false;

            using var bgra = decoded.ColorType == SKColorType.Bgra8888
                ? decoded.Copy()
                : decoded.Copy(SKColorType.Bgra8888);
            if (bgra is null)
                return false;

            rowBytes = bgra.RowBytes;
            var pointer = bgra.GetPixels();
            pixels = new byte[rowBytes * height];
            Marshal.Copy(pointer, pixels, 0, pixels.Length);
        }

        var stride = width * 4;
        var imageSize = stride * height;
        var dibSize = BitmapInfoHeaderSize + imageSize;

        var dib = Marshal.AllocHGlobal(dibSize);
        try
        {
            Marshal.WriteInt32(dib, 0, BitmapInfoHeaderSize);
            Marshal.WriteInt32(dib, 4, width);
            Marshal.WriteInt32(dib, 8, height); // 正数 = 自下而上，与 DIB 行序一致
            Marshal.WriteInt16(dib, 12, 1); // biPlanes
            Marshal.WriteInt16(dib, 14, 32); // biBitCount
            Marshal.WriteInt32(dib, 16, 0); // BI_RGB
            Marshal.WriteInt32(dib, 20, imageSize);
            Marshal.WriteInt32(dib, 24, 0);
            Marshal.WriteInt32(dib, 28, 0);
            Marshal.WriteInt32(dib, 32, 0);
            Marshal.WriteInt32(dib, 36, 0);

            for (var y = 0; y < height; y++)
            {
                // DIB 第一行是图片最下面一行；源像素第一行是最上面一行。
                Marshal.Copy(pixels, (height - 1 - y) * rowBytes, dib + BitmapInfoHeaderSize + y * stride, stride);
            }

            // BI_RGB 的 32bpp 里 alpha 一般被忽略，但有些程序会拿它做透明，统一改成不透明。
            for (var offset = BitmapInfoHeaderSize + 3; offset < dibSize; offset += 4)
                Marshal.WriteByte(dib, offset, 0xFF);

            if (!TryOpenClipboard(ownerWindow))
                return false;

            try
            {
                if (!EmptyClipboard())
                    return false;

                var memory = GlobalAlloc(GmemMoveable, (UIntPtr)dibSize);
                if (memory == IntPtr.Zero)
                    return false;

                var locked = GlobalLock(memory);
                if (locked == IntPtr.Zero)
                {
                    GlobalFree(memory);
                    return false;
                }

                try
                {
                    CopyMemory(locked, dib, (UIntPtr)dibSize);
                }
                finally
                {
                    GlobalUnlock(memory);
                }

                if (SetClipboardData(CfDib, memory) == IntPtr.Zero)
                {
                    GlobalFree(memory);
                    return false;
                }

                // SetClipboardData 成功后这块内存归系统所有，不能再 GlobalFree。
                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }
        finally
        {
            Marshal.FreeHGlobal(dib);
        }
    }

    private static bool TryOpenClipboard(IntPtr ownerWindow)
    {
        // 剪贴板可能被别的程序短暂占用，重试几次比直接报错友好。
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(ownerWindow))
                return true;
            Thread.Sleep(40);
        }

        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr newOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void CopyMemory(IntPtr destination, IntPtr source, UIntPtr length);
}
