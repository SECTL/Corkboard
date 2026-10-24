using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Corkboard.Core.Imaging;

/// <summary>ICO 容器里的一帧：<paramref name="Size" /> 像素见方，<paramref name="PremultipliedBgra" /> 为预乘 BGRA。</summary>
/// <param name="Size">边长（1～256）。</param>
/// <param name="PremultipliedBgra">预乘 alpha 的 BGRA 像素，长度必须是 <c>Size * Size * 4</c>。</param>
public readonly record struct IcoFrame(int Size, byte[] PremultipliedBgra);

/// <summary>
///     把若干张位图拼成一份 ICO（Windows 的多尺寸图标容器）。
/// </summary>
/// <remarks>
///     <para>
///         为什么要拼 ICO 而不是直接把 <c>Bitmap</c> 交给 <c>WindowIcon</c>：
///         Avalonia 只有在拿到**真正的 ICO 字节**时才会按 DPI 从里面挑最接近的一帧
///         （<c>IconImpl.LoadSmallIcon / LoadBigIcon</c> 走 <c>CreateIconFromResourceEx</c>）；
///         给它一张位图时，它只会按位图原始尺寸造一个 HICON（我们的母图 1254×1254），
///         任务栏再把这个巨型 HICON 硬缩到 36 像素，于是糊成一片。
///     </para>
///     <para>
///         每帧是一个 32bpp 的 DIB（<c>BITMAPINFOHEADER</c> + 自下而上的 BGRA + 1bpp AND 掩码），
///         这正是 Vista 以来 ICO 里放 alpha 通道的标准写法：像素存**非预乘** BGRA，
///         全透明像素在 AND 掩码里置 1（老程序不认 alpha 时靠它抠图）。
///     </para>
/// </remarks>
public static class IcoFile
{
    /// <summary>ICO 目录项里的边长上限，256 记作 0。</summary>
    public const int MaxFrameSize = 256;

    private const int HeaderSize = 6;
    private const int EntrySize = 16;
    private const int DibHeaderSize = 40;

    /// <summary>
    ///     默认帧尺寸：Windows 任务栏/标题栏按 DPI 请求的是 <c>16 × 缩放</c>（小图标）与
    ///     <c>24 × 缩放</c>（大图标，Win10+），100%～300% 就是 16/20/24/28/32/36/40/48 与
    ///     24/30/36/42/48/54/60/72；Avalonia 挑帧时「最接近的比请求的小」还会退到
    ///     <c>32 × 缩放</c> 再挑一次。这里把这些尺寸全备上，外加 64/96/128 兜底，
    ///     任务栏才能拿到**恰好那么大**的一帧，做到 1:1 不缩放。
    /// </summary>
    public static IReadOnlyList<int> DefaultFrameSizes { get; } = new[]
    {
        16, 20, 24, 28, 30, 32, 36, 40, 42, 48, 54, 60, 64, 72, 96, 128
    };

    /// <summary>拼出 ICO 字节。</summary>
    /// <exception cref="ArgumentException">帧列表为空、尺寸重复或像素长度不符。</exception>
    public static byte[] Write(IReadOnlyList<IcoFrame> frames)
    {
        if (frames.Count is 0 or > 255)
        {
            throw new ArgumentException("ICO 需要 1～255 帧。", nameof(frames));
        }

        var seen = new HashSet<int>();
        var offsets = new int[frames.Count];
        var sizes = new int[frames.Count];
        var total = HeaderSize + EntrySize * frames.Count;

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            if (frame.Size is <= 0 or > MaxFrameSize)
            {
                throw new ArgumentException($"帧尺寸必须在 1～{MaxFrameSize}：{frame.Size}。", nameof(frames));
            }

            if (!seen.Add(frame.Size))
            {
                throw new ArgumentException($"帧尺寸重复：{frame.Size}。", nameof(frames));
            }

            if (frame.PremultipliedBgra is null ||
                frame.PremultipliedBgra.Length != frame.Size * frame.Size * IconRaster.BytesPerPixel)
            {
                throw new ArgumentException($"帧 {frame.Size} 的像素长度应为 {frame.Size * frame.Size * 4}。",
                    nameof(frames));
            }

            offsets[i] = total;
            sizes[i] = DibHeaderSize + frame.Size * frame.Size * IconRaster.BytesPerPixel
                       + MaskStride(frame.Size) * frame.Size;
            total += sizes[i];
        }

        var buffer = new byte[total];
        var span = buffer.AsSpan();

        // ICONDIR
        BinaryPrimitives.WriteUInt16LittleEndian(span[..2], 0); // reserved
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..4], 1); // type = icon
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..6], (ushort)frames.Count);

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            var entry = span.Slice(HeaderSize + EntrySize * i, EntrySize);
            entry[0] = (byte)(frame.Size >= MaxFrameSize ? 0 : frame.Size);
            entry[1] = (byte)(frame.Size >= MaxFrameSize ? 0 : frame.Size);
            entry[2] = 0; // 调色板颜色数：真彩为 0
            entry[3] = 0; // reserved
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..6], 1); // planes
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..8], 32); // bit count
            BinaryPrimitives.WriteInt32LittleEndian(entry[8..12], sizes[i]);
            BinaryPrimitives.WriteInt32LittleEndian(entry[12..16], offsets[i]);

            WriteDib(span.Slice(offsets[i], sizes[i]), frame);
        }

        return buffer;
    }

    /// <summary>写一帧的 DIB：头 + 自下而上的 BGRA + 自下而上的 1bpp 掩码。</summary>
    private static void WriteDib(Span<byte> target, IcoFrame frame)
    {
        var size = frame.Size;
        var xorSize = size * size * IconRaster.BytesPerPixel;
        var maskStride = MaskStride(size);
        var maskSize = maskStride * size;

        // BITMAPINFOHEADER
        BinaryPrimitives.WriteInt32LittleEndian(target[0..4], DibHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(target[4..8], size); // biWidth
        BinaryPrimitives.WriteInt32LittleEndian(target[8..12], size * 2); // biHeight = XOR + AND
        BinaryPrimitives.WriteUInt16LittleEndian(target[12..14], 1); // biPlanes
        BinaryPrimitives.WriteUInt16LittleEndian(target[14..16], 32); // biBitCount
        BinaryPrimitives.WriteInt32LittleEndian(target[16..20], 0); // BI_RGB
        BinaryPrimitives.WriteInt32LittleEndian(target[20..24], xorSize + maskSize); // biSizeImage

        var pixels = frame.PremultipliedBgra;
        var xor = target.Slice(DibHeaderSize, xorSize);
        var mask = target.Slice(DibHeaderSize + xorSize, maskSize);

        for (var y = 0; y < size; y++)
        {
            var sourceRow = (size - 1 - y) * size * IconRaster.BytesPerPixel; // 自下而上
            var targetRow = y * size * IconRaster.BytesPerPixel;
            var maskRow = y * maskStride;
            for (var x = 0; x < size; x++)
            {
                var i = sourceRow + x * IconRaster.BytesPerPixel;
                var o = targetRow + x * IconRaster.BytesPerPixel;
                var alpha = pixels[i + 3];
                // ICO 里存非预乘 BGRA；预乘的透明像素 RGB 本来就是 0，这里一并除回去。
                xor[o] = Unpremultiply(pixels[i], alpha);
                xor[o + 1] = Unpremultiply(pixels[i + 1], alpha);
                xor[o + 2] = Unpremultiply(pixels[i + 2], alpha);
                xor[o + 3] = alpha;

                if (alpha == 0)
                {
                    mask[maskRow + (x >> 3)] |= (byte)(0x80 >> (x & 7)); // 1 = 透明
                }
            }
        }
    }

    /// <summary>AND 掩码每行的字节数（按 4 字节对齐）。</summary>
    private static int MaskStride(int size) => (size + 31) / 32 * 4;

    private static byte Unpremultiply(byte value, byte alpha)
    {
        if (alpha == 0 || value == 0)
        {
            return 0;
        }

        if (alpha == 255)
        {
            return value;
        }

        var straight = (value * 255 + alpha / 2) / alpha;
        return (byte)Math.Min(255, straight);
    }
}
