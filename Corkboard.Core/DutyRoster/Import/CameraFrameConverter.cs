namespace Corkboard.Core.DutyRoster.Import;

/// <summary>
///     相机帧的像素排列转换。
///     <para>
///         平台层（<c>OpenCvCameraScanner</c>）出的是 OpenCV 的紧凑 <b>BGR24</b>：
///         二维码解码器（<see cref="QrFrameDecoder" />）直接吃这一份，
///         但 Avalonia 的 <c>WriteableBitmap</c> 要 <b>BGRA32</b>（预乘与否在显示上等价，alpha 恒 255）。
///         两边的换算只写在这里，预览控件与将来的导出/抓拍都复用它，别各自再写一遍。
///     </para>
///     <para>
///         纯托管、无第三方依赖（不引 ImageSharp / Skia）：一帧 720p 只是 300 万次字节搬运，
///         比走一遍编码解码便宜得多。
///     </para>
/// </summary>
public static class CameraFrameConverter
{
    /// <summary>
    ///     把 BGR24 一帧换成 BGRA32（alpha 一律 255），按行优先、无行填充。
    /// </summary>
    /// <param name="bgr">BGR24 像素，长度至少 <c>width * height * 3</c>；多出来的尾巴忽略。</param>
    /// <param name="width">像素宽。</param>
    /// <param name="height">像素高。</param>
    /// <returns>新分配的 BGRA32 像素，长度正好 <c>width * height * 4</c>。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bgr" /> 为 <see langword="null" />。</exception>
    /// <exception cref="ArgumentException">
    ///     宽或高不为正，或 <paramref name="bgr" /> 短于 <c>width * height * 3</c>。
    /// </exception>
    public static byte[] ToBgra32(byte[] bgr, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(bgr);
        var bgra = new byte[Validate(width, height, bgr.LongLength)];
        ToBgra32(bgr, width, height, bgra);
        return bgra;
    }

    /// <summary>
    ///     同上，但写进调用方给的缓冲区——预览每帧复用同一块内存时用这个重载，
    ///     免得 10fps × 720p 每秒分配三十多 MB 直接喂给 GC。
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     宽高不为正、<paramref name="bgr" /> 短于一帧，或 <paramref name="bgra" /> 放不下一帧。
    /// </exception>
    public static void ToBgra32(ReadOnlySpan<byte> bgr, int width, int height, Span<byte> bgra)
    {
        var bgraLength = Validate(width, height, bgr.Length);
        if (bgra.Length < bgraLength)
            throw new ArgumentException($"BGRA32 缓冲区太小：需要 {bgraLength} 字节，实际 {bgra.Length} 字节", nameof(bgra));

        var source = 0;
        var target = 0;
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                // BGR24 → BGRA32：B、G、R 原地顺序不变，只在尾部补一个不透明 alpha。
                bgra[target] = bgr[source];
                bgra[target + 1] = bgr[source + 1];
                bgra[target + 2] = bgr[source + 2];
                bgra[target + 3] = 255;
                source += 3;
                target += 4;
            }
        }
    }

    /// <summary>
    ///     校验宽高并算出 BGRA32 的字节数（= 新数组的长度）。
    ///     用 <see cref="long" /> 算乘积：调用方给的宽高相乘在极端值上会溢出成负数，
    ///     长度校验就被绕过、下面直接越界。
    /// </summary>
    private static int Validate(int width, int height, long bgrLength)
    {
        if (width <= 0)
            throw new ArgumentException("帧宽度必须为正数", nameof(width));
        if (height <= 0)
            throw new ArgumentException("帧高度必须为正数", nameof(height));

        var expected = (long)width * height * 3;
        if (bgrLength < expected)
            throw new ArgumentException($"BGR24 帧长度不足：需要 {expected} 字节，实际 {bgrLength} 字节", "bgr");

        return checked((int)((long)width * height * 4));
    }
}