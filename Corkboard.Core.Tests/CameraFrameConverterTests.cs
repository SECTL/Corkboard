using System;
using Corkboard.Core.DutyRoster.Import;

namespace Corkboard.Core.Tests;

/// <summary>
///     `CameraFrameConverter` 的单测：只验像素搬运本身，不涉及相机、Avalonia 位图。
/// </summary>
public sealed class CameraFrameConverterTests
{
    [Fact]
    public void ConvertsTwoByTwoBgr24ToBgra32()
    {
        // 红 / 绿 / 蓝 / 白，BGR 顺序；换算后每个像素尾部补 255。
        byte[] bgr =
        [
            0, 0, 255, // 红
            0, 255, 0, // 绿
            255, 0, 0, // 蓝
            255, 255, 255 // 白
        ];

        var bgra = CameraFrameConverter.ToBgra32(bgr, 2, 2);

        Assert.Equal(16, bgra.Length);
        Assert.Equal(
        [
            0, 0, 255, 255,
            0, 255, 0, 255,
            255, 0, 0, 255,
            255, 255, 255, 255
        ], bgra);
    }

    [Fact]
    public void ConvertsOddWidthWithoutRowDrift()
    {
        // 宽 3、高 2：每行 9 字节，最容易写错行内步长（写错会让第二行整体错位）。
        byte[] bgr =
        [
            1, 2, 3, 4, 5, 6, 7, 8, 9,
            10, 11, 12, 13, 14, 15, 16, 17, 18
        ];

        var bgra = CameraFrameConverter.ToBgra32(bgr, 3, 2);

        Assert.Equal(24, bgra.Length);
        Assert.Equal(
        [
            1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255,
            10, 11, 12, 255, 13, 14, 15, 255, 16, 17, 18, 255
        ], bgra);
    }

    [Fact]
    public void IgnoresTrailingBytesBeyondOneFrame()
    {
        byte[] bgr = [10, 20, 30, 40, 50, 60, 70, 80, 90];

        var bgra = CameraFrameConverter.ToBgra32(bgr, 1, 1);

        Assert.Equal([10, 20, 30, 255], bgra);
    }

    [Fact]
    public void RejectsBadArguments()
    {
        Assert.Throws<ArgumentNullException>(() => CameraFrameConverter.ToBgra32(null!, 1, 1));
        Assert.Throws<ArgumentException>(() => CameraFrameConverter.ToBgra32(new byte[3], 0, 1));
        Assert.Throws<ArgumentException>(() => CameraFrameConverter.ToBgra32(new byte[3], 1, 0));
        Assert.Throws<ArgumentException>(() => CameraFrameConverter.ToBgra32(new byte[5], 2, 1));
    }
}