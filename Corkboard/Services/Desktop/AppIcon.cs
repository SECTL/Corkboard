using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Corkboard.Core.Imaging;

namespace Corkboard.Services.Desktop;

/// <summary>
///     应用图标的唯一来源：窗口（标题栏 / 任务栏）与托盘都从随包分发的 PNG 母图来。
/// </summary>
/// <remarks>
///     <para>
///         <b>为什么要绕一圈拼 ICO</b>：把 PNG 位图直接交给 <c>WindowIcon</c> 时，Avalonia 会按位图
///         原始尺寸造一个 HICON（母图 1254×1254 就是 1254 的 HICON），任务栏拿到后再把这个巨型图标
///         硬缩到 36 像素——缩 30 多倍又没有面积平均，结果就是糊的、边缘一圈锯齿（用户反馈
///         「任务栏图标像素不够高」）。上游 SecRandom-C 的做法是把 ICO 交给窗口
///         （<c>Icon="/Assets/AppLogo.ico"</c>），Avalonia 才会走 <c>CreateIconFromResourceEx</c>，
///         按 DPI 从容器里挑**最接近的一帧**。
///     </para>
///     <para>
///         我们沿这条路走，但**母图仍然只有 PNG 一份**：启动时按
///         <see cref="IcoFile.DefaultFrameSizes" />（含 100%～300% 缩放下的 16/24/30/36/42/48… 精确尺寸）
///         从 PNG 缩出各帧、拼成 ICO 再交给 <c>WindowIcon</c>。于是任务栏在 150% 缩放下拿到的就是
///         36×36 的一帧，1:1 贴上去，不用系统再缩放；换图标也只改 PNG。
///     </para>
///     <para>
///         exe 文件图标（<c>Corkboard.Desktop.csproj</c> 的 <c>ApplicationIcon</c>）仍然是 ICO——
///         那是 PE 资源，Windows 只认 ICO，和运行期窗口图标不是一回事。
///     </para>
///     <para>
///         <see cref="Bitmap" /> 不释放：部分平台的 <see cref="WindowIcon" /> 实现直接持有它，
///         提前释放会在画图标时炸掉，所以缓存到进程结束。
///     </para>
/// </remarks>
public static class AppIcon
{
    private const string PngAsset = "avares://Corkboard/Assets/Icons/Corkboard.png";

    private static Bitmap? _bitmap;
    private static WindowIcon? _windowIcon;

    /// <summary>
    ///     界面里直接画图标用的位图（标题栏、关于页）。<c>FAAppWindow.Icon</c> 的声明类型是
    ///     <see cref="Avalonia.Media.IImage" />（它把基类 <c>Window.Icon</c> 藏起来了），
    ///     那条路只会得到「一张位图」，**不要**拿它当窗口图标，见 <see cref="LoadWindowIcon" />。
    /// </summary>
    public static Bitmap LoadBitmap() =>
        _bitmap ??= new Bitmap(AssetLoader.Open(new Uri(PngAsset)));

    /// <summary>
    ///     窗口与托盘用的 <see cref="WindowIcon" />：从 PNG 母图现缩出一份多尺寸 ICO，
    ///     让 Avalonia / Windows 能按 DPI 取到**恰好那么大**的一帧。
    /// </summary>
    public static WindowIcon LoadWindowIcon() => _windowIcon ??= BuildWindowIcon();

    private static WindowIcon BuildWindowIcon()
    {
        try
        {
            return new WindowIcon(new MemoryStream(BuildIco(), false));
        }
        catch (Exception)
        {
            // 兜底：拼 ICO 出了岔子也不能让应用起不来，退回位图（Windows 会自己缩，糊一点但能显示）。
            return new WindowIcon(LoadBitmap());
        }
    }

    private static byte[] BuildIco()
    {
        var (pixels, width, height) = ReadMasterPixels();
        var frames = new List<IcoFrame>(IcoFile.DefaultFrameSizes.Count);
        foreach (var size in IcoFile.DefaultFrameSizes)
        {
            frames.Add(new IcoFrame(size, IconRaster.ScaleSquare(pixels, width, height, size)));
        }

        return IcoFile.Write(frames);
    }

    /// <summary>把母图读成预乘 BGRA（<see cref="IconRaster" /> 要的格式）。</summary>
    private static (byte[] Pixels, int Width, int Height) ReadMasterPixels()
    {
        var bitmap = LoadBitmap();
        var size = bitmap.PixelSize;
        var stride = size.Width * IconRaster.BytesPerPixel;
        var buffer = new byte[stride * size.Height];

        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), handle.AddrOfPinnedObject(),
                buffer.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        Normalize(buffer, bitmap.Format, bitmap.AlphaFormat);
        return (buffer, size.Width, size.Height);
    }

    /// <summary>
    ///     把 <see cref="Bitmap.CopyPixels(PixelRect, nint, int, int)" /> 拷出来的原始像素
    ///     就地整成「预乘 BGRA」——PNG 解出来通常是这个格式，但不同后端/解码器可能给
    ///     RGBA 顺序或非预乘，差一样颜色就会错（预乘差一次，白底的圆角会带脏边）。
    /// </summary>
    private static void Normalize(byte[] pixels, PixelFormat? format, AlphaFormat? alphaFormat)
    {
        if (format == PixelFormat.Rgba8888)
        {
            for (var i = 0; i + 3 < pixels.Length; i += IconRaster.BytesPerPixel)
            {
                (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
            }
        }

        switch (alphaFormat)
        {
            case AlphaFormat.Unpremul:
                for (var i = 0; i + 3 < pixels.Length; i += IconRaster.BytesPerPixel)
                {
                    var alpha = pixels[i + 3];
                    if (alpha == 255)
                    {
                        continue;
                    }

                    pixels[i] = Premultiply(pixels[i], alpha);
                    pixels[i + 1] = Premultiply(pixels[i + 1], alpha);
                    pixels[i + 2] = Premultiply(pixels[i + 2], alpha);
                }

                break;
            case AlphaFormat.Opaque:
                for (var i = 3; i < pixels.Length; i += IconRaster.BytesPerPixel)
                {
                    pixels[i] = 255;
                }

                break;
        }
    }

    private static byte Premultiply(byte value, byte alpha) => (byte)((value * alpha + 127) / 255);
}
