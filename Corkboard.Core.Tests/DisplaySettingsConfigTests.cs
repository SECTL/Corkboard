using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Exam;
using Corkboard.Core.Models.SubConfigs.Display;
using Corkboard.Core.Display;

namespace Corkboard.Core.Tests;

/// <summary>
///     看板模式的配置：默认值、读盘之后的收拾（<see cref="DisplaySettingsConfig.Normalize" />），
///     以及派生出来的时刻 / 字号与收拾之后的值必须一致。
///     <para>
///     全屏窗口在设置页打开之前就会先按配置渲染，所以脏值必须在配置层就被夹住——
///     这里钉的就是「手改过 settings.json 也不会画出个 9999 号的字、或者一段越界的时刻」。
///     字号与考试看板共用 <see cref="ExamBoardTextStyle" /> 的区间，不要在别处另写一组数字。
///     </para>
/// </summary>
public class DisplaySettingsConfigTests
{
    /// <summary>
    ///     默认值：四样都显示（含「可以编辑」）、时钟在左上角、96 号的大时钟、
    ///     晚上七点到九点半、自动进入关、自动退出开。
    /// </summary>
    [Fact]
    public void Defaults_AreWhatANewInstallGets()
    {
        var config = new DisplaySettingsConfig();

        // 显示什么：四样全开，「默认需要可以编辑」是用户明确要的。
        Assert.True(config.ShowClock);
        Assert.True(config.ShowDate);
        Assert.True(config.ShowBoard);
        Assert.True(config.Editable);

        // 怎么排：字号那几项有默认值（时钟固定左上角，没有版式可配了）。
        Assert.Equal(DisplayTextStyle.DefaultClockFontSize, config.ClockFontSize, 9);
        Assert.Equal(DisplayTextStyle.DefaultClockDateFontSize, config.ClockDateFontSize, 9);
        Assert.Equal(DisplayTextStyle.DefaultTitleFontSize, config.TitleFontSize, 9);
        Assert.Equal(DisplayTextStyle.DefaultExitIconSize, config.ExitIconSize, 9);
        Assert.Equal(DisplayTextStyle.DefaultWeatherFontSize, config.WeatherFontSize, 9);
        Assert.Equal(DisplayTextStyle.DefaultQuoteFontSize, config.QuoteFontSize, 9);
        Assert.Equal(3, config.HideCursorAfterSeconds);

        // 什么时候自己进出：自动进入关（不该装了就弹全屏），自动退出开（进去的那次到点要自己收）。
        Assert.False(config.AutoShow);
        Assert.True(config.AutoHide);
        Assert.Equal(new TimeOnly(19, 0), config.EnterTime);
        Assert.Equal(new TimeOnly(21, 30), config.ExitTime);
        Assert.Equal(DisplaySchedulePolicy.DefaultEnterTime, config.EnterTime);
        Assert.Equal(DisplaySchedulePolicy.DefaultExitTime, config.ExitTime);
    }

    /// <summary>派生出来的时刻就是 <see cref="DisplaySchedulePolicy.ResolveTime" /> 拼出来的那一对。</summary>
    [Fact]
    public void EnterAndExitTime_ComeFromTheFourNumbers()
    {
        var config = new DisplaySettingsConfig
        {
            EnterHour = 18,
            EnterMinute = 45,
            ExitHour = 22,
            ExitMinute = 5
        };

        Assert.Equal(new TimeOnly(18, 45), config.EnterTime);
        Assert.Equal(new TimeOnly(22, 5), config.ExitTime);
    }

    /// <summary>
    ///     时刻越界被夹回合法区间：小时进 0–23、分钟进 0–59，
    ///     派生出来的 <see cref="DisplaySettingsConfig.EnterTime" /> 读到的也必须是夹过之后的值。
    /// </summary>
    [Fact]
    public void Normalize_ClampsHoursAndMinutes()
    {
        var config = new DisplaySettingsConfig
        {
            EnterHour = 99,
            EnterMinute = -1,
            ExitHour = -5,
            ExitMinute = 99
        };

        config.Normalize();

        Assert.Equal(23, config.EnterHour);
        Assert.Equal(0, config.EnterMinute);
        Assert.Equal(0, config.ExitHour);
        Assert.Equal(59, config.ExitMinute);

        // 派生值跟着走：窗口与设置页看到的是同一对时刻。
        Assert.Equal(new TimeOnly(23, 0), config.EnterTime);
        Assert.Equal(new TimeOnly(0, 59), config.ExitTime);
    }

    /// <summary>合法值原样保留：收拾一遍不该动用户自己设的时刻。</summary>
    [Fact]
    public void Normalize_KeepsValidTimes()
    {
        var config = new DisplaySettingsConfig
        {
            EnterHour = 8,
            EnterMinute = 0,
            ExitHour = 23,
            ExitMinute = 59
        };

        config.Normalize();

        Assert.Equal(new TimeOnly(8, 0), config.EnterTime);
        Assert.Equal(new TimeOnly(23, 59), config.ExitTime);
    }

    /// <summary>光标隐藏的等待秒数夹进 0–60：0 是合法值（从不隐藏），越界的一律折回边上。</summary>
    [Theory]
    [InlineData(0, 0)] // 0 = 从不隐藏，合法
    [InlineData(3, 3)]
    [InlineData(60, 60)]
    [InlineData(-5, 0)]
    [InlineData(999, 60)]
    [InlineData(int.MaxValue, 60)]
    [InlineData(int.MinValue, 0)]
    public void Normalize_ClampsCursorHideSeconds(int stored, int expected)
    {
        var config = new DisplaySettingsConfig { HideCursorAfterSeconds = stored };

        config.Normalize();

        Assert.Equal(expected, config.HideCursorAfterSeconds);
    }

    /// <summary>
    ///     老配置里的 <c>layout</c> 已经下架：第 3 档（「只有作业」）本意是「别画时钟」，
    ///     读盘时折算成关掉时钟；其余几档本来就画时钟，折成左上角即可，什么都不用改。
    ///     <para>
    ///     时钟的位置**不再可配**（用户要求「时间的显示位置不能改，只能显示到左上角」），
    ///     所以这里钉的是「升级上来的人不会突然多出一块大钟、也不会丢时钟」。
    ///     </para>
    /// </summary>
    [Theory]
    [InlineData(0, true)] // 时钟在左上角 → 照旧画
    [InlineData(1, true)] // 右上角 → 折成左上角，照旧画
    [InlineData(2, true)] // 顶部居中 → 折成左上角，照旧画
    [InlineData(3, false)] // 只有作业 → 关掉时钟
    [InlineData(99, true)] // 脏值 → 当没配，照旧画
    public void LegacyLayoutOnLoad_FoldsBoardOnlyIntoHiddenClock(int stored, bool expected)
    {
        var config = new DisplaySettingsConfig { LegacyLayoutOnLoad = stored };

        Assert.Equal(expected, config.ShowClock);
    }

    /// <summary>没有 <c>layout</c> 的老配置（或新装的）不该被这一折算动到时钟开关。</summary>
    [Fact]
    public void LegacyLayoutOnLoad_DoesNothingWhenMissing()
    {
        var config = new DisplaySettingsConfig { ShowClock = false };

        config.LegacyLayoutOnLoad = null;

        Assert.False(config.ShowClock);
    }

    /// <summary>
    ///     时钟字号走时钟那一档的上下限（8–480），坏值退回默认的 96——
    ///     它是整块屏幕上最大的字，跟一般文字同一个天花板就没有意义了。
    /// </summary>
    [Theory]
    [InlineData(96, 96)] // 默认值原样
    [InlineData(300, 300)] // 区间内
    [InlineData(480, 480)] // 上限
    [InlineData(8, 8)] // 下限
    [InlineData(10000, 480)] // 越界：夹到天花板
    [InlineData(1, 8)] // 太小：夹到地板
    [InlineData(-3, 8)]
    [InlineData(0, 8)]
    public void Normalize_ClampsClockFontSize(double stored, double expected)
    {
        var config = new DisplaySettingsConfig { ClockFontSize = stored };

        config.Normalize();

        Assert.Equal(expected, config.ClockFontSize, 9);
        Assert.Equal(expected, config.ResolvedClockFontSize, 9);
    }

    /// <summary>坏值（NaN / 无穷）退回默认字号，而不是夹成上下限。</summary>
    [Fact]
    public void Normalize_FallsBackToDefaultForNonFiniteClockFontSize()
    {
        var config = new DisplaySettingsConfig
        {
            ClockFontSize = double.NaN,
            ClockDateFontSize = double.NaN,
            TitleFontSize = double.NaN,
            ExitIconSize = double.NaN
        };

        config.Normalize();

        Assert.Equal(DisplayTextStyle.DefaultClockFontSize, config.ClockFontSize, 9);
        Assert.Equal(DisplayTextStyle.DefaultClockDateFontSize, config.ClockDateFontSize, 9);
        Assert.Equal(DisplayTextStyle.DefaultTitleFontSize, config.TitleFontSize, 9);
        Assert.Equal(DisplayTextStyle.DefaultExitIconSize, config.ExitIconSize, 9);

        // 派生值与夹取之后的值一致。
        Assert.Equal(config.ClockFontSize, config.ResolvedClockFontSize, 9);
        Assert.Equal(config.ClockDateFontSize, config.ResolvedClockDateFontSize, 9);
        Assert.Equal(config.TitleFontSize, config.ResolvedTitleFontSize, 9);
        Assert.Equal(config.ExitIconSize, config.ResolvedExitIconSize, 9);
    }

    /// <summary>无穷值同样退回默认字号（它也不是个能画的数）。</summary>
    [Fact]
    public void Normalize_FallsBackToDefaultForInfiniteFontSizes()
    {
        var config = new DisplaySettingsConfig
        {
            ClockFontSize = double.PositiveInfinity,
            ClockDateFontSize = double.NegativeInfinity,
            TitleFontSize = double.PositiveInfinity,
            ExitIconSize = double.NegativeInfinity
        };

        config.Normalize();

        Assert.Equal(DisplayTextStyle.DefaultClockFontSize, config.ClockFontSize, 9);
        Assert.Equal(DisplayTextStyle.DefaultClockDateFontSize, config.ClockDateFontSize, 9);
        Assert.Equal(DisplayTextStyle.DefaultTitleFontSize, config.TitleFontSize, 9);
        Assert.Equal(DisplayTextStyle.DefaultExitIconSize, config.ExitIconSize, 9);
    }

    /// <summary>
    ///     其余三处字号走一般文字那一档（8–120），不是时钟那一档——
    ///     日期行、作业区标题与退出图标都不该被允许涨到 480。
    /// </summary>
    [Fact]
    public void Normalize_ClampsTheOtherFontSizesToTheGeneralRange()
    {
        var config = new DisplaySettingsConfig
        {
            ClockDateFontSize = 9999,
            TitleFontSize = 1,
            ExitIconSize = 500
        };

        config.Normalize();

        Assert.Equal(ExamBoardTextStyle.MaxFontSize, config.ClockDateFontSize, 9);
        Assert.Equal(ExamBoardTextStyle.MinFontSize, config.TitleFontSize, 9);
        Assert.Equal(ExamBoardTextStyle.MaxFontSize, config.ExitIconSize, 9);
    }

    /// <summary>
    ///     天气与一言的字号各走自己那一档区间（18–96 / 14–72）：既不是时钟那一档（8–480），
    ///     也不是一般文字那一档（8–120）——天气只是屏幕角上的一块、一言只有一行，
    ///     都不该被允许涨到把别的字挤没。
    ///     <para>坏值（NaN / 无穷）退回默认值，而不是夹到边上。</para>
    /// </summary>
    [Theory]
    [InlineData(22, 22)]
    [InlineData(96, 96)]
    [InlineData(1000, 96)]
    [InlineData(1, 18)]
    [InlineData(-4, 18)]
    public void Normalize_ClampsWeatherFontSize(double stored, double expected)
    {
        var config = new DisplaySettingsConfig { WeatherFontSize = stored };

        config.Normalize();

        Assert.Equal(expected, config.WeatherFontSize, 9);
        Assert.Equal(expected, config.ResolvedWeatherFontSize, 9);
    }

    [Theory]
    [InlineData(18, 18)]
    [InlineData(72, 72)]
    [InlineData(1000, 72)]
    [InlineData(1, 14)]
    [InlineData(-4, 14)]
    public void Normalize_ClampsQuoteFontSize(double stored, double expected)
    {
        var config = new DisplaySettingsConfig { QuoteFontSize = stored };

        config.Normalize();

        Assert.Equal(expected, config.QuoteFontSize, 9);
        Assert.Equal(expected, config.ResolvedQuoteFontSize, 9);
    }

    [Fact]
    public void Normalize_FallsBackToDefaultForNonFiniteCaptionFontSizes()
    {
        var config = new DisplaySettingsConfig
        {
            WeatherFontSize = double.NaN,
            QuoteFontSize = double.PositiveInfinity
        };

        config.Normalize();

        Assert.Equal(DisplayTextStyle.DefaultWeatherFontSize, config.WeatherFontSize, 9);
        Assert.Equal(DisplayTextStyle.DefaultQuoteFontSize, config.QuoteFontSize, 9);
    }

    /// <summary>
    ///     <see cref="DisplaySettingsConfig.Resolved*" /> 是窗口直接绑的那几个值：
    ///     就算没调 <see cref="DisplaySettingsConfig.Normalize" />（比如设置页刚改完还没落盘），
    ///     它们也得给出夹过的数，不能把 10000 原样交给渲染层。
    /// </summary>
    [Fact]
    public void ResolvedFontSizes_ClampWithoutNormalize()
    {
        var config = new DisplaySettingsConfig
        {
            ClockFontSize = 10000,
            ClockDateFontSize = 1,
            TitleFontSize = 9999,
            ExitIconSize = -1
        };

        Assert.Equal(ExamBoardTextStyle.MaxClockFontSize, config.ResolvedClockFontSize, 9);
        Assert.Equal(ExamBoardTextStyle.MinFontSize, config.ResolvedClockDateFontSize, 9);
        Assert.Equal(ExamBoardTextStyle.MaxFontSize, config.ResolvedTitleFontSize, 9);
        Assert.Equal(ExamBoardTextStyle.MinFontSize, config.ResolvedExitIconSize, 9);

        // 收拾一遍之后两边必须落到同一个数上（设置页显示什么、窗口就画什么）。
        config.Normalize();

        Assert.Equal(config.ClockFontSize, config.ResolvedClockFontSize, 9);
        Assert.Equal(config.ClockDateFontSize, config.ResolvedClockDateFontSize, 9);
        Assert.Equal(config.TitleFontSize, config.ResolvedTitleFontSize, 9);
        Assert.Equal(config.ExitIconSize, config.ResolvedExitIconSize, 9);
    }

    /// <summary>
    ///     光标隐藏的等待时长：0（或负数）表示不隐藏，读成 <see cref="TimeSpan.Zero" />；
    ///     正数按秒换算。窗口拿它去起计时器，所以这里不能出现负的时长。
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    [InlineData(3, 3)]
    [InlineData(60, 60)]
    public void CursorHideDelay_IsZeroWhenDisabled(int seconds, int expectedSeconds)
    {
        var config = new DisplaySettingsConfig { HideCursorAfterSeconds = seconds };

        var delay = config.CursorHideDelay;

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
        Assert.True(delay >= TimeSpan.Zero);
    }

    /// <summary>
    ///     一条「全脏」的配置收拾完必须是完全合法的：时刻在区间内、字号在区间内。
    ///     这是读盘那条路上唯一的一道闸（<c>MainConfigModel.NormalizeLoaded</c>）。
    /// </summary>
    [Fact]
    public void Normalize_MakesACompletelyDirtyConfigUsable()
    {
        var config = new DisplaySettingsConfig
        {
            EnterHour = -100,
            EnterMinute = 100,
            ExitHour = 1000,
            ExitMinute = -1000,
            HideCursorAfterSeconds = 100000,
            ClockFontSize = double.NaN,
            ClockDateFontSize = 0,
            TitleFontSize = 1e9,
            ExitIconSize = double.NegativeInfinity,
            WeatherFontSize = 9999,
            QuoteFontSize = double.NaN
        };

        config.Normalize();

        Assert.InRange(config.EnterHour, 0, 23);
        Assert.InRange(config.EnterMinute, 0, 59);
        Assert.InRange(config.ExitHour, 0, 23);
        Assert.InRange(config.ExitMinute, 0, 59);
        Assert.InRange(config.HideCursorAfterSeconds, 0, 60);
        Assert.InRange(config.ClockFontSize, ExamBoardTextStyle.MinFontSize, ExamBoardTextStyle.MaxClockFontSize);
        Assert.InRange(config.ClockDateFontSize, ExamBoardTextStyle.MinFontSize, ExamBoardTextStyle.MaxFontSize);
        Assert.InRange(config.TitleFontSize, ExamBoardTextStyle.MinFontSize, ExamBoardTextStyle.MaxFontSize);
        Assert.InRange(config.ExitIconSize, ExamBoardTextStyle.MinFontSize, ExamBoardTextStyle.MaxFontSize);
        Assert.InRange(config.WeatherFontSize, DisplayTextStyle.MinWeatherFontSize, DisplayTextStyle.MaxWeatherFontSize);
        Assert.InRange(config.QuoteFontSize, DisplayTextStyle.MinQuoteFontSize, DisplayTextStyle.MaxQuoteFontSize);
    }

    /// <summary>收拾是幂等的：跑第二遍不会再改任何东西（配置层会反复调它）。</summary>
    [Fact]
    public void Normalize_IsIdempotent()
    {
        var config = new DisplaySettingsConfig
        {
            EnterHour = 99,
            EnterMinute = -1,
            HideCursorAfterSeconds = 999,
            ClockFontSize = 10000,
            ClockDateFontSize = double.NaN
        };

        config.Normalize();
        var snapshot = (config.EnterHour, config.EnterMinute, config.ExitHour, config.ExitMinute,
            config.HideCursorAfterSeconds, config.ClockFontSize, config.ClockDateFontSize,
            config.TitleFontSize, config.ExitIconSize);

        config.Normalize();

        Assert.Equal(snapshot, (config.EnterHour, config.EnterMinute, config.ExitHour, config.ExitMinute,
            config.HideCursorAfterSeconds, config.ClockFontSize, config.ClockDateFontSize,
            config.TitleFontSize, config.ExitIconSize));
    }

    /// <summary>
    ///     老配置里只有那四个数、没有时段列表：收拾时用它们补出第一段，
    ///     所以升级上来的用户原来设的「几点到几点」不会丢。
    /// </summary>
    [Fact]
    public void Normalize_BuildsTheFirstSlotFromTheOldFourNumbers()
    {
        var config = new DisplaySettingsConfig
        {
            EnterHour = 20,
            EnterMinute = 15,
            ExitHour = 22,
            ExitMinute = 45,
            Slots = []
        };

        config.Normalize();

        var slot = Assert.Single(config.Slots);
        Assert.Equal(new TimeOnly(20, 15), slot.EnterTime);
        Assert.Equal(new TimeOnly(22, 45), slot.ExitTime);
        Assert.True(slot.Enabled);
        Assert.Equal(DisplayDayFlags.EveryDay, slot.Days);
        Assert.Equal(new TimeOnly(20, 15), config.EnterTime);
    }

    /// <summary>时段列表存在时以它为准，并且把第一段写回那四个老字段（别处还在读）。</summary>
    [Fact]
    public void Normalize_KeepsTheSlotsAndSyncsTheOldFourNumbers()
    {
        var config = new DisplaySettingsConfig
        {
            Slots =
            [
                new DisplaySlot { EnterHour = 7, EnterMinute = 5, ExitHour = 8, ExitMinute = 0 },
                new DisplaySlot { EnterHour = 19, EnterMinute = 0, ExitHour = 21, ExitMinute = 30 }
            ]
        };

        config.Normalize();

        Assert.Equal(2, config.SlotCount);
        Assert.Equal(7, config.EnterHour);
        Assert.Equal(5, config.EnterMinute);
        Assert.Equal(8, config.ExitHour);
        Assert.Equal(0, config.ExitMinute);
    }

    /// <summary>段数有上限：手改出来的十几段被截到 <see cref="DisplaySlot.MaxCount" />。</summary>
    [Fact]
    public void Normalize_TrimsSlotsToTheMaximum()
    {
        var config = new DisplaySettingsConfig
        {
            Slots = [.. Enumerable.Range(0, DisplaySlot.MaxCount + 4).Select(_ => DisplaySlot.CreateDefault())]
        };

        config.Normalize();

        Assert.Equal(DisplaySlot.MaxCount, config.SlotCount);
    }

    /// <summary>每一段自己也要收拾：时刻夹取、星期脏值兜底。</summary>
    [Fact]
    public void Normalize_NormalizesEverySlot()
    {
        var config = new DisplaySettingsConfig
        {
            Slots =
            [
                new DisplaySlot { EnterHour = -3, EnterMinute = 99, ExitHour = 40, ExitMinute = -8, Days = DisplayDayFlags.None }
            ]
        };

        config.Normalize();

        var slot = Assert.Single(config.Slots);
        Assert.Equal(new TimeOnly(0, 59), slot.EnterTime);
        Assert.Equal(new TimeOnly(23, 0), slot.ExitTime);
        Assert.Equal(DisplayDayFlags.EveryDay, slot.Days);
    }

    /// <summary>时钟、作业、考试倒计时三样都关掉 = 屏幕上什么都不剩，设置页要靠这个提示用户。</summary>
    [Fact]
    public void EverythingHidden_OnlyWhenEveryPartIsOff()
    {
        Assert.False(new DisplaySettingsConfig().EverythingHidden);

        Assert.True(new DisplaySettingsConfig
        {
            ShowClock = false,
            ShowBoard = false,
            ShowExamCountdown = false
        }.EverythingHidden);
        Assert.False(new DisplaySettingsConfig { ShowClock = false, ShowBoard = true }.EverythingHidden);
        Assert.False(new DisplaySettingsConfig { ShowClock = true, ShowBoard = false }.EverythingHidden);

        // 倒计时也算一样东西：它挂在时钟那一格外面，时钟与作业都关掉之后还能自己显示。
        Assert.False(new DisplaySettingsConfig { ShowClock = false, ShowBoard = false }.EverythingHidden);
    }

    /// <summary>轮播停顿夹在 3–60 秒之间（太快读不完，太慢等于不滚）。</summary>
    [Theory]
    [InlineData(8, 8)]
    [InlineData(0, DisplaySettingsConfig.MinAutoScrollPauseSeconds)]
    [InlineData(-40, DisplaySettingsConfig.MinAutoScrollPauseSeconds)]
    [InlineData(600, DisplaySettingsConfig.MaxAutoScrollPauseSeconds)]
    public void Normalize_ClampsTheCarouselPause(int stored, int expected)
    {
        var config = new DisplaySettingsConfig { AutoScrollPauseSeconds = stored };

        config.Normalize();

        Assert.Equal(expected, config.AutoScrollPauseSeconds);
    }
}
