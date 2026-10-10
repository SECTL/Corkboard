using Corkboard.Core.Enums.Configs;

namespace Corkboard.Core.Tests;

/// <summary>
///     排布方式折算：配置里存的值不一定是界面上还提供的两种。
/// </summary>
public class BoardLayoutModesTests
{
    [Theory]
    [InlineData(BoardLayoutMode.SingleColumn, BoardLayoutMode.SingleColumn)]
    [InlineData(BoardLayoutMode.Block, BoardLayoutMode.Block)]
    // 「通铺」已取消：老配置里读出来的 2 按区块显示。
    [InlineData(BoardLayoutMode.Flow, BoardLayoutMode.Block)]
    // 手改脏了的越界值：退回默认的第一项（单列）。
    [InlineData((BoardLayoutMode)9, BoardLayoutMode.SingleColumn)]
    [InlineData((BoardLayoutMode)(-1), BoardLayoutMode.SingleColumn)]
    public void Normalize_MapsEveryValueToASupportedLayout(BoardLayoutMode stored, BoardLayoutMode expected)
    {
        Assert.Equal(expected, BoardLayoutModes.Normalize(stored));
    }
}
