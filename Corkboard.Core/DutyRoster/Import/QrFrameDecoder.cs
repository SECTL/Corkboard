using ZXing;
using ZXing.Common;

namespace Corkboard.Core.DutyRoster.Import;

/// <summary>帧像素的排列方式（相机与截图各用各的，这里只负责翻译给 ZXing）。</summary>
public enum QrPixelFormat
{
    /// <summary>8 位灰度，每像素 1 字节。</summary>
    Gray8 = 0,

    /// <summary>RGB 24 位，每像素 3 字节。</summary>
    Rgb24 = 1,

    /// <summary>BGR 24 位（OpenCV Mat 的默认排列），每像素 3 字节。</summary>
    Bgr24 = 2,

    /// <summary>RGBA 32 位，每像素 4 字节。</summary>
    Rgba32 = 3,

    /// <summary>BGRA 32 位（Avalonia WriteableBitmap 的默认排列），每像素 4 字节。</summary>
    Bgra32 = 4
}

/// <summary>
///     从一帧像素里解出二维码文本（只认 QR）。
///     <para>
///         ZXing 是纯托管库、不依赖图片解码器，所以相机帧可以按「Mat → 像素数组」直接喂进来，
///         不必先编码成 PNG 再解回来。相机预览与照片导入共用这一个入口；解析帧协议
///         （<see cref="OfflineQrProtocol" />）仍是另一层的事，这里只负责「这帧里有没有二维码、文本是什么」。
///     </para>
///     <para>
///         解码器是共享的，调用处必须串行（相机线程只有一个，导入预览也是一个）；这里用锁兜底，
///         不要在多线程里并发调同一帧。
///     </para>
/// </summary>
public static class QrFrameDecoder
{
    private static readonly object Sync = new();

    private static readonly BarcodeReaderGeneric Reader = new()
    {
        // 手机斜着扫、相机装反都可能，交给 ZXing 自己转四次。
        AutoRotate = true,
        Options = new DecodingOptions
        {
            PossibleFormats = [BarcodeFormat.QR_CODE],
            TryHarder = true
        }
    };

    /// <summary>尝试解码一帧；失败返回 <see langword="false" />，不抛异常。</summary>
    /// <param name="pixels">按 <paramref name="format" /> 排列的像素字节。</param>
    /// <param name="width">像素宽。</param>
    /// <param name="height">像素高。</param>
    /// <param name="format">像素排列方式。</param>
    /// <param name="text">解出来的二维码文本（含 SecRandom 帧前缀）。</param>
    public static bool TryDecode(
        byte[]? pixels,
        int width,
        int height,
        QrPixelFormat format,
        out string text)
    {
        text = string.Empty;
        if (pixels is null || width <= 0 || height <= 0)
            return false;

        var bytesPerPixel = format switch
        {
            QrPixelFormat.Gray8 => 1,
            QrPixelFormat.Rgb24 or QrPixelFormat.Bgr24 => 3,
            _ => 4
        };
        if (pixels.Length < (long)width * height * bytesPerPixel)
            return false;

        var bitmapFormat = format switch
        {
            QrPixelFormat.Gray8 => RGBLuminanceSource.BitmapFormat.Gray8,
            QrPixelFormat.Rgb24 => RGBLuminanceSource.BitmapFormat.RGB24,
            QrPixelFormat.Bgr24 => RGBLuminanceSource.BitmapFormat.BGR24,
            QrPixelFormat.Rgba32 => RGBLuminanceSource.BitmapFormat.RGBA32,
            _ => RGBLuminanceSource.BitmapFormat.BGRA32
        };

        try
        {
            // BarcodeReaderGeneric 不是线程安全的；相机回调与手动预览可能撞在一起，统一加锁。
            lock (Sync)
            {
                var result = Reader.Decode(pixels, width, height, bitmapFormat);
                if (result is null || string.IsNullOrEmpty(result.Text))
                    return false;

                text = result.Text;
                return true;
            }
        }
        catch (Exception)
        {
            // 相机帧里的噪声不该把导入流程打断；解不出来就等下一帧。
            return false;
        }
    }
}