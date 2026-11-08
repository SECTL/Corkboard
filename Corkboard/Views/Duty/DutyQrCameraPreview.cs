using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Corkboard.Core.DutyRoster.Import;
using Corkboard.Platforms.Abstractions;

namespace Corkboard.Views.Duty;

/// <summary>
///     相机扫码的预览画面。
///     <para>
///         相机帧从平台层出来是 BGR24（<c>CameraFrame</c>），这里换成 BGRA32 塞进一块
///         <b>复用</b>的 <c>WriteableBitmap</c>：10fps 的 720p 每帧约 3.7MB，
///         如果每帧都新建位图，光是分配与倒腾就能把 GC 压出明显停顿。
///         所以只有<b>分辨率变了</b>才重建位图，其余每帧只往同一块像素缓冲区里覆盖。
///     </para>
///     <para>
///         线程约定：<see cref="ShowFrame" /> 与 <see cref="Clear" /> 都必须在 UI 线程（Dispatcher）上调用。
///         采集线程在平台层，那边只负责发事件，转发到这边的责任留给调用方——
///         控件自己不订阅相机、也不做线程调度，这样它既能给相机用，也能给截图预览用。
///     </para>
/// </summary>
public sealed class DutyQrCameraPreview : ContentControl, IDisposable
{
    private readonly Image _image = new()
    {
        // 整帧都要看得见（扫码时用户要自己把二维码框进画面），所以是等比缩放、不裁剪。
        Stretch = Stretch.Uniform,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
    };

    private WriteableBitmap? _bitmap;
    private byte[] _scratch = [];
    private int _bitmapWidth;
    private int _bitmapHeight;
    private bool _disposed;

    public DutyQrCameraPreview()
    {
        Content = _image;
    }

    /// <summary>显示一帧。调用方必须已切到 UI 线程；尺寸没变时不会重建位图。</summary>
    /// <exception cref="ArgumentException">帧尺寸不合法（宽或高不为正），或像素数组短于一帧。</exception>
    public void ShowFrame(CameraFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_disposed)
            return;

        var width = frame.Width;
        var height = frame.Height;
        if (width <= 0 || height <= 0)
            throw new ArgumentException("相机帧尺寸必须为正数", nameof(frame));

        var bgra = frame.Format switch
        {
            // 平台层正常只发 BGR24；BGRA32 顺手认了，省得将来换实现时两边对不上。
            CameraPixelFormat.Bgr24 => ConvertBgr24(frame.Pixels, width, height),
            CameraPixelFormat.Bgra32 => CopyBgra32(frame.Pixels, width, height),
            _ => null
        };
        if (bgra is null)
            return;

        if (_bitmap is null || _bitmapWidth != width || _bitmapHeight != height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                // 像素里 alpha 恒 255，预乘与不预乘在显示上等价；用 Unpremul 与解码器/写出的字节语义一致。
                AlphaFormat.Unpremul);
            _bitmapWidth = width;
            _bitmapHeight = height;
            _image.Source = _bitmap;
        }

        using (var buffer = _bitmap.Lock())
        {
            // 位图行距不一定等于 width*4（实现可能按 4 字节对齐追加填充），逐行拷才安全。
            var sourceRowBytes = width * 4;
            var destinationRowBytes = buffer.RowBytes;
            if (destinationRowBytes == sourceRowBytes)
            {
                Marshal.Copy(bgra, 0, buffer.Address, sourceRowBytes * height);
            }
            else
            {
                for (var row = 0; row < height; row++)
                    Marshal.Copy(bgra, row * sourceRowBytes, IntPtr.Add(buffer.Address, row * destinationRowBytes),
                        sourceRowBytes);
            }
        }

        // WriteableBitmap 内容变了要显式通知装饰层重绘（同一块位图反复写，Avalonia 不会自己发现）。
        _image.InvalidateVisual();
    }

    /// <summary>清空画面并释放位图（相机停下后调用，别把几 MB 的像素缓冲一直挂着）。</summary>
    public void Clear()
    {
        if (_disposed)
            return;

        _image.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        _bitmapWidth = 0;
        _bitmapHeight = 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Clear();
    }

    /// <summary>把 BGR24 换进复用缓冲区（尺寸不变就不重新分配）。</summary>
    private byte[] ConvertBgr24(byte[] pixels, int width, int height)
    {
        var length = checked((int)((long)width * height * 4));
        if (_scratch.Length != length)
            _scratch = new byte[length];

        CameraFrameConverter.ToBgra32(pixels, width, height, _scratch);
        return _scratch;
    }

    private static byte[]? CopyBgra32(byte[] pixels, int width, int height)
    {
        var expected = (long)width * height * 4;
        if (pixels.LongLength < expected)
            throw new ArgumentException($"BGRA32 帧长度不足：需要 {expected} 字节，实际 {pixels.LongLength} 字节",
                nameof(pixels));

        // 尺寸对得上就直接用调用方的数组；不对（带尾巴）才截一份。
        return pixels.LongLength == expected ? pixels : pixels[..(int)expected];
    }
}