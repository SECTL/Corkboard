using System;

namespace Corkboard.Core.Imaging;

/// <summary>
///     图标母图的缩小：按「面积平均」（box filter）把整张位图缩成正方形的一帧。
/// </summary>
/// <remarks>
///     <para>
///         为什么自己算而不用 <c>Bitmap.CreateScaledBitmap</c> / 位图库的双三次：任务栏图标要从
///         一千多像素的母图一路缩到 24～36 像素（缩 30～50 倍），双三次只取 4×4 邻域，
///         等于在一千多个像素里**抽点**，细线直接抽没、边缘一圈锯齿；面积平均是把目标像素
///         盖住的那一整块源像素**全部平均**掉，缩多少倍都不会漏点。
///     </para>
///     <para>
///         输入输出都是**预乘 alpha** 的 BGRA（Avalonia 的位图就是预乘的）：
///         透明像素的 RGB 在预乘空间里是 0，直接平均才不会把「透明的黑」混进边上，
///         否则白色圆角会带一圈脏边。写完 ICO 之前再反预乘（见 <see cref="IcoFile" />）。
///     </para>
/// </remarks>
public static class IconRaster
{
    /// <summary>每像素字节数（BGRA）。</summary>
    public const int BytesPerPixel = 4;

    /// <summary>
    ///     把 <paramref name="premultipliedBgra" />（<paramref name="width" />×<paramref name="height" />）
    ///     缩成 <paramref name="size" />×<paramref name="size" /> 的一帧，返回预乘 BGRA。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">尺寸或缓冲区长度不合法。</exception>
    public static byte[] ScaleSquare(ReadOnlySpan<byte> premultipliedBgra, int width, int height, int size)
    {
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "目标尺寸必须为正。");
        }

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "源图尺寸必须为正。");
        }

        var required = (long)width * height * BytesPerPixel;
        if (premultipliedBgra.Length < required)
        {
            throw new ArgumentException($"源像素缓冲区不足：需要 {required} 字节，实际 {premultipliedBgra.Length}。",
                nameof(premultipliedBgra));
        }

        var target = new byte[size * size * BytesPerPixel];
        for (var ty = 0; ty < size; ty++)
        {
            var y0 = (int)((long)ty * height / size);
            var y1 = (int)((long)(ty + 1) * height / size);
            if (y1 <= y0)
            {
                y1 = Math.Min(y0 + 1, height);
            }

            for (var tx = 0; tx < size; tx++)
            {
                var x0 = (int)((long)tx * width / size);
                var x1 = (int)((long)(tx + 1) * width / size);
                if (x1 <= x0)
                {
                    x1 = Math.Min(x0 + 1, width);
                }

                long b = 0, g = 0, r = 0, a = 0;
                var count = 0;
                for (var y = y0; y < y1; y++)
                {
                    var rowStart = y * width * BytesPerPixel;
                    for (var x = x0; x < x1; x++)
                    {
                        var i = rowStart + x * BytesPerPixel;
                        b += premultipliedBgra[i];
                        g += premultipliedBgra[i + 1];
                        r += premultipliedBgra[i + 2];
                        a += premultipliedBgra[i + 3];
                        count++;
                    }
                }

                var o = (ty * size + tx) * BytesPerPixel;
                target[o] = Divide(b, count);
                target[o + 1] = Divide(g, count);
                target[o + 2] = Divide(r, count);
                target[o + 3] = Divide(a, count);
            }
        }

        return target;
    }

    /// <summary>四舍五入的平均值（<c>sum / count</c>）。</summary>
    private static byte Divide(long sum, int count) => (byte)((sum + count / 2) / count);
}
