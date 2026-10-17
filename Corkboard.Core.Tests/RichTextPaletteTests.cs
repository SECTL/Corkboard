using Avalonia.Media;
using Xunit;

using Corkboard.Core.RichText;

namespace Corkboard.Core.Tests;

/// <summary>
///     <see cref="RichTextPalette" /> 的规则测试。
///     <para>
///         设置页能加几个、浮窗上画几个，都取决于这里，所以边界（到顶、空、重复、透明）要钉死。
///     </para>
/// </summary>
public class RichTextPaletteTests
{
    [Fact]
    public void DefaultColors_FillThePalette()
    {
        Assert.Equal(RichTextPalette.MaxColors, RichTextPalette.DefaultColors.Count);
        Assert.All(RichTextPalette.DefaultColors, color => Assert.Equal(255, color.A));
    }

    [Fact]
    public void Normalize_Empty_FallsBackToDefaults()
    {
        Assert.Equal(RichTextPalette.DefaultColors, RichTextPalette.Normalize([]));
        Assert.Equal(RichTextPalette.DefaultColors, RichTextPalette.Normalize(null));
    }

    [Fact]
    public void Normalize_OnlyTransparentColors_FallsBackToDefaults()
    {
        Assert.Equal(RichTextPalette.DefaultColors, RichTextPalette.Normalize([Colors.Transparent]));
    }

    [Fact]
    public void Normalize_DropsTransparentColors()
    {
        var colors = RichTextPalette.Normalize([Colors.Red, Colors.Transparent, Colors.Blue]);

        Assert.Equal([Colors.Red, Colors.Blue], colors);
    }

    [Fact]
    public void Normalize_KeepsSemiTransparentColors()
    {
        // 半透明是用户自己的选择，只有全透明才等于「看不见的色块」。
        var half = Color.FromArgb(128, 255, 0, 0);

        Assert.Equal([half], RichTextPalette.Normalize([half]));
    }

    [Fact]
    public void Normalize_RemovesDuplicatesKeepingTheFirst()
    {
        var colors = RichTextPalette.Normalize([Colors.Red, Colors.Blue, Colors.Red]);

        Assert.Equal([Colors.Red, Colors.Blue], colors);
    }

    [Fact]
    public void Normalize_TruncatesToMaxColors()
    {
        var colors = RichTextPalette.Normalize(
            [Colors.Red, Colors.Green, Colors.Blue, Colors.Yellow, Colors.Orange, Colors.Purple, Colors.Pink]);

        Assert.Equal(RichTextPalette.MaxColors, colors.Count);
        Assert.DoesNotContain(Colors.Purple, colors);
        Assert.DoesNotContain(Colors.Pink, colors);
    }

    [Fact]
    public void Normalize_KeepsOrder()
    {
        Assert.Equal([Colors.Blue, Colors.Red, Colors.Green], RichTextPalette.Normalize([Colors.Blue, Colors.Red, Colors.Green]));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    public void CanAdd_StopsAtMaxColors(int count, bool expected)
    {
        Assert.Equal(expected, RichTextPalette.CanAdd(count));
    }
}
