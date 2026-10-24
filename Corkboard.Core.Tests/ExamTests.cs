using System.Globalization;
using System.Text.Json;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Exam;
using Corkboard.Core.Models;
using Corkboard.Core.Services.Config;
using Corkboard.Shared.Abstraction;
using Microsoft.Extensions.Logging.Abstractions;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Core.Tests;

/// <summary>
///     考试状态的三档判定（未开始 / 即将开始 / 进行中 / 已结束）。
///     <para>
///     「现在」一律由参数传进来，用例里没有一处读 <see cref="DateTime.Now" />——
///     判定是纯函数，钉死时刻才守得住边界。
///     </para>
/// </summary>
public class ExamStatusPolicyTests
{
    /// <summary>一场固定的考试：2026-06-07 09:00 – 11:00（两小时）。</summary>
    private static readonly DateTime Start = new(2026, 6, 7, 9, 0, 0, DateTimeKind.Unspecified);

    private static readonly DateTime End = Start.AddMinutes(120);

    /// <summary>相对开考时刻偏移若干分钟的那个时刻。</summary>
    private static DateTime At(int minutesFromStart) => Start.AddMinutes(minutesFromStart);

    /// <summary>
    ///     三档边界都是<b>左闭右开</b>：<c>now &lt; start-阈值</c> 未开始；<c>start-阈值 ≤ now &lt; start</c>
    ///     即将开始（<b>边界那一分钟算即将开始</b>）；<c>start ≤ now &lt; end</c> 进行中；<c>now ≥ end</c> 已结束。
    ///     <para>守的是「差一分钟 / 正好那一分钟」这两处最容易写成 &lt; 还是 ≤ 的地方。</para>
    /// </summary>
    [Theory]
    [InlineData(30, -31, ExamStatus.NotStarted)] // 差 31 分钟：还没进窗口
    [InlineData(30, -30, ExamStatus.Upcoming)] // 边界：正好 30 分钟 → 即将开始
    [InlineData(30, -1, ExamStatus.Upcoming)] // 开考前一分钟
    [InlineData(30, 0, ExamStatus.InProgress)] // 边界：开考那一刻 → 进行中
    [InlineData(30, 119, ExamStatus.InProgress)] // 收卷前一分钟
    [InlineData(30, 120, ExamStatus.Finished)] // 边界：收卷那一刻 → 已结束
    [InlineData(30, 600, ExamStatus.Finished)] // 早就考完了
    public void Resolve_BoundariesAreLeftClosed(int upcomingMinutes, int offsetMinutes, ExamStatus expected)
    {
        Assert.Equal(expected, ExamStatusPolicy.Resolve(Start, End, At(offsetMinutes), upcomingMinutes));
    }

    /// <summary>
    ///     阈值 1 / 30 / 1440 都按原样生效；0 与负数被拉回 1（等于「开考前一分钟才算即将开始」），
    ///     超过一天被夹到 1440。手改过的 settings.json 不会把这一档弄成「永远即将开始」或「永不出现」。
    /// </summary>
    [Theory]
    [InlineData(1, -1, ExamStatus.Upcoming)]
    [InlineData(1, -2, ExamStatus.NotStarted)]
    [InlineData(0, -1, ExamStatus.Upcoming)] // 0 → 1
    [InlineData(0, -2, ExamStatus.NotStarted)]
    [InlineData(-5, -1, ExamStatus.Upcoming)] // 负数 → 1
    [InlineData(-5, -2, ExamStatus.NotStarted)]
    [InlineData(1440, -1440, ExamStatus.Upcoming)] // 一天：整整一天前仍算即将开始
    [InlineData(1440, -1441, ExamStatus.NotStarted)]
    [InlineData(5000, -1440, ExamStatus.Upcoming)] // 5000 → 1440
    [InlineData(5000, -1441, ExamStatus.NotStarted)]
    public void Resolve_ClampsUpcomingMinutes(int upcomingMinutes, int offsetMinutes, ExamStatus expected)
    {
        Assert.Equal(expected, ExamStatusPolicy.Resolve(Start, End, At(offsetMinutes), upcomingMinutes));
    }

    /// <summary>阈值本身的夹取区间：下限 1、上限一天，默认 30。</summary>
    [Fact]
    public void ClampUpcomingMinutes_KeepsThresholdWithinOneDay()
    {
        Assert.Equal(1, ExamStatusPolicy.ClampUpcomingMinutes(int.MinValue));
        Assert.Equal(1, ExamStatusPolicy.ClampUpcomingMinutes(-5));
        Assert.Equal(1, ExamStatusPolicy.ClampUpcomingMinutes(0));
        Assert.Equal(1, ExamStatusPolicy.ClampUpcomingMinutes(1));
        Assert.Equal(30, ExamStatusPolicy.ClampUpcomingMinutes(30));
        Assert.Equal(1440, ExamStatusPolicy.ClampUpcomingMinutes(1440));
        Assert.Equal(1440, ExamStatusPolicy.ClampUpcomingMinutes(5000));

        Assert.Equal(30, ExamStatusPolicy.DefaultUpcomingMinutes);
        Assert.Equal(1, ExamStatusPolicy.MinUpcomingMinutes);
        Assert.Equal(1440, ExamStatusPolicy.MaxUpcomingMinutes);
    }

    /// <summary>「剩余时间变红」门槛的夹取区间：0（不提醒）到一天，默认 15。</summary>
    [Fact]
    public void ClampRemainingWarningMinutes_AllowsZeroAsOff()
    {
        Assert.Equal(0, ExamStatusPolicy.ClampRemainingWarningMinutes(int.MinValue));
        Assert.Equal(0, ExamStatusPolicy.ClampRemainingWarningMinutes(-5));
        Assert.Equal(0, ExamStatusPolicy.ClampRemainingWarningMinutes(0));
        Assert.Equal(15, ExamStatusPolicy.ClampRemainingWarningMinutes(15));
        Assert.Equal(1440, ExamStatusPolicy.ClampRemainingWarningMinutes(1440));
        Assert.Equal(1440, ExamStatusPolicy.ClampRemainingWarningMinutes(5000));

        Assert.Equal(15, ExamStatusPolicy.DefaultRemainingWarningMinutes);
        Assert.Equal(0, ExamStatusPolicy.MinRemainingWarningMinutes);
        Assert.Equal(1440, ExamStatusPolicy.MaxRemainingWarningMinutes);
    }

    /// <summary>
    ///     剩余时间什么时候该转红：正在考看「离收卷还有多久」、即将开始看「离开考还有多久」，
    ///     到点（含正好等于门槛）就红；未开始 / 已结束不红；门槛 0 表示不提醒。
    /// </summary>
    [Fact]
    public void IsRemainingUrgent_WatchesTheCloserEdge()
    {
        var start = new DateTime(2026, 6, 7, 9, 0, 0, DateTimeKind.Unspecified);
        var exam = new ExamEntry
        {
            Subject = "数学",
            Date = new DateOnly(2026, 6, 7),
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(11, 0)
        };

        // 进行中：离收卷 10 分钟 → 红；20 分钟 → 不红；正好 15 分钟 → 红（门槛含等于）。
        Assert.True(ExamStatusPolicy.IsRemainingUrgent(ExamStatus.InProgress, exam, start.AddMinutes(110), 15));
        Assert.False(ExamStatusPolicy.IsRemainingUrgent(ExamStatus.InProgress, exam, start.AddMinutes(100), 15));
        Assert.True(ExamStatusPolicy.IsRemainingUrgent(ExamStatus.InProgress, exam, start.AddMinutes(105), 15));

        // 即将开始：离开考 5 分钟 → 红；25 分钟 → 不红。
        Assert.True(ExamStatusPolicy.IsRemainingUrgent(ExamStatus.Upcoming, exam, start.AddMinutes(-5), 15));
        Assert.False(ExamStatusPolicy.IsRemainingUrgent(ExamStatus.Upcoming, exam, start.AddMinutes(-25), 15));

        // 未开始 / 已结束不提醒，哪怕时间更近。
        Assert.False(ExamStatusPolicy.IsRemainingUrgent(ExamStatus.NotStarted, exam, start.AddHours(-3), 15));
        Assert.False(ExamStatusPolicy.IsRemainingUrgent(ExamStatus.Finished, exam, start.AddHours(5), 15));

        // 门槛 0 = 关掉提醒。
        Assert.False(ExamStatusPolicy.IsRemainingUrgent(ExamStatus.InProgress, exam, start.AddMinutes(110), 0));

        // 门槛超出区间按上限算（一天以内一律提醒）。
        Assert.True(ExamStatusPolicy.IsRemainingUrgent(ExamStatus.Upcoming, exam, start.AddMinutes(-5), 99999));
    }

    /// <summary>
    ///     收 <see cref="ExamEntry" /> 的那个重载走的是同一套规则，并且认 <see cref="ExamEntry.EndAt" />
    ///     的夹取——时间填反了（收卷早于开考）的考试没有「进行中」这一段，直接算考完。
    /// </summary>
    [Fact]
    public void Resolve_EntryOverloadHonoursClampedEnd()
    {
        var exam = new ExamEntry
        {
            Subject = "数学",
            Date = new DateOnly(2026, 6, 7),
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(11, 0)
        };

        Assert.Equal(ExamStatus.Upcoming, ExamStatusPolicy.Resolve(exam, At(-30), 30));
        Assert.Equal(ExamStatus.InProgress, ExamStatusPolicy.Resolve(exam, At(0), 30));
        Assert.Equal(ExamStatus.Finished, ExamStatusPolicy.Resolve(exam, At(120), 30));

        var reversed = new ExamEntry
        {
            Subject = "语文",
            Date = new DateOnly(2026, 6, 7),
            StartTime = new TimeOnly(11, 0),
            EndTime = new TimeOnly(9, 0)
        };

        // 收卷被夹成 11:00：11:00 之前是「即将开始」，到点直接「已结束」，中间没有「进行中」。
        Assert.Equal(ExamStatus.Upcoming, ExamStatusPolicy.Resolve(reversed, At(119), 30));
        Assert.Equal(ExamStatus.Finished, ExamStatusPolicy.Resolve(reversed, At(120), 30));
    }

    /// <summary>考试为空是调用方的 bug，要在入口就炸，不要静默当成「没有考试」。</summary>
    [Fact]
    public void Resolve_NullExamThrows()
    {
        Assert.Throws<ArgumentNullException>(() => ExamStatusPolicy.Resolve(null!, At(0), 30));
    }

    /// <summary>
    ///     「下一场」= 还没考完的那些里开考最早的一场；<b>正在进行的那场也算</b>（此刻该看的就是它）。
    ///     已经考完的哪怕开考更早也不能被选中。
    /// </summary>
    [Fact]
    public void FindNext_SkipsFinishedAndTakesEarliestStart()
    {
        var now = At(60); // 10:00

        var finished = Entry("历史", 8, 0, 9, 0);
        var inProgress = Entry("数学", 9, 30, 11, 0);
        var later = Entry("英语", 14, 0, 16, 0);

        Assert.Same(inProgress, ExamStatusPolicy.FindNext([finished, later, inProgress], now));
    }

    /// <summary>没有下一场（空集合 / 全都考完了 / 传 null）一律返回 null，调用方据此显示「没有考试」。</summary>
    [Fact]
    public void FindNext_ReturnsNullWhenNothingIsLeft()
    {
        var now = At(60);

        Assert.Null(ExamStatusPolicy.FindNext(null, now));
        Assert.Null(ExamStatusPolicy.FindNext([], now));
        Assert.Null(ExamStatusPolicy.FindNext([Entry("历史", 8, 0, 9, 0), Entry("数学", 8, 30, 10, 0)], now));
    }

    /// <summary>
    ///     看板列表：只留没考完的、按开考时刻升序。考完的<b>不进列表</b>（看板是给「接下来考什么」用的），
    ///     正在进行的那场留在列表里（它还占着今天）。
    /// </summary>
    [Fact]
    public void ListUpcoming_KeepsUnfinishedAndSortsByStart()
    {
        var now = At(60); // 10:00

        var finished = Entry("历史", 8, 0, 9, 0);
        var inProgress = Entry("数学", 9, 30, 11, 0);
        var soon = Entry("英语", 9, 45, 11, 30);
        var later = Entry("物理", 14, 0, 16, 0);

        var list = ExamStatusPolicy.ListUpcoming([later, soon, finished, inProgress], now);

        Assert.Equal(["数学", "英语", "物理"], list.Select(exam => exam.Subject));

        // 传进来的集合不能被排序改动（服务里那份是活的 ObservableCollection）。
        Assert.Equal(["物理", "英语", "历史", "数学"], new[] { later, soon, finished, inProgress }.Select(e => e.Subject));
    }

    /// <summary>空集合与 null 都返回空列表（不是 null），界面直接绑就行。</summary>
    [Fact]
    public void ListUpcoming_ReturnsEmptyListForNothing()
    {
        Assert.Empty(ExamStatusPolicy.ListUpcoming(null, At(60)));
        Assert.Empty(ExamStatusPolicy.ListUpcoming([], At(60)));
    }

    /// <summary>造一条当天第几点的考试（日期统一 2026-06-07，与 <see cref="Start" /> 同一天）。</summary>
    private static ExamEntry Entry(string subject, int startHour, int startMinute, int endHour, int endMinute) => new()
    {
        Subject = subject,
        Date = new DateOnly(2026, 6, 7),
        StartTime = new TimeOnly(startHour, startMinute),
        EndTime = new TimeOnly(endHour, endMinute)
    };
}

/// <summary>
///     <see cref="ExamEntry" /> 自己那几条派生量：收卷时刻的夹取、时长、卷面信息的有无、
///     卷面数字的夹取，以及编辑表单要用的 <see cref="ExamEntry.Clone" /> / <see cref="ExamEntry.CopyFrom" />。
/// </summary>
public class ExamEntryTests
{
    /// <summary>
    ///     收卷时刻早于开考（历史数据或手改 JSON 里存反了）时，<see cref="ExamEntry.EndAt" /> 夹成开考那一刻：
    ///     那场考试就没有「进行中」这一段，也不会把后面的安排顶掉。
    /// </summary>
    [Fact]
    public void EndAt_FallsBackToStartWhenTimeIsReversed()
    {
        var normal = new ExamEntry
        {
            Date = new DateOnly(2026, 6, 7),
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(11, 0)
        };
        Assert.Equal(new DateTime(2026, 6, 7, 11, 0, 0), normal.EndAt);

        var reversed = new ExamEntry
        {
            Date = new DateOnly(2026, 6, 7),
            StartTime = new TimeOnly(11, 0),
            EndTime = new TimeOnly(9, 0)
        };
        Assert.Equal(new DateTime(2026, 6, 7, 11, 0, 0), reversed.StartAt);
        Assert.Equal(reversed.StartAt, reversed.EndAt);
        Assert.Equal(TimeSpan.Zero, reversed.Duration);
    }

    /// <summary>时长就是收卷减开考；收卷填成与开考同一时刻时是 0（不是负数）。</summary>
    [Fact]
    public void Duration_IsEndMinusStart()
    {
        var exam = new ExamEntry
        {
            Date = new DateOnly(2026, 6, 7),
            StartTime = new TimeOnly(8, 30),
            EndTime = new TimeOnly(10, 0)
        };

        Assert.Equal(TimeSpan.FromMinutes(90), exam.Duration);

        exam.EndTime = new TimeOnly(8, 30);
        Assert.Equal(TimeSpan.Zero, exam.Duration);
    }

    /// <summary>卷面信息三项都没填时看板不画那一行；只要有一项有值就要画。</summary>
    [Fact]
    public void HasPaperInfo_IsFalseOnlyWhenAllThreeAreNull()
    {
        var exam = new ExamEntry();
        Assert.False(exam.HasPaperInfo);

        exam.PaperCount = 2;
        Assert.True(exam.HasPaperInfo);
        exam.PaperCount = null;

        exam.PageCount = 8;
        Assert.True(exam.HasPaperInfo);
        exam.PageCount = null;

        exam.QuestionCount = 3;
        Assert.True(exam.HasPaperInfo);
    }

    /// <summary>
    ///     卷面数字的夹取：≤ 0 一律当「没填」（不显示「0 张」这种噪音），
    ///     上限挡一下手改出来的天文数字（1000 → 999）。
    /// </summary>
    [Fact]
    public void NormalizeCount_TreatsNonPositiveAsUnsetAndCapsTheRest()
    {
        Assert.Null(ExamEntry.NormalizeCount(null));
        Assert.Null(ExamEntry.NormalizeCount(0));
        Assert.Null(ExamEntry.NormalizeCount(-5));
        Assert.Equal(1, ExamEntry.NormalizeCount(1));
        Assert.Equal(42, ExamEntry.NormalizeCount(42));
        Assert.Equal(999, ExamEntry.NormalizeCount(999));
        Assert.Equal(999, ExamEntry.NormalizeCount(1000));
        Assert.Equal(999, ExamEntry.NormalizeCount(int.MaxValue));
        Assert.Equal(999, ExamEntry.MaxPaperInfoValue);
    }

    /// <summary>
    ///     编辑表单拿副本改、取消时不能影响原数据：<see cref="ExamEntry.Clone" /> 得是独立的一份，
    ///     改副本的科目 / 日期 / 卷面数字都碰不到原件（Id 保持一致，保存时按它写回）。
    /// </summary>
    [Fact]
    public void Clone_IsAnIndependentCopy()
    {
        var original = new ExamEntry
        {
            Subject = "数学",
            Date = new DateOnly(2026, 6, 7),
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(11, 0),
            PaperCount = 2,
            PageCount = 8,
            QuestionCount = 3,
            Note = "第三考场"
        };

        var copy = original.Clone();

        Assert.NotSame(original, copy);
        Assert.Equal(original.Id, copy.Id);
        Assert.Equal("数学", copy.Subject);
        Assert.Equal(new DateOnly(2026, 6, 7), copy.Date);
        Assert.Equal(new TimeOnly(9, 0), copy.StartTime);
        Assert.Equal(new TimeOnly(11, 0), copy.EndTime);
        Assert.Equal(2, copy.PaperCount);
        Assert.Equal(8, copy.PageCount);
        Assert.Equal(3, copy.QuestionCount);
        Assert.Equal("第三考场", copy.Note);

        // 改副本，原件一个字都不动。
        copy.Subject = "语文";
        copy.Date = new DateOnly(2027, 1, 2);
        copy.StartTime = new TimeOnly(14, 0);
        copy.EndTime = new TimeOnly(16, 0);
        copy.PaperCount = null;
        copy.PageCount = 99;
        copy.Note = null;

        Assert.Equal("数学", original.Subject);
        Assert.Equal(new DateOnly(2026, 6, 7), original.Date);
        Assert.Equal(new TimeOnly(9, 0), original.StartTime);
        Assert.Equal(new TimeOnly(11, 0), original.EndTime);
        Assert.Equal(2, original.PaperCount);
        Assert.Equal(8, original.PageCount);
        Assert.Equal("第三考场", original.Note);
    }

    /// <summary>
    ///     <see cref="ExamEntry.CopyFrom" /> 只盖内容、<b>不动 Id</b>：
    ///     表单保存时是把副本的内容盖回服务里那条，Id 一变就变成「删一条、加一条」。
    /// </summary>
    [Fact]
    public void CopyFrom_KeepsOwnId()
    {
        var targetId = Guid.NewGuid();
        var target = new ExamEntry { Id = targetId, Subject = "数学" };
        var source = new ExamEntry
        {
            Subject = "语文",
            Date = new DateOnly(2026, 6, 8),
            StartTime = new TimeOnly(14, 0),
            EndTime = new TimeOnly(16, 0),
            PaperCount = 1,
            PageCount = 4,
            QuestionCount = 20,
            Note = "带计算器"
        };

        target.CopyFrom(source);

        Assert.Equal(targetId, target.Id);
        Assert.Equal("语文", target.Subject);
        Assert.Equal(new DateOnly(2026, 6, 8), target.Date);
        Assert.Equal(new TimeOnly(14, 0), target.StartTime);
        Assert.Equal(new TimeOnly(16, 0), target.EndTime);
        Assert.Equal(1, target.PaperCount);
        Assert.Equal(4, target.PageCount);
        Assert.Equal(20, target.QuestionCount);
        Assert.Equal("带计算器", target.Note);

        Assert.Throws<ArgumentNullException>(() => target.CopyFrom(null!));
    }

    /// <summary>
    ///     读盘时的统一收拾：科目去首尾空白、卷面数字夹取、空白备注当没填。
    ///     返回的是<b>新对象</b>，传进来的那份不动（服务拿它建集合，界面上那份可能还挂在表单里）。
    /// </summary>
    [Fact]
    public void Normalize_TrimsFieldsAndReturnsANewInstance()
    {
        var source = new ExamEntry
        {
            Subject = "  数学  ",
            PaperCount = 0,
            PageCount = 5000,
            QuestionCount = 12,
            Note = "   "
        };

        var normalized = ExamEntry.Normalize(source);

        Assert.NotSame(source, normalized);
        Assert.Equal("数学", normalized.Subject);
        Assert.Null(normalized.PaperCount);
        Assert.Equal(999, normalized.PageCount);
        Assert.Equal(12, normalized.QuestionCount);
        Assert.Null(normalized.Note);

        // 原件不动。
        Assert.Equal("  数学  ", source.Subject);
        Assert.Equal(0, source.PaperCount);
        Assert.Equal("   ", source.Note);

        Assert.Throws<ArgumentNullException>(() => ExamEntry.Normalize(null!));
    }
}

/// <summary>
///     考试文案的拼装：日期、时间区间、时长、倒计时、卷面信息。
///     断言一律拿资源串（<c>CR.*</c>）拼期望值，不写死中文——CI 可能跑在 en-US 上。
/// </summary>
public class ExamTimeFormatterTests
{
    /// <summary>「今天」钉在 2026-03-02，星期几跟着当前语言走（中文「周一」、英文「Mon」）。</summary>
    private static readonly DateOnly Today = new(2026, 3, 2);

    /// <summary>今天 / 明天 / 后天用相对说法，别写日期。</summary>
    [Fact]
    public void FormatDate_SaysNearbyDaysInWords()
    {
        Assert.Equal(CR.Exam_Date_Today, ExamTimeFormatter.FormatDate(Today, Today));
        Assert.Equal(CR.Exam_Date_Tomorrow, ExamTimeFormatter.FormatDate(Today.AddDays(1), Today));
        Assert.Equal(CR.Exam_Date_DayAfterTomorrow, ExamTimeFormatter.FormatDate(Today.AddDays(2), Today));
    }

    /// <summary>
    ///     再远就写「3月9日 周一」：同年不带年份（看板一行放得下），
    ///     跨年必须带上年份，否则 12 月底看「1月2日」会以为已经过了。
    /// </summary>
    [Fact]
    public void FormatDate_WritesMonthDayAndAddsYearOnlyWhenCrossing()
    {
        var sameYear = Today.AddDays(7);
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, CR.Exam_Date_Format, sameYear.Month, sameYear.Day,
                ExamTimeFormatter.WeekdayName(sameYear)),
            ExamTimeFormatter.FormatDate(sameYear, Today));

        // 已经过去的日期同样走这条分支（不是「昨天」这种说法）。
        var past = Today.AddDays(-3);
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, CR.Exam_Date_Format, past.Month, past.Day,
                ExamTimeFormatter.WeekdayName(past)),
            ExamTimeFormatter.FormatDate(past, Today));

        var yearEnd = new DateOnly(2026, 12, 30);
        var nextYear = new DateOnly(2027, 1, 2);
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, CR.Exam_Date_FormatWithYear, nextYear.Year, nextYear.Month,
                nextYear.Day, ExamTimeFormatter.WeekdayName(nextYear)),
            ExamTimeFormatter.FormatDate(nextYear, yearEnd));
    }

    /// <summary>
    ///     时间区间是 <c>HH:mm – HH:mm</c>：24 小时制、两位补零、中间是 en dash 且<b>前后各一个空格</b>
    ///     （用「至」或者用连字符都和界面上的排版对不上）。
    /// </summary>
    [Fact]
    public void FormatTimeRange_IsHourMinuteWithSpacedEnDash()
    {
        var exam = new ExamEntry
        {
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(10, 5)
        };

        var text = ExamTimeFormatter.FormatTimeRange(exam);

        Assert.Equal("08:00 \u2013 10:05", text);
        Assert.Contains('\u2013', text);
        Assert.Equal("08:00", ExamTimeFormatter.FormatTime(exam.StartTime));
        Assert.Equal("13:45", ExamTimeFormatter.FormatTime(new TimeOnly(13, 45)));

        Assert.Throws<ArgumentNullException>(() => ExamTimeFormatter.FormatTimeRange(null!));
    }

    /// <summary>
    ///     卷面信息那一行只拼有值的项、用「 · 」分隔；一项都没填时返回空串，
    ///     调用方据此决定要不要画那一行（不能画出一条只有分隔符的空行）。
    /// </summary>
    [Fact]
    public void FormatPaperInfo_JoinsOnlyTheFilledFields()
    {
        Assert.Equal(string.Empty, ExamTimeFormatter.FormatPaperInfo(new ExamEntry()));

        var full = new ExamEntry { PaperCount = 2, PageCount = 8, QuestionCount = 3 };
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, CR.Exam_PaperCount, 2) + CR.Exam_PaperInfoSeparator +
            string.Format(CultureInfo.CurrentCulture, CR.Exam_PageCount, 8) + CR.Exam_PaperInfoSeparator +
            string.Format(CultureInfo.CurrentCulture, CR.Exam_QuestionCount, 3),
            ExamTimeFormatter.FormatPaperInfo(full));

        // 只填中间那一项：不能留下开头 / 结尾的分隔符。
        var onlyPages = new ExamEntry { PageCount = 8 };
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, CR.Exam_PageCount, 8),
            ExamTimeFormatter.FormatPaperInfo(onlyPages));

        var onlyQuestions = new ExamEntry { QuestionCount = 12 };
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, CR.Exam_QuestionCount, 12),
            ExamTimeFormatter.FormatPaperInfo(onlyQuestions));

        Assert.Throws<ArgumentNullException>(() => ExamTimeFormatter.FormatPaperInfo(null!));
    }

    /// <summary>
    ///     时长说法：「2 小时 15 分」/「2 小时」/「45 分钟」/「不到 1 分钟」。
    ///     零与负数都说「不到 1 分钟」（负数来自时间填反，不能显示成负时长）。
    /// </summary>
    [Fact]
    public void FormatDuration_CoversMinutesHoursAndEmpty()
    {
        Assert.Equal(CR.Exam_Duration_LessThanMinute, ExamTimeFormatter.FormatDuration(TimeSpan.Zero));
        Assert.Equal(CR.Exam_Duration_LessThanMinute, ExamTimeFormatter.FormatDuration(TimeSpan.FromMinutes(-5)));

        // 只要大于零就向上取整成至少 1 分钟（宁可多说一分钟，也不显示「0 分钟」）。
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, CR.Exam_Duration_Minutes, 1),
            ExamTimeFormatter.FormatDuration(TimeSpan.FromSeconds(30)));
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, CR.Exam_Duration_Minutes, 45),
            ExamTimeFormatter.FormatDuration(TimeSpan.FromMinutes(45)));

        Assert.Equal(string.Format(CultureInfo.CurrentCulture, CR.Exam_Duration_Hours, 2),
            ExamTimeFormatter.FormatDuration(TimeSpan.FromHours(2)));
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, CR.Exam_Duration_HoursMinutes, 2, 15),
            ExamTimeFormatter.FormatDuration(new TimeSpan(2, 15, 0)));
    }

    /// <summary>
    ///     倒计时：满一天就说「还有 N 天」（向上取整，1 天 1 小时说 2 天），
    ///     不到一天就退回「还有 5 小时 20 分」。负数（已经开考）说「不到 1 分钟」，不出现负号。
    /// </summary>
    [Fact]
    public void FormatCountdown_SwitchesFromDaysToHours()
    {
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, CR.Exam_Countdown_Days, 3),
            ExamTimeFormatter.FormatCountdown(TimeSpan.FromDays(3)));

        // 1 天 1 小时向上取整成 2 天。
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, CR.Exam_Countdown_Days, 2),
            ExamTimeFormatter.FormatCountdown(TimeSpan.FromHours(25)));

        Assert.Equal(string.Format(CultureInfo.CurrentCulture, CR.Exam_Countdown_Days, 1),
            ExamTimeFormatter.FormatCountdown(TimeSpan.FromDays(1)));

        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, CR.Exam_Countdown_Remaining,
                string.Format(CultureInfo.CurrentCulture, CR.Exam_Duration_HoursMinutes, 5, 20)),
            ExamTimeFormatter.FormatCountdown(new TimeSpan(5, 20, 0)));

        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, CR.Exam_Countdown_Remaining,
                string.Format(CultureInfo.CurrentCulture, CR.Exam_Duration_Minutes, 12)),
            ExamTimeFormatter.FormatCountdown(TimeSpan.FromMinutes(12)));

        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, CR.Exam_Countdown_Remaining, CR.Exam_Duration_LessThanMinute),
            ExamTimeFormatter.FormatCountdown(TimeSpan.FromMinutes(-3)));
    }

    /// <summary>状态短名四档都要有话说（枚举默认分支落回「未开始」，不能冒出空串）。</summary>
    [Fact]
    public void FormatStatus_NamesEveryState()
    {
        Assert.Equal(CR.Exam_Status_NotStarted, ExamTimeFormatter.FormatStatus(ExamStatus.NotStarted));
        Assert.Equal(CR.Exam_Status_Upcoming, ExamTimeFormatter.FormatStatus(ExamStatus.Upcoming));
        Assert.Equal(CR.Exam_Status_InProgress, ExamTimeFormatter.FormatStatus(ExamStatus.InProgress));
        Assert.Equal(CR.Exam_Status_Finished, ExamTimeFormatter.FormatStatus(ExamStatus.Finished));
    }

    /// <summary>
    ///     状态那一列的完整说法：即将开始补「还有多久开考」、进行中补「还剩多久收卷」，
    /// 其余档只有状态名。这里的「现在」也是参数，不从时钟读。
    /// </summary>
    [Fact]
    public void FormatStatusDetail_AddsTheOneThingWorthKnowing()
    {
        var start = new DateTime(2026, 6, 7, 9, 0, 0, DateTimeKind.Unspecified);
        var exam = new ExamEntry
        {
            Subject = "数学",
            Date = new DateOnly(2026, 6, 7),
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(11, 0)
        };

        var upcoming = ExamTimeFormatter.FormatStatusDetail(ExamStatus.Upcoming, exam, start.AddMinutes(-12));
        Assert.Equal(
            $"{CR.Exam_Status_Upcoming} \u00b7 " + string.Format(CultureInfo.CurrentCulture,
                CR.Exam_Countdown_Remaining, string.Format(CultureInfo.CurrentCulture, CR.Exam_Duration_Minutes, 12)),
            upcoming);

        var inProgress = ExamTimeFormatter.FormatStatusDetail(ExamStatus.InProgress, exam, start.AddMinutes(80));
        Assert.Equal(
            $"{CR.Exam_Status_InProgress} \u00b7 " + string.Format(CultureInfo.CurrentCulture,
                CR.Exam_Status_Remaining, string.Format(CultureInfo.CurrentCulture, CR.Exam_Duration_Minutes, 40)),
            inProgress);

        // 未开始 / 已结束只说状态名，不硬凑一句话。
        Assert.Equal(CR.Exam_Status_NotStarted,
            ExamTimeFormatter.FormatStatusDetail(ExamStatus.NotStarted, exam, start.AddDays(-3)));
        Assert.Equal(CR.Exam_Status_Finished,
            ExamTimeFormatter.FormatStatusDetail(ExamStatus.Finished, exam, start.AddHours(5)));

        Assert.Throws<ArgumentNullException>(() => ExamTimeFormatter.FormatStatusDetail(ExamStatus.Upcoming, null!, start));
    }

    /// <summary>
    ///     看板左侧那组「考试信息」的抬头与倒计时：离得远时说「下一场 / 还有 3 天」，
    ///     快要考试说「即将开始」，正在考说「进行中 / 还剩 40 分钟」——两行都要给得出字，
    ///     否则看板左边会留下一个没有倒计时的空块。
    /// </summary>
    [Fact]
    public void FormatInfoCaption_AndRemaining_AlwaysHaveSomethingToSay()
    {
        var start = new DateTime(2026, 6, 7, 9, 0, 0, DateTimeKind.Unspecified);
        var exam = new ExamEntry
        {
            Subject = "数学",
            Date = new DateOnly(2026, 6, 7),
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(11, 0)
        };

        // 离得远：抬头说「下一场」（不写「未开始」，那等于什么都没说），倒计时按天说。
        Assert.Equal(CR.Exam_Board_NextExam, ExamTimeFormatter.FormatInfoCaption(ExamStatus.NotStarted));
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, CR.Exam_Countdown_Days, 3),
            ExamTimeFormatter.FormatInfoRemaining(ExamStatus.NotStarted, exam, start.AddDays(-3), false));

        // 即将开始 / 进行中：抬头用状态名，倒计时与表格里那一句是同一套说法。
        Assert.Equal(CR.Exam_Status_Upcoming, ExamTimeFormatter.FormatInfoCaption(ExamStatus.Upcoming));
        Assert.Equal(
            ExamTimeFormatter.FormatStatusHint(ExamStatus.Upcoming, exam, start.AddMinutes(-12)),
            ExamTimeFormatter.FormatInfoRemaining(ExamStatus.Upcoming, exam, start.AddMinutes(-12), false));

        Assert.Equal(CR.Exam_Status_InProgress, ExamTimeFormatter.FormatInfoCaption(ExamStatus.InProgress));
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, CR.Exam_Status_Remaining,
                string.Format(CultureInfo.CurrentCulture, CR.Exam_Duration_Minutes, 40)),
            ExamTimeFormatter.FormatInfoRemaining(ExamStatus.InProgress, exam, start.AddMinutes(80), false));

        Assert.Throws<ArgumentNullException>(() =>
            ExamTimeFormatter.FormatInfoRemaining(ExamStatus.InProgress, null!, start, false));
    }

    /// <summary>
    ///     倒计时的精确度：一天以上按天说；一小时以上说「X 小时 Y 分」（不是 187 分钟）；
    ///     最后一小时说分钟；开了「精确到秒」再连秒一起跳，关着的时候不到一分钟仍然说「不到 1 分钟」。
    /// </summary>
    [Fact]
    public void FormatInfoRemaining_PrecisionRules()
    {
        var start = new DateTime(2026, 6, 7, 9, 0, 0, DateTimeKind.Unspecified);
        var exam = new ExamEntry
        {
            Subject = "数学",
            Date = new DateOnly(2026, 6, 7),
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(11, 0)
        };

        Assert.Equal(Remaining0(Hours(2)), Remaining(ExamStatus.InProgress, exam, start, false));
        Assert.Equal(Remaining0(HoursMinutes(1, 30)), Remaining(ExamStatus.InProgress, exam, start.AddMinutes(30), false));
        Assert.Equal(Remaining0(HoursMinutes(1, 1)), Remaining(ExamStatus.InProgress, exam, start.AddMinutes(59), false));

        // 最后一小时按分钟说，向下取整：40 分 30 秒 → 「40 分钟」（宁可少说，不让人以为还有时间）。
        Assert.Equal(Remaining0(Minutes(40)),
            Remaining(ExamStatus.InProgress, exam, start.AddMinutes(79).AddSeconds(30), false));
        Assert.Equal(Remaining0(CR.Exam_Duration_LessThanMinute),
            Remaining(ExamStatus.InProgress, exam, start.AddMinutes(119).AddSeconds(45), false));

        // 开了秒：最后一小时按「分 秒」跳，不到一分钟只写秒。
        Assert.Equal(Remaining0(MinutesSeconds(40, 30)),
            Remaining(ExamStatus.InProgress, exam, start.AddMinutes(79).AddSeconds(30), true));
        Assert.Equal(Remaining0(Seconds(15)),
            Remaining(ExamStatus.InProgress, exam, start.AddMinutes(119).AddSeconds(45), true));

        // 即将开始也一样（前置词变成「还有」），但一小时以上不带秒。
        Assert.Equal(Countdown0(HoursMinutes(1, 30)), Remaining(ExamStatus.Upcoming, exam, start.AddMinutes(-90), true));
        Assert.Equal(Countdown0(MinutesSeconds(5, 20)),
            Remaining(ExamStatus.Upcoming, exam, start.AddMinutes(-6).AddSeconds(40), true));

        // 一天以上只按天说，开不开秒都一样。
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, CR.Exam_Countdown_Days, 3),
            Remaining(ExamStatus.NotStarted, exam, start.AddDays(-3), true));

        static string Remaining(ExamStatus status, ExamEntry exam, DateTime now, bool seconds) =>
            ExamTimeFormatter.FormatInfoRemaining(status, exam, now, seconds);

        static string Remaining0(string phrase) =>
            string.Format(CultureInfo.CurrentCulture, CR.Exam_Status_Remaining, phrase);

        static string Countdown0(string phrase) =>
            string.Format(CultureInfo.CurrentCulture, CR.Exam_Countdown_Remaining, phrase);

        static string Hours(int hours) => string.Format(CultureInfo.CurrentCulture, CR.Exam_Duration_Hours, hours);

        static string HoursMinutes(int hours, int minutes) =>
            string.Format(CultureInfo.CurrentCulture, CR.Exam_Duration_HoursMinutes, hours, minutes);

        static string Minutes(int minutes) =>
            string.Format(CultureInfo.CurrentCulture, CR.Exam_Duration_Minutes, minutes);

        static string MinutesSeconds(int minutes, int seconds) =>
            string.Format(CultureInfo.CurrentCulture, CR.Exam_Duration_MinutesSeconds, minutes, seconds);

        static string Seconds(int seconds) =>
            string.Format(CultureInfo.CurrentCulture, CR.Exam_Duration_Seconds, seconds);
    }

    /// <summary>
    ///     状态底下那句提示单独拿出来：全屏看板把状态名与这句分两行放，
    ///     两半拼起来必须与 <see cref="ExamTimeFormatter.FormatStatusDetail" /> 一模一样，
    ///     未开始 / 已结束那两档必须是空串（调用方据此隐藏那一行）。
    /// </summary>
    [Fact]
    public void FormatStatusHint_IsTheSecondHalfOfStatusDetail()
    {
        var start = new DateTime(2026, 6, 7, 9, 0, 0, DateTimeKind.Unspecified);
        var exam = new ExamEntry
        {
            Subject = "数学",
            Date = new DateOnly(2026, 6, 7),
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(11, 0)
        };

        foreach (var (status, now) in new[]
                 {
                     (ExamStatus.NotStarted, start.AddDays(-3)),
                     (ExamStatus.Upcoming, start.AddMinutes(-12)),
                     (ExamStatus.InProgress, start.AddMinutes(80)),
                     (ExamStatus.Finished, start.AddHours(5))
                 })
        {
            var hint = ExamTimeFormatter.FormatStatusHint(status, exam, now);
            var detail = ExamTimeFormatter.FormatStatusDetail(status, exam, now);

            if (hint.Length == 0)
            {
                Assert.Equal(ExamTimeFormatter.FormatStatus(status), detail);
            }
            else
            {
                Assert.Equal($"{ExamTimeFormatter.FormatStatus(status)} \u00b7 {hint}", detail);
            }
        }

        // 未开始与已结束没有可补的那句话。
        Assert.Equal(string.Empty, ExamTimeFormatter.FormatStatusHint(ExamStatus.NotStarted, exam, start.AddDays(-3)));
        Assert.Equal(string.Empty, ExamTimeFormatter.FormatStatusHint(ExamStatus.Finished, exam, start.AddHours(5)));

        Assert.Throws<ArgumentNullException>(() => ExamTimeFormatter.FormatStatusHint(ExamStatus.Upcoming, null!, start));
    }
}

/// <summary>
///     考试看板服务：一块看板 = 一个文件（标题、副标题、考试安排都在里面），
///     加 / 改 / 删都要落盘并通知界面，从磁盘重读要能把脏数据收拾干净。
///     <para>
///     配置文件全部指向临时沙箱（<c>SandboxConfigService</c> 只按文件名映射到沙箱目录，
///     不认 <c>Utils</c> 解析出的真实路径），绝不碰开发机上的真实数据根。
///     </para>
/// </summary>
public class ExamServiceTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(
        Path.GetTempPath(), "corkboard-exam", Guid.NewGuid().ToString("N"));

    private string IndexPath => Path.Combine(_sandbox, ExamBoardIndexConfig.StorageFileName);

    private string LegacyPath => Path.Combine(_sandbox, "exams.json");

    private string SettingsPath => Path.Combine(_sandbox, "settings.json");

    private string BoardPath(Guid id) => Path.Combine(_sandbox, id.ToString("N") + ".json");

    private ExamService CreateService() => new(new SandboxConfigService(_sandbox), CreateHandler());

    private MainConfigHandler CreateHandler() =>
        new(NullLogger<MainConfigHandler>.Instance, new SandboxConfigService(_sandbox));

    /// <summary>
    ///     把配置写到沙箱里的 JSON 文件：真实序列化，但不碰 <c>Utils</c> 解析出的真实路径。
    ///     看板、索引、老版考试安排都只靠文件名区分，拍平到同一个目录里不会撞名。
    /// </summary>
    private sealed class SandboxConfigService(string root) : ConfigServiceBase
    {
        private string Resolve(object config) =>
            Path.Combine(root, Path.GetFileName(((ConfigBase)config).ConfigFilePath));

        public override bool IsConfigExists<T>(T fallback) => File.Exists(Resolve(fallback!));

        public override T LoadConfig<T>(T fallback)
        {
            var path = Resolve(fallback!);
            if (!File.Exists(path))
                return fallback;

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
        }

        public override void SaveConfig<T>(T config)
        {
            var path = Resolve(config!);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
        }

        public override void DeleteConfig<T>(T config)
        {
            var path = Resolve(config!);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    /// <summary>写一份老版本的单份考试安排（<c>data/exam/exams.json</c>），模拟升级前留下的数据。</summary>
    private void WriteLegacySchedule(params ExamEntry[] exams)
    {
        Directory.CreateDirectory(_sandbox);
        var config = new ExamScheduleConfig { Exams = [.. exams] };
        File.WriteAllText(LegacyPath, JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions));
    }

    /// <summary>直接往沙箱里写一份看板文件，模拟手改过 / 云同步拉回来的文件。</summary>
    private void WriteBoard(ExamBoardDocument board)
    {
        Directory.CreateDirectory(_sandbox);
        File.WriteAllText(BoardPath(board.Id), JsonSerializer.Serialize(board, ConfigServiceBase.JsonOptions));
    }

    /// <summary>写一份看板索引（有哪些看板、当前是哪一块）。</summary>
    private void WriteIndex(Guid[] boardIds, Guid activeBoardId)
    {
        Directory.CreateDirectory(_sandbox);
        var index = new ExamBoardIndexConfig { BoardIds = [.. boardIds], ActiveBoardId = activeBoardId };
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(index, ConfigServiceBase.JsonOptions));
    }

    /// <summary>写一份 <c>settings.json</c>，用来模拟老版本把标题写在全局配置里的情况。</summary>
    private void WriteSettings(Action<MainConfigModel> mutate)
    {
        Directory.CreateDirectory(_sandbox);
        var model = new MainConfigModel();
        mutate(model);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(model, ConfigServiceBase.JsonOptions));
    }

    private static ExamEntry MathExam() => new()
    {
        Subject = "数学",
        Date = new DateOnly(2026, 6, 7),
        StartTime = new TimeOnly(9, 0),
        EndTime = new TimeOnly(11, 0),
        PaperCount = 2,
        PageCount = 8,
        QuestionCount = 3
    };

    /// <summary>
    ///     新增考试：Id 为空的那条要当场分配 Id（表单新建时就是空的），
    ///     并且要触发一次 <see cref="IExamService.Changed" />——界面靠它重建列表。
    /// </summary>
    [Fact]
    public void Add_AssignsIdAndRaisesChanged()
    {
        var service = CreateService();
        var raised = 0;
        service.Changed += (_, _) => raised++;

        var added = service.Add(new ExamEntry { Id = Guid.Empty, Subject = "  数学  " });

        Assert.NotEqual(Guid.Empty, added.Id);
        Assert.Equal("数学", added.Subject); // 落盘前统一收拾过
        Assert.Equal(1, raised);

        var stored = Assert.Single(service.Exams);
        Assert.Equal(added.Id, stored.Id);
        Assert.Same(added, stored); // 存的就是收拾后的那一份，不是调用方手里那份
    }

    /// <summary>
    ///     新增时分配的 Id 要**回填到调用方手里那份**：否则调用方拿原引用接着
    ///     <see cref="IExamService.Update" /> / <see cref="IExamService.Remove" /> 必然失败（Id 还是空的）。
    /// </summary>
    [Fact]
    public void Add_BackfillsTheGeneratedIdOntoTheCallerEntry()
    {
        var service = CreateService();
        var draft = new ExamEntry { Subject = "数学" };

        var added = service.Add(draft);

        Assert.NotEqual(Guid.Empty, draft.Id);
        Assert.Equal(added.Id, draft.Id);

        draft.Subject = "数学（补考）";
        Assert.True(service.Update(draft));
        Assert.True(service.Remove(draft.Id));
    }

    /// <summary>
    ///     空科目的考试一律不许进服务：读盘时这类条目会被丢掉，存进来就成了
    ///     「界面上看得见、下次启动就消失」的空行。
    /// </summary>
    [Fact]
    public void AddAndUpdate_RejectBlankSubject()
    {
        var service = CreateService();

        Assert.Throws<ArgumentException>(() => service.Add(new ExamEntry { Subject = "   " }));

        var added = service.Add(MathExam());
        var blank = added.Clone();
        blank.Subject = "  ";
        Assert.Throws<ArgumentException>(() => service.Update(blank));

        Assert.Single(service.Exams);
    }

    /// <summary>修改按 Id 定位、内容整份覆盖，Id 与条数都不变。</summary>
    [Fact]
    public void Update_OverwritesContentByKeepId()
    {
        var service = CreateService();
        var added = service.Add(MathExam());

        var edit = added.Clone();
        edit.Subject = "语文";
        edit.StartTime = new TimeOnly(14, 0);
        edit.EndTime = new TimeOnly(16, 30);
        edit.PaperCount = null;

        Assert.True(service.Update(edit));

        var stored = Assert.Single(service.Exams);
        Assert.Equal(added.Id, stored.Id);
        Assert.Equal("语文", stored.Subject);
        Assert.Equal(new TimeOnly(14, 0), stored.StartTime);
        Assert.Equal(new TimeOnly(16, 30), stored.EndTime);
        Assert.Null(stored.PaperCount);
        Assert.Equal(8, stored.PageCount);

        // 不认识的 Id 返回 false，不能顺手加一条。
        Assert.False(service.Update(new ExamEntry { Id = Guid.NewGuid(), Subject = "物理" }));
        Assert.Single(service.Exams);
    }

    /// <summary>删掉的考试不能再出现在集合里，也不能再被重读回来（删除要落盘）。</summary>
    [Fact]
    public void Remove_TakesTheExamOffTheList()
    {
        var service = CreateService();
        var math = service.Add(MathExam());
        var chinese = service.Add(new ExamEntry { Subject = "语文" });
        Assert.Equal(2, service.Exams.Count);

        Assert.True(service.Remove(math.Id));
        Assert.Equal(["语文"], service.Exams.Select(exam => exam.Subject));

        // 已经不在了：再删一次返回 false，不当成成功。
        Assert.False(service.Remove(math.Id));

        service.Reload();
        Assert.Equal(["语文"], service.Exams.Select(exam => exam.Subject));
        Assert.Equal(chinese.Id, Assert.Single(service.Exams).Id);
    }

    /// <summary>
    ///     <see cref="IExamService.Save" /> 与 <see cref="IExamService.Reload" /> 往返一致：
    ///     直接改集合里的元素（服务不订阅元素属性）后调 <c>Save</c> 也要落盘，
    ///     重读回来科目 / 日期 / 起止时刻 / 卷面信息 / 备注一项不差。
    /// </summary>
    [Fact]
    public void SaveAndReload_RoundTripsEveryField()
    {
        var service = CreateService();
        var added = service.Add(MathExam());
        added.Note = "第三考场 12 号";

        service.Save();

        service.Reload();
        var reloaded = Assert.Single(service.Exams);
        Assert.Equal(added.Id, reloaded.Id);
        Assert.Equal("数学", reloaded.Subject);
        Assert.Equal(new DateOnly(2026, 6, 7), reloaded.Date);
        Assert.Equal(new TimeOnly(9, 0), reloaded.StartTime);
        Assert.Equal(new TimeOnly(11, 0), reloaded.EndTime);
        Assert.Equal(2, reloaded.PaperCount);
        Assert.Equal(8, reloaded.PageCount);
        Assert.Equal(3, reloaded.QuestionCount);
        Assert.Equal("第三考场 12 号", reloaded.Note);

        // 换一个新服务实例读同一个文件，结果一样（说明真落盘了，不是内存里的旧值）。
        var other = CreateService();
        Assert.Equal(added.Id, Assert.Single(other.Exams).Id);
    }

    /// <summary>
    ///     第一次启动：没有索引、只有老版本单份的 <c>exams.json</c> 与全局的标题 / 副标题时，
    ///     这些要一起搬进第一块看板，搬完把全局那三个字段清空（否则下次读盘又把旧标题盖回来）。
    /// </summary>
    [Fact]
    public void FirstLoad_MigratesTheLegacyScheduleIntoTheFirstBoard()
    {
        WriteSettings(model =>
        {
            model.ExamBoard.Title = "期中考试";
            model.ExamBoard.Subtitle = "距离期末还有 20 天";
        });
        WriteLegacySchedule(MathExam(), new ExamEntry { Subject = "语文" });

        var handler = CreateHandler();
        var service = new ExamService(new SandboxConfigService(_sandbox), handler);

        var board = Assert.Single(service.Boards);
        Assert.Same(board, service.ActiveBoard);
        Assert.Equal("期中考试", board.ResolveTitle());
        Assert.Equal("距离期末还有 20 天", board.ResolveSubtitle());
        Assert.Equal(["数学", "语文"], service.Exams.Select(exam => exam.Subject));

        Assert.Equal(string.Empty, handler.Data.ExamBoard.Title);
        Assert.Equal(string.Empty, handler.Data.ExamBoard.Subtitle);

        // 搬进来的内容要真落盘：换个服务实例读同一份看板，内容一样。
        var reopened = new ExamService(new SandboxConfigService(_sandbox), CreateHandler());
        Assert.Equal(board.Id, reopened.ActiveBoard.Id);
        Assert.Equal("期中考试", reopened.ActiveBoard.ResolveTitle());
        Assert.Equal(2, reopened.Exams.Count);
    }

    /// <summary>
    ///     新建看板：加一块空的、切过去，原来那块连同它的考试原封不动；
    ///     切回去还能看到原来的考试（说明每块的考试是各自独立的）。
    /// </summary>
    [Fact]
    public void CreateBoard_AddsAnEmptyBoardAndSwitchesToIt()
    {
        var service = CreateService();
        var first = service.ActiveBoard;
        service.Add(MathExam());

        var second = service.CreateBoard("期末考试");

        Assert.Equal(2, service.Boards.Count);
        Assert.Same(second, service.ActiveBoard);
        Assert.Equal("期末考试", second.ResolveTitle());
        Assert.Empty(service.Exams); // 新看板里没有考试
        Assert.Single(first.Exams);  // 原来那块原封不动

        Assert.True(service.SelectBoard(first.Id));
        Assert.Equal(["数学"], service.Exams.Select(exam => exam.Subject));
    }

    /// <summary>切看板：右侧那列考试整批换掉，「当前是哪一块」也要落盘（重开回到上次那块）。</summary>
    [Fact]
    public void SelectBoard_SwitchesTheVisibleExamsAndPersistsTheChoice()
    {
        var service = CreateService();
        service.Add(new ExamEntry { Subject = "数学" });
        var firstId = service.ActiveBoard.Id;

        service.CreateBoard("期末考试");
        service.Add(new ExamEntry { Subject = "物理" });
        Assert.Equal(["物理"], service.Exams.Select(exam => exam.Subject));

        Assert.True(service.SelectBoard(firstId));
        Assert.Equal(["数学"], service.Exams.Select(exam => exam.Subject));

        var reopened = new ExamService(new SandboxConfigService(_sandbox), CreateHandler());
        Assert.Equal(firstId, reopened.ActiveBoard.Id);
        Assert.Equal(["数学"], reopened.Exams.Select(exam => exam.Subject));
    }

    /// <summary>删看板：文件一起删掉、当前退回第一块；**最后一块删不掉**（界面上永远要有可看的看板）。</summary>
    [Fact]
    public void DeleteBoard_RemovesItsFileAndKeepsTheLastOne()
    {
        var service = CreateService();
        var first = service.ActiveBoard;
        var second = service.CreateBoard("期末考试");
        Assert.True(File.Exists(BoardPath(second.Id)));

        Assert.True(service.DeleteBoard(second.Id));
        Assert.Single(service.Boards);
        Assert.Same(first, service.ActiveBoard);
        Assert.False(File.Exists(BoardPath(second.Id)));

        Assert.False(service.DeleteBoard(first.Id));
        Assert.Single(service.Boards);
    }

    /// <summary>标题与副标题跟着看板文件走，不是全局配置。</summary>
    [Fact]
    public void TitleAndSubtitle_TravelWithTheBoardFile()
    {
        var service = CreateService();
        service.ActiveBoard.Title = "期中考试";
        service.ActiveBoard.Subtitle = "还有 20 天";
        service.Save();

        var reopened = new ExamService(new SandboxConfigService(_sandbox), CreateHandler());
        Assert.Equal("期中考试", reopened.ActiveBoard.ResolveTitle());
        Assert.Equal("还有 20 天", reopened.ActiveBoard.ResolveSubtitle());
    }

    /// <summary>
    ///     读盘时丢掉<b>空科目</b>的条目（看板上没法显示，多半是手改 JSON 或旧数据留下的），
    ///     顺手把有科目的那条收拾干净：去首尾空白、卷面数字夹取、空 Id 补上。
    /// </summary>
    [Fact]
    public void Load_DropsEntriesWithoutSubjectAndNormalizesTheRest()
    {
        WriteLegacySchedule(
            new ExamEntry { Subject = "  数学  ", PaperCount = 0, PageCount = 5000, Note = "  " },
            new ExamEntry { Subject = string.Empty },
            new ExamEntry { Subject = "   " },
            new ExamEntry { Subject = "语文", Id = Guid.Empty });

        var service = CreateService();

        Assert.Equal(["数学", "语文"], service.Exams.Select(exam => exam.Subject));

        var math = service.Exams[0];
        Assert.Null(math.PaperCount);
        Assert.Equal(999, math.PageCount);
        Assert.Null(math.Note);

        // 空 Id 的那条在读盘时补一个，后面按 Id 编辑 / 删除才认得出来。
        Assert.All(service.Exams, exam => Assert.NotEqual(Guid.Empty, exam.Id));
        Assert.Equal(2, service.Exams.Select(exam => exam.Id).Distinct().Count());
    }

    /// <summary>
    ///     索引里有、文件却没了的看板直接丢掉（用户手工删过文件），当前那块没了就退回第一块；
    ///     看板文件里的 <c>id</c> 与文件名不一致时**以索引为准**（否则会算出一条指向别处的看板）。
    /// </summary>
    [Fact]
    public void Load_DropsBoardsWhoseFileIsMissingAndTrustsTheIndexId()
    {
        var kept = Guid.NewGuid();
        var lost = Guid.NewGuid();

        // 文件写在 kept 那个名字上，但内容里的 id 是另一个——读盘要以索引为准。
        var raw = new ExamBoardDocument { Id = Guid.NewGuid(), Title = "期中" };
        Directory.CreateDirectory(_sandbox);
        File.WriteAllText(BoardPath(kept), JsonSerializer.Serialize(raw, ConfigServiceBase.JsonOptions));
        WriteIndex([kept, lost], lost);

        var service = CreateService();

        var board = Assert.Single(service.Boards);
        Assert.Equal(kept, board.Id);
        Assert.Same(board, service.ActiveBoard);
        Assert.Equal("期中", board.ResolveTitle());
    }

    /// <summary>配置文件不存在时从一块空看板起步（第一次开应用就是这样），不抛异常。</summary>
    [Fact]
    public void Load_StartsEmptyWhenTheFileIsMissing()
    {
        Assert.False(File.Exists(IndexPath));

        var service = CreateService();

        Assert.Empty(service.Exams);
        Assert.Single(service.Boards);
    }

    public void Dispose()
    {
        if (Directory.Exists(_sandbox))
            Directory.Delete(_sandbox, recursive: true);
    }
}
