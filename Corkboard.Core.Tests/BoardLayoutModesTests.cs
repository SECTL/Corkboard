using Corkboard.Core.Board;

namespace Corkboard.Core.Tests;

/// <summary>
///     排布方式折算：配置里存的值不一定是界面上还提供的两种。
/// </summary>
public class BoardLayoutModesTests
{
    [Theory]
    [InlineData(BoardLayoutMode.Columns, BoardLayoutMode.Columns)]
    [InlineData(BoardLayoutMode.Block, BoardLayoutMode.Block)]
    // 「通铺」已取消：老配置里读出来的 2 按区块显示。
    [InlineData(BoardLayoutMode.Flow, BoardLayoutMode.Block)]
    // 手改脏了的越界值：退回默认的排布（区块）。
    [InlineData((BoardLayoutMode)9, BoardLayoutMode.Block)]
    [InlineData((BoardLayoutMode)(-1), BoardLayoutMode.Block)]
    public void Normalize_MapsEveryValueToASupportedLayout(BoardLayoutMode stored, BoardLayoutMode expected)
    {
        Assert.Equal(expected, BoardLayoutModes.Normalize(stored));
    }

    /// <summary>默认排布是区块：新装 / 手改脏配置都落到这里。</summary>
    [Fact]
    public void Default_IsBlockLayout()
    {
        Assert.Equal(BoardLayoutMode.Block, BoardLayoutModes.Default);
    }

    /// <summary>
    ///     多列排布沿用的仍是老配置里「单列」那个数值 0——枚举按数字落盘，改数字等于改配置格式。
    /// </summary>
    [Fact]
    public void Columns_KeepsTheLegacySingleColumnValue()
    {
        Assert.Equal(0, (int)BoardLayoutMode.Columns);
    }

    /// <summary>栏宽上下限得是个能排版的区间：下限小于上限，且都在窗口最小宽度之内。</summary>
    [Fact]
    public void ColumnStyle_LimitsFormAUsableRange()
    {
        Assert.True(BoardColumnStyle.MinColumnWidth > 0);
        Assert.True(BoardColumnStyle.MaxColumnWidth > BoardColumnStyle.MinColumnWidth);
    }
}
