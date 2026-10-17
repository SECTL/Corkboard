
using Corkboard.Core.Board;

namespace Corkboard.Core.Tests;

/// <summary>
///     作业板两处小字的字号夹取：科目标题与作业条小字（类型、字段值、截止日期）。
///     设置页存的草稿、老版本 settings.json 里没有的字段、被手改脏的值，最后都要从这里过一遍，
///     渲染层才不会拿到越界或 NaN 的字号。
/// </summary>
public class BoardTextStyleTests
{
    [Fact]
    public void ClampSubjectFontSize_KeepsValuesInsideTheRange()
    {
        // 区间内的值原样返回（默认值 16 就在区间里）。
        Assert.Equal(16, BoardTextStyle.ClampSubjectFontSize(16), 9);
        Assert.Equal(BoardTextStyle.MinSubjectFontSize, BoardTextStyle.ClampSubjectFontSize(BoardTextStyle.MinSubjectFontSize), 9);
        Assert.Equal(BoardTextStyle.MaxSubjectFontSize, BoardTextStyle.ClampSubjectFontSize(BoardTextStyle.MaxSubjectFontSize), 9);

        // 越界值夹到边界：手输 4 或 200 都不该漏到渲染层。
        Assert.Equal(BoardTextStyle.MinSubjectFontSize, BoardTextStyle.ClampSubjectFontSize(4), 9);
        Assert.Equal(BoardTextStyle.MaxSubjectFontSize, BoardTextStyle.ClampSubjectFontSize(200), 9);
    }

    [Fact]
    public void ClampMetaFontSize_KeepsValuesInsideTheRange()
    {
        Assert.Equal(14, BoardTextStyle.ClampMetaFontSize(14), 9);
        Assert.Equal(BoardTextStyle.MinMetaFontSize, BoardTextStyle.ClampMetaFontSize(2), 9);
        Assert.Equal(BoardTextStyle.MaxMetaFontSize, BoardTextStyle.ClampMetaFontSize(80), 9);
    }

    [Fact]
    public void ClampFontSizes_FallBackToTheDefaultsForBadValues()
    {
        // 坏值（NaN / 无穷）退回默认字号，而不是夹成上下限。
        Assert.Equal(BoardTextStyle.DefaultSubjectFontSize, BoardTextStyle.ClampSubjectFontSize(double.NaN), 9);
        Assert.Equal(BoardTextStyle.DefaultSubjectFontSize, BoardTextStyle.ClampSubjectFontSize(double.PositiveInfinity), 9);
        Assert.Equal(BoardTextStyle.DefaultMetaFontSize, BoardTextStyle.ClampMetaFontSize(double.NaN), 9);
        Assert.Equal(BoardTextStyle.DefaultMetaFontSize, BoardTextStyle.ClampMetaFontSize(double.NegativeInfinity), 9);
    }

    [Fact]
    public void Defaults_MakeBothLinesLargerThanBefore()
    {
        // 默认值比改动前大：科目标题原来是继承下来的 14，作业条小字原来是写死的 12。
        Assert.Equal(16, BoardTextStyle.DefaultSubjectFontSize, 9);
        Assert.Equal(14, BoardTextStyle.DefaultMetaFontSize, 9);
        Assert.True(BoardTextStyle.DefaultSubjectFontSize > 14);
        Assert.True(BoardTextStyle.DefaultMetaFontSize > 12);
    }
}
