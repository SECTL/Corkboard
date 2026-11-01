using Corkboard.Core.Exam;

namespace Corkboard.Core.Tests;

/// <summary>
///     考试看板「最小化之后那颗浮窗按钮」的形状参数：缩放夹取、各部件随缩放联动、按钮文字的归一。
///     <para>
///         设置页的输入框上下限（60–200%）与浮窗真正画出来的字号 / 图标 / 圆角 / 内边距都读
///         <see cref="ExamBoardFloatingStyle" />，所以这里钉的就是「设置里说多大，浮窗就是多大」，
///         以及写坏的配置（负数、NaN、超长文字）读回来也不会画出一颗占满半个屏幕的按钮。
///     </para>
/// </summary>
public class ExamBoardFloatingStyleTests
{
    /// <summary>缩放区间：区间内原样，越界夹到边上，NaN 退回默认的 100%。</summary>
    [Fact]
    public void ClampScalePercent_KeepsPercentInsideTheRange()
    {
        Assert.Equal(120, ExamBoardFloatingStyle.ClampScalePercent(120), 9);
        Assert.Equal(ExamBoardFloatingStyle.MinScalePercent, ExamBoardFloatingStyle.ClampScalePercent(1), 9);
        Assert.Equal(ExamBoardFloatingStyle.MaxScalePercent, ExamBoardFloatingStyle.ClampScalePercent(9999), 9);
        Assert.Equal(ExamBoardFloatingStyle.DefaultScalePercent,
            ExamBoardFloatingStyle.ClampScalePercent(double.NaN), 9);
        Assert.Equal(ExamBoardFloatingStyle.DefaultScalePercent,
            ExamBoardFloatingStyle.ClampScalePercent(double.PositiveInfinity), 9);
    }

    /// <summary>100% 就是基准值本身：设置页把百分比默认成 100 时，浮窗长得跟设计稿一样。</summary>
    [Fact]
    public void Resolve_AtDefaultPercent_ReturnsTheBaseMetrics()
    {
        const double percent = ExamBoardFloatingStyle.DefaultScalePercent;

        Assert.Equal(1d, ExamBoardFloatingStyle.ResolveScale(percent), 9);
        Assert.Equal(ExamBoardFloatingStyle.BaseFontSize, ExamBoardFloatingStyle.ResolveFontSize(percent), 9);
        Assert.Equal(ExamBoardFloatingStyle.BaseIconSize, ExamBoardFloatingStyle.ResolveIconSize(percent), 9);
        Assert.Equal(ExamBoardFloatingStyle.BaseCornerRadius, ExamBoardFloatingStyle.ResolveCornerRadius(percent), 9);
        Assert.Equal(ExamBoardFloatingStyle.BasePaddingHorizontal,
            ExamBoardFloatingStyle.ResolvePaddingHorizontal(percent), 9);
        Assert.Equal(ExamBoardFloatingStyle.BasePaddingVertical,
            ExamBoardFloatingStyle.ResolvePaddingVertical(percent), 9);
    }

    /// <summary>放大每一样都变大、缩小每一样都变小——包括圆角与内边距，不然大按钮会显得「字大框没大」。</summary>
    [Fact]
    public void Resolve_ScalesEveryMetricUpAndDown()
    {
        const double bigger = ExamBoardFloatingStyle.MaxScalePercent;
        const double smaller = ExamBoardFloatingStyle.MinScalePercent;

        Assert.True(ExamBoardFloatingStyle.ResolveFontSize(bigger) > ExamBoardFloatingStyle.BaseFontSize);
        Assert.True(ExamBoardFloatingStyle.ResolveIconSize(bigger) > ExamBoardFloatingStyle.BaseIconSize);
        Assert.True(ExamBoardFloatingStyle.ResolveCornerRadius(bigger) > ExamBoardFloatingStyle.BaseCornerRadius);
        Assert.True(ExamBoardFloatingStyle.ResolvePaddingHorizontal(bigger) >
                    ExamBoardFloatingStyle.BasePaddingHorizontal);
        Assert.True(ExamBoardFloatingStyle.ResolvePaddingVertical(bigger) > ExamBoardFloatingStyle.BasePaddingVertical);

        Assert.True(ExamBoardFloatingStyle.ResolveFontSize(smaller) < ExamBoardFloatingStyle.BaseFontSize);
        Assert.True(ExamBoardFloatingStyle.ResolveIconSize(smaller) < ExamBoardFloatingStyle.BaseIconSize);
        Assert.True(ExamBoardFloatingStyle.ResolvePaddingHorizontal(smaller) <
                    ExamBoardFloatingStyle.BasePaddingHorizontal);
    }

    /// <summary>圆角永远是「圆角矩形」：缩到最大也远不到半高，不会变成一颗胶囊。</summary>
    [Fact]
    public void ResolveCornerRadius_StaysWellBelowHalfHeight()
    {
        var halfHeight = ExamBoardFloatingStyle.BaseFontSize / 2 +
                         ExamBoardFloatingStyle.BasePaddingVertical;

        Assert.True(ExamBoardFloatingStyle.ResolveCornerRadius(ExamBoardFloatingStyle.MaxScalePercent) <
                    halfHeight * ExamBoardFloatingStyle.ResolveScale(ExamBoardFloatingStyle.MaxScalePercent));
    }

    /// <summary>没填 / 只有空白 = 空串，界面那边据此回退到默认的那句话。</summary>
    [Fact]
    public void NormalizeText_TreatsBlankAsEmpty()
    {
        Assert.Equal(string.Empty, ExamBoardFloatingStyle.NormalizeText(null));
        Assert.Equal(string.Empty, ExamBoardFloatingStyle.NormalizeText(string.Empty));
        Assert.Equal(string.Empty, ExamBoardFloatingStyle.NormalizeText("   "));
        Assert.Equal(string.Empty, ExamBoardFloatingStyle.NormalizeText("\r\n\t "));
    }

    /// <summary>首尾空白去掉、换行压成空格、连续空格收成一个：浮窗只有一行字，留不住换行。</summary>
    [Fact]
    public void NormalizeText_FlattensWhitespace()
    {
        Assert.Equal("显示看板", ExamBoardFloatingStyle.NormalizeText("  显示看板  "));
        Assert.Equal("显示 看板", ExamBoardFloatingStyle.NormalizeText("显示\r\n看板"));
        Assert.Equal("显示 看板", ExamBoardFloatingStyle.NormalizeText("显示   看板"));
    }

    /// <summary>超长截到上限并去掉截断处的尾随空格，避免浮窗横着占掉半个屏幕。</summary>
    [Fact]
    public void NormalizeText_TruncatesLongText()
    {
        var tooLong = new string('看', ExamBoardFloatingStyle.MaxTextLength + 10);
        var normalized = ExamBoardFloatingStyle.NormalizeText(tooLong);

        Assert.Equal(ExamBoardFloatingStyle.MaxTextLength, normalized.Length);

        var withTrailingSpace = new string('看', ExamBoardFloatingStyle.MaxTextLength - 1) + "  尾巴";
        Assert.Equal(new string('看', ExamBoardFloatingStyle.MaxTextLength - 1),
            ExamBoardFloatingStyle.NormalizeText(withTrailingSpace));
    }

    /// <summary>刚好等于上限的文字原样保留（别把边界值也截掉一个字）。</summary>
    [Fact]
    public void NormalizeText_KeepsTextAtTheLimit()
    {
        var exact = new string('考', ExamBoardFloatingStyle.MaxTextLength);

        Assert.Equal(exact, ExamBoardFloatingStyle.NormalizeText(exact));
    }

    /// <summary>无字模式（只剩一颗图标）时图标要比常规大一档：没有文字撑场，小图标会显得按钮是空的。</summary>
    [Fact]
    public void ResolveIconOnlyIconSize_IsBiggerThanTheRegularIcon()
    {
        foreach (var percent in new[] { ExamBoardFloatingStyle.MinScalePercent, 100, ExamBoardFloatingStyle.MaxScalePercent })
            Assert.True(ExamBoardFloatingStyle.ResolveIconOnlyIconSize(percent) >
                        ExamBoardFloatingStyle.ResolveIconSize(percent));
    }

    /// <summary>无字模式也吃「大小」那一档设置：60% 更小、200% 更大，与常规图标同步放大缩小。</summary>
    [Fact]
    public void ResolveIconOnlyIconSize_FollowsTheScale()
    {
        Assert.True(ExamBoardFloatingStyle.ResolveIconOnlyIconSize(ExamBoardFloatingStyle.MaxScalePercent) >
                    ExamBoardFloatingStyle.ResolveIconOnlyIconSize(ExamBoardFloatingStyle.DefaultScalePercent));
        Assert.True(ExamBoardFloatingStyle.ResolveIconOnlyIconSize(ExamBoardFloatingStyle.MinScalePercent) <
                    ExamBoardFloatingStyle.ResolveIconOnlyIconSize(ExamBoardFloatingStyle.DefaultScalePercent));
    }

    /// <summary>图标尺寸取整：DIP 带小数会被渲染成模糊的一团，浮窗上那颗小图标尤其明显。</summary>
    [Fact]
    public void ResolveIconOnlyMetrics_AreRounded()
    {
        foreach (var percent in new[] { 60d, 73d, 111d, 199d })
        {
            Assert.Equal(Math.Round(ExamBoardFloatingStyle.ResolveIconOnlyIconSize(percent)),
                ExamBoardFloatingStyle.ResolveIconOnlyIconSize(percent), 9);
            Assert.Equal(Math.Round(ExamBoardFloatingStyle.ResolveIconOnlyPadding(percent)),
                ExamBoardFloatingStyle.ResolveIconOnlyPadding(percent), 9);
        }
    }

    /// <summary>无字模式的内边距就是常规纵向内边距（四边一样），不另开一套数字。</summary>
    [Fact]
    public void ResolveIconOnlyPadding_MatchesTheVerticalPadding()
    {
        foreach (var percent in new[] { 60d, 100d, 200d })
            Assert.Equal(ExamBoardFloatingStyle.ResolvePaddingVertical(percent),
                ExamBoardFloatingStyle.ResolveIconOnlyPadding(percent), 9);
    }

    /// <summary>
    ///     「按住 2 秒退出」那行提示比标题小一档——提示不该抢标题的戏，但也不能糊成一团。
    ///     最小档（60%）时会撞到 <c>MinHintFontSize</c> 下限，与标题同号，所以这里判「不大于」。
    /// </summary>
    [Fact]
    public void ResolveHintFontSize_IsNotBiggerThanTheCaption()
    {
        foreach (var percent in new[] { 60d, 100d, 150d, 200d })
            Assert.True(ExamBoardFloatingStyle.ResolveHintFontSize(percent) <=
                        ExamBoardFloatingStyle.ResolveFontSize(percent));
    }

    /// <summary>提示字号跟着「大小」那一档走：放大按钮时提示也放大，不能一直钉在 11。</summary>
    [Fact]
    public void ResolveHintFontSize_FollowsTheScale()
    {
        Assert.True(ExamBoardFloatingStyle.ResolveHintFontSize(ExamBoardFloatingStyle.MaxScalePercent) >
                    ExamBoardFloatingStyle.ResolveHintFontSize(ExamBoardFloatingStyle.DefaultScalePercent));
        Assert.True(ExamBoardFloatingStyle.ResolveHintFontSize(ExamBoardFloatingStyle.MinScalePercent) <
                    ExamBoardFloatingStyle.ResolveHintFontSize(ExamBoardFloatingStyle.DefaultScalePercent));
    }

    /// <summary>缩放调到最小档时提示字仍有下限：再小就等于没写。</summary>
    [Fact]
    public void ResolveHintFontSize_NeverDropsBelowTheFloor()
    {
        foreach (var percent in new[] { ExamBoardFloatingStyle.MinScalePercent, 0d, double.NaN })
            Assert.True(ExamBoardFloatingStyle.ResolveHintFontSize(percent) >=
                        ExamBoardFloatingStyle.MinHintFontSize);
    }
}
