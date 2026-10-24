using System.Buffers.Binary;
using Corkboard.Core.Imaging;

namespace Corkboard.Core.Tests;

/// <summary>
///     <see cref="IcoFile" /> 的单测：拼出来的 ICO 要让 Windows 的
///     <c>CreateIconFromResourceEx</c> 认，帧表、DIB 头、自下而上的像素与 AND 掩码都得对。
/// </summary>
public class IcoFileTests
{
    private const int DibHeaderSize = 40;

    /// <summary>造一张纯色帧（预乘 BGRA）。</summary>
    private static IcoFrame SolidFrame(int size, byte b, byte g, byte r, byte a)
    {
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = a;
        }

        return new IcoFrame(size, pixels);
    }

    /// <summary>帧在 ICO 里的长度：DIB 头 + 32bpp 像素 + 4 字节对齐的 1bpp 掩码。</summary>
    private static int FrameLength(int size) => DibHeaderSize + size * size * 4 + ((size + 31) / 32 * 4) * size;

    /// <summary>单帧 ICO 的目录项与 DIB 头字段全部正确，且偏移正好接在目录后面。</summary>
    [Fact]
    public void Write_WritesHeaderAndSingleFrameDirectory()
    {
        var bytes = IcoFile.Write(new[] { SolidFrame(4, 255, 255, 255, 255) });

        Assert.Equal(6 + 16 + FrameLength(4), bytes.Length);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, 2))); // reserved
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2))); // type = icon
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2))); // 帧数

        var entry = bytes.AsSpan(6, 16);
        Assert.Equal(4, entry[0]);
        Assert.Equal(4, entry[1]);
        Assert.Equal(0, entry[2]); // 真彩没有调色板
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(entry[4..6])); // planes
        Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(entry[6..8])); // 32bpp
        Assert.Equal(FrameLength(4), BinaryPrimitives.ReadInt32LittleEndian(entry[8..12]));
        Assert.Equal(22, BinaryPrimitives.ReadInt32LittleEndian(entry[12..16])); // 紧接目录

        var dib = bytes.AsSpan(22);
        Assert.Equal(DibHeaderSize, BinaryPrimitives.ReadInt32LittleEndian(dib[0..4]));
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(dib[4..8])); // biWidth
        Assert.Equal(8, BinaryPrimitives.ReadInt32LittleEndian(dib[8..12])); // biHeight = XOR + AND
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(dib[12..14]));
        Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(dib[14..16]));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(dib[16..20])); // BI_RGB
        Assert.Equal(4 * 4 * 4 + 4 * 4, BinaryPrimitives.ReadInt32LittleEndian(dib[20..24]));
    }

    /// <summary>像素自下而上写：DIB 的第一行是源图最后一行。</summary>
    [Fact]
    public void Write_StoresRowsBottomUp()
    {
        // 2×2：顶行红、底行蓝（预乘后 alpha 不透明即原色）。
        var pixels = new byte[]
        {
            0, 0, 255, 255, 0, 0, 255, 255, // 顶行：红
            255, 0, 0, 255, 255, 0, 0, 255 // 底行：蓝
        };

        var bytes = IcoFile.Write(new[] { new IcoFrame(2, pixels) });
        var dib = bytes.AsSpan(22);
        var firstRow = dib.Slice(DibHeaderSize, 2 * 4);

        Assert.Equal(new byte[] { 255, 0, 0, 255, 255, 0, 0, 255 }, firstRow.ToArray());
    }

    /// <summary>ICO 里存非预乘 BGRA：半透明像素要除回原色，否则会被再乘一次而变暗。</summary>
    [Fact]
    public void Write_UnpremultipliesPixelColors()
    {
        // 预乘值 (32, 64, 128, alpha=128) 对应原色 (64, 128, 255)。
        var pixels = new byte[] { 32, 64, 128, 128 };

        var bytes = IcoFile.Write(new[] { new IcoFrame(1, pixels) });
        var pixel = bytes.AsSpan(22 + DibHeaderSize, 4);

        Assert.Equal(new byte[] { 64, 128, 255, 128 }, pixel.ToArray());
    }

    /// <summary>全透明像素在 AND 掩码里置 1（老程序不认 alpha 时靠它抠图）。</summary>
    [Fact]
    public void Write_SetsMaskBitForFullyTransparentPixels()
    {
        // 8×8，只有最后一行（DIB 里的第一行）的 x=1 全透明。
        var pixels = new byte[8 * 8 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 1] = 255;
            pixels[i + 2] = 255;
            pixels[i + 3] = 255;
        }

        var transparent = (7 * 8 + 1) * 4;
        pixels[transparent] = 0;
        pixels[transparent + 1] = 0;
        pixels[transparent + 2] = 0;
        pixels[transparent + 3] = 0;

        var bytes = IcoFile.Write(new[] { new IcoFrame(8, pixels) });
        var mask = bytes.AsSpan(22 + DibHeaderSize + 8 * 8 * 4, 4);

        Assert.Equal(new byte[] { 0b0100_0000, 0, 0, 0 }, mask.ToArray());
    }

    /// <summary>256 帧在目录里记作 0（ICO 的边长字段只有一个字节）。</summary>
    [Fact]
    public void Write_Records256FrameAsZeroInDirectory()
    {
        var bytes = IcoFile.Write(new[] { SolidFrame(256, 1, 2, 3, 255) });

        Assert.Equal(0, bytes[6]);
        Assert.Equal(0, bytes[7]);
        Assert.Equal(6 + 16 + FrameLength(256), bytes.Length);
    }

    /// <summary>多帧时每帧的偏移首尾相接，总长度等于目录加所有帧。</summary>
    [Fact]
    public void Write_ChainsFrameOffsets()
    {
        var sizes = new[] { 16, 24, 36, 48 };
        var frames = sizes.Select(size => SolidFrame(size, 10, 20, 30, 255)).ToArray();

        var bytes = IcoFile.Write(frames);

        var expectedOffset = 6 + 16 * frames.Length;
        for (var i = 0; i < frames.Length; i++)
        {
            var entry = bytes.AsSpan(6 + 16 * i, 16);
            Assert.Equal(sizes[i], entry[0]);
            Assert.Equal(FrameLength(sizes[i]), BinaryPrimitives.ReadInt32LittleEndian(entry[8..12]));
            Assert.Equal(expectedOffset, BinaryPrimitives.ReadInt32LittleEndian(entry[12..16]));
            expectedOffset += FrameLength(sizes[i]);
        }

        Assert.Equal(expectedOffset, bytes.Length);
    }

    /// <summary>默认帧表覆盖 Windows 任务栏各种 DPI 下真正请求的尺寸，且严格递增、不超上限。</summary>
    [Fact]
    public void DefaultFrameSizes_CoverTaskbarSizes()
    {
        var sizes = IcoFile.DefaultFrameSizes;

        // 144 DPI 下任务栏要 36（16×1.5 小图标 / 24×1.5 大图标都落在 36），其它缩放也要命中。
        Assert.Contains(16, sizes);
        Assert.Contains(24, sizes);
        Assert.Contains(36, sizes);
        Assert.Contains(48, sizes);
        Assert.All(sizes, size => Assert.InRange(size, 1, IcoFile.MaxFrameSize));
        Assert.Equal(sizes.OrderBy(size => size), sizes);
        Assert.Equal(sizes.Distinct(), sizes);
    }

    /// <summary>空帧列表直接拒绝，而不是拼出一份没人认得的文件。</summary>
    [Fact]
    public void Write_ThrowsWhenFramesAreEmpty()
    {
        Assert.Throws<ArgumentException>(() => IcoFile.Write(Array.Empty<IcoFrame>()));
    }

    /// <summary>尺寸重复会让目录出现两个同样的项，Windows 取哪个不确定。</summary>
    [Fact]
    public void Write_ThrowsWhenFrameSizesRepeat()
    {
        var frames = new[] { SolidFrame(32, 0, 0, 0, 255), SolidFrame(32, 0, 0, 0, 255) };

        Assert.Throws<ArgumentException>(() => IcoFile.Write(frames));
    }

    /// <summary>像素长度与声明尺寸不符时拒绝，避免写出半帧。</summary>
    [Fact]
    public void Write_ThrowsWhenPixelBufferLengthIsWrong()
    {
        var frames = new[] { new IcoFrame(16, new byte[16 * 16 * 4 - 4]) };

        Assert.Throws<ArgumentException>(() => IcoFile.Write(frames));
    }

    /// <summary>边长超出 ICO 目录项能表达的范围（>256）时拒绝。</summary>
    [Fact]
    public void Write_ThrowsWhenFrameIsTooLarge()
    {
        var frames = new[] { new IcoFrame(IcoFile.MaxFrameSize + 1, new byte[257 * 257 * 4]) };

        Assert.Throws<ArgumentException>(() => IcoFile.Write(frames));
    }
}
