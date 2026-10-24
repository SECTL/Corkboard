using Corkboard.Core.Imaging;

namespace Corkboard.Core.Tests;

/// <summary>
///     <see cref="IconRaster" /> 的单测：任务栏图标母图按面积平均缩小，
///     保证缩 30～50 倍时不漏点、透明边不带脏色。
/// </summary>
public class IconRasterTests
{
    /// <summary>目标像素盖住的那一整块源像素要全部参与平均，而不是抽一个点。</summary>
    [Fact]
    public void ScaleSquare_AveragesWholeSourceBlock()
    {
        // 2×2 源图：左上、右上不透明白，下面两个全透明（预乘空间里 RGB 为 0）。
        var source = new byte[]
        {
            255, 255, 255, 255, 255, 255, 255, 255,
            0, 0, 0, 0, 0, 0, 0, 0
        };

        var target = IconRaster.ScaleSquare(source, 2, 2, 1);

        Assert.Equal(4, target.Length);
        // 两个不透明白 + 两个全透明：预乘后每个通道都是 255+255+0+0 的平均。
        Assert.Equal(new byte[] { 128, 128, 128, 128 }, target);
    }

    /// <summary>目标尺寸与源图一样时原样返回，不做任何插值。</summary>
    [Fact]
    public void ScaleSquare_ReturnsSamePixelsWhenSizeMatches()
    {
        var source = new byte[]
        {
            10, 20, 30, 40, 50, 60, 70, 80,
            90, 100, 110, 120, 130, 140, 150, 160
        };

        var target = IconRaster.ScaleSquare(source, 2, 2, 2);

        Assert.Equal(source, target);
    }

    /// <summary>非正方形母图也能缩成正方形的一帧（宽高各自独立分块）。</summary>
    [Fact]
    public void ScaleSquare_HandlesNonSquareSource()
    {
        // 4×2，全部不透明同色：缩成 1×1 就是那个颜色。
        var source = new byte[4 * 2 * 4];
        for (var i = 0; i < source.Length; i += 4)
        {
            source[i] = 12;
            source[i + 1] = 34;
            source[i + 2] = 56;
            source[i + 3] = 255;
        }

        var target = IconRaster.ScaleSquare(source, 4, 2, 1);

        Assert.Equal(new byte[] { 12, 34, 56, 255 }, target);
    }

    /// <summary>缓冲区不够长时直接拒绝，不要越界读到垃圾数据。</summary>
    [Fact]
    public void ScaleSquare_ThrowsWhenSourceBufferIsTooSmall()
    {
        var source = new byte[3 * 3 * 4 - 1];

        Assert.Throws<ArgumentException>(() => IconRaster.ScaleSquare(source, 3, 3, 2));
    }

    /// <summary>目标尺寸必须为正，否则算不出任何一帧。</summary>
    [Fact]
    public void ScaleSquare_ThrowsWhenTargetSizeIsNotPositive()
    {
        var source = new byte[4];

        Assert.Throws<ArgumentOutOfRangeException>(() => IconRaster.ScaleSquare(source, 1, 1, 0));
    }
}
