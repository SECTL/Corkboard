using Corkboard.Core.Exam;
using Corkboard.Core.Models.SubConfigs.Exam;

namespace Corkboard.Core.Tests;

/// <summary>
///     考试看板「每一处文字都能设字号」这条线：夹取区间、默认值，以及落盘回来的值必须被夹住。
///     <para>
///         设置页的输入框与看板的渲染都读 <see cref="ExamBoardTextStyle" />，所以这里钉两件事：
///         一般文字与时钟各有自己的天花板（时钟能到 480，别的到 120 就到顶），以及坏配置读回来不会
///         画出个「字大到顶出屏幕」的看板。
///     </para>
/// </summary>
public class ExamBoardTextStyleTests
{
    /// <summary>一般字号的夹取：区间内原样，越界夹到边上，NaN 退回默认值。</summary>
    [Fact]
    public void Clamp_KeepsGeneralFontSizesInsideTheRange()
    {
        Assert.Equal(26, ExamBoardTextStyle.Clamp(26, ExamBoardTextStyle.DefaultTableFontSize), 9);
        Assert.Equal(ExamBoardTextStyle.MinFontSize, ExamBoardTextStyle.Clamp(1, 26), 9);
        Assert.Equal(ExamBoardTextStyle.MaxFontSize, ExamBoardTextStyle.Clamp(9999, 26), 9);
        Assert.Equal(ExamBoardTextStyle.DefaultTableFontSize, ExamBoardTextStyle.Clamp(double.NaN, 26), 9);
    }

    /// <summary>时钟那两档的上限比一般文字大得多：220 是它的日常值，一般字号的天花板管不到它。</summary>
    [Fact]
    public void ClampClock_AllowsMuchLargerSizesThanGeneralText()
    {
        Assert.True(ExamBoardTextStyle.MaxClockFontSize > ExamBoardTextStyle.MaxFontSize);
        Assert.Equal(220, ExamBoardTextStyle.ClampClock(220, ExamBoardTextStyle.DefaultClockAloneFontSize), 9);
        Assert.Equal(ExamBoardTextStyle.MaxClockFontSize, ExamBoardTextStyle.ClampClock(9999, 220), 9);
        Assert.Equal(ExamBoardTextStyle.DefaultClockAloneFontSize,
            ExamBoardTextStyle.ClampClock(double.PositiveInfinity, ExamBoardTextStyle.DefaultClockAloneFontSize), 9);
    }

    /// <summary>
    ///     手改坏的 settings.json 读回来要被收拾干净：字号全夹进区间、阈值夹进各自区间，
    ///     而不是把这些数原样画到看板上。
    /// </summary>
    [Fact]
    public void ConfigNormalize_ClampsEveryFontSize()
    {
        var config = new ExamBoardSettingsConfig
        {
            TitleFontSize = 1,
            SubtitleFontSize = 9999,
            TableFontSize = double.NaN,
            TableSubjectFontSize = -3,
            InfoSubjectFontSize = 900,
            ClockFontSize = 9999,
            ClockAloneFontSize = 1,
            EmptyFontSize = 0,
            ExitIconSize = 9999,
            UpcomingThresholdMinutes = -5
        };

        config.Normalize();

        Assert.Equal(ExamBoardTextStyle.MinFontSize, config.TitleFontSize, 9);
        Assert.Equal(ExamBoardTextStyle.MaxFontSize, config.SubtitleFontSize, 9);
        Assert.Equal(ExamBoardTextStyle.DefaultTableFontSize, config.TableFontSize, 9);
        // Normalize 也会把剩余提醒门槛一起夹一遍（0 是不提醒、合法），所以上面只喂了负的即将开始阈值。
        Assert.Equal(ExamStatusPolicy.DefaultRemainingWarningMinutes, config.RemainingWarningMinutes);
        Assert.Equal(ExamBoardTextStyle.MinFontSize, config.TableSubjectFontSize, 9);
        Assert.Equal(ExamBoardTextStyle.MaxFontSize, config.InfoSubjectFontSize, 9);
        Assert.Equal(ExamBoardTextStyle.MaxClockFontSize, config.ClockFontSize, 9);
        Assert.Equal(ExamBoardTextStyle.MinFontSize, config.ClockAloneFontSize, 9);
        Assert.Equal(ExamBoardTextStyle.MinFontSize, config.EmptyFontSize, 9);
        Assert.Equal(ExamBoardTextStyle.MaxFontSize, config.ExitIconSize, 9);
        Assert.Equal(ExamStatusPolicy.MinUpcomingMinutes, config.UpcomingThresholdMinutes);
    }

    /// <summary>默认值就是历史上窗口 XAML 里写死的那几个数，老用户升级上来看不到变化。</summary>
    [Fact]
    public void Defaults_MatchTheSizesTheBoardUsedToHardCode()
    {
        Assert.Equal(34, ExamBoardTextStyle.DefaultTitleFontSize, 9);
        Assert.Equal(22, ExamBoardTextStyle.DefaultSubtitleFontSize, 9);
        Assert.Equal(108, ExamBoardTextStyle.DefaultClockFontSize, 9);
        Assert.Equal(220, ExamBoardTextStyle.DefaultClockAloneFontSize, 9);
        Assert.Equal(15, ExamBoardTextStyle.DefaultTableHeaderFontSize, 9);
        Assert.Equal(26, ExamBoardTextStyle.DefaultTableFontSize, 9);
        Assert.Equal(30, ExamBoardTextStyle.DefaultTableSubjectFontSize, 9);
        Assert.Equal(24, ExamBoardTextStyle.DefaultExitIconSize, 9);
    }
}
