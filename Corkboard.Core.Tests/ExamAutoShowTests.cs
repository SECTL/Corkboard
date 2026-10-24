using Corkboard.Core.Exam;
using Xunit;

namespace Corkboard.Core.Tests;

/// <summary>
///     自动放映的判定：放映期、该不该弹、该不该收。
///     <para>
///     「现在」一律由参数传进来，用例里没有一处读 <see cref="DateTime.Now" />——
///     判定是纯函数，钉死时刻才守得住边界。
///     </para>
///     <para>
///     场景固定成一场两天的小考：
///     第 1 天 09:00 – 11:00（语文）、第 2 天 09:00 – 10:00（数学）。
///     </para>
/// </summary>
public class ExamAutoShowPolicyTests
{
    private static readonly DateOnly Day1 = new(2026, 6, 7);
    private static readonly DateOnly Day2 = new(2026, 6, 8);

    /// <summary>第 1 天开考时刻：2026-06-07 09:00。</summary>
    private static readonly DateTime FirstStart = new(2026, 6, 7, 9, 0, 0, DateTimeKind.Unspecified);

    /// <summary>第 2 天收卷时刻：2026-06-08 10:00。</summary>
    private static readonly DateTime LastEnd = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Unspecified);

    private static ExamEntry Make(string subject, DateOnly date, int startHour, int startMinute, int endHour,
        int endMinute) => new()
    {
        Subject = subject,
        Date = date,
        StartTime = new TimeOnly(startHour, startMinute),
        EndTime = new TimeOnly(endHour, endMinute)
    };

    private static List<ExamEntry> TwoDayExams() =>
    [
        Make("语文", Day1, 9, 0, 11, 0),
        Make("数学", Day2, 9, 0, 10, 0)
    ];

    /// <summary>相对第一场开考偏移若干分钟的那个时刻。</summary>
    private static DateTime At(int minutesFromFirstStart) => FirstStart.AddMinutes(minutesFromFirstStart);

    /// <summary>
    ///     放映期 = 第一场开考 − 提前量 ～ 最后一场收卷 + 延迟。
    ///     边界左闭右开：起点那一刻已经在放映期里，终点那一刻已经结束。
    /// </summary>
    [Fact]
    public void ResolveWindow_CoversFirstStartMinusLeadToLastEndPlusDelay()
    {
        var window = ExamAutoShowPolicy.ResolveWindow(TwoDayExams(), leadMinutes: 10, closeDelayMinutes: 15);

        Assert.NotNull(window);
        Assert.Equal(At(-10), window!.Value.Start);
        Assert.Equal(LastEnd.AddMinutes(15), window.Value.End);

        Assert.False(window.Value.Contains(At(-11))); // 差一分钟：还没进放映期
        Assert.True(window.Value.Contains(At(-10))); // 边界：提前量那一刻就已经在放
        Assert.True(window.Value.Contains(At(0))); // 开考那一刻
        Assert.True(window.Value.Contains(LastEnd.AddMinutes(14))); // 延迟的最后一分钟
        Assert.False(window.Value.Contains(LastEnd.AddMinutes(15))); // 边界：延迟到点就收
    }

    /// <summary>一场考试都没有：没有放映期，也永远不算「已经放完」。</summary>
    [Fact]
    public void ResolveWindow_EmptyScheduleHasNoWindow()
    {
        Assert.Null(ExamAutoShowPolicy.ResolveWindow([], 10, 10));
        Assert.Null(ExamAutoShowPolicy.ResolveWindow(null, 10, 10));

        // 一场都没有时「已完成」必须是 false：别让「看板被清空」变成「到点去关别人的窗口」。
        Assert.False(ExamAutoShowPolicy.IsFinished([], At(0), 10));
        Assert.False(ExamAutoShowPolicy.IsFinished(null, At(0), 10));
    }

    /// <summary>
    ///     提前量被夹在 1 ～ 120 分钟；考完延迟被夹在 0 ～ 120 分钟。
    ///     手改过的 settings.json 不会把放映期拉成「提前一天」或者「放一整天」。
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(120, 120)]
    [InlineData(0, 1)] // 0 等于「开考那刻才弹」，赶不上进场 → 夹到 1
    [InlineData(-30, 1)]
    [InlineData(121, 120)]
    [InlineData(int.MaxValue, 120)]
    public void ClampLeadMinutes_KeepsValueInRange(int input, int expected) =>
        Assert.Equal(expected, ExamAutoShowPolicy.ClampLeadMinutes(input));

    [Theory]
    [InlineData(0, 0)] // 0 是合法值：立刻收
    [InlineData(10, 10)]
    [InlineData(120, 120)]
    [InlineData(-1, 0)]
    [InlineData(121, 120)]
    [InlineData(int.MaxValue, 120)]
    public void ClampCloseDelayMinutes_KeepsValueInRange(int input, int expected) =>
        Assert.Equal(expected, ExamAutoShowPolicy.ClampCloseDelayMinutes(input));

    /// <summary>
    ///     触发条件：放映时刻已到、这场还没考完、（补弹关掉时）还没开考、且这一场没弹过。
    ///     这里逐条钉住那四处边界。
    /// </summary>
    [Theory]
    [InlineData(-11, false)] // 差一分钟才到提前量
    [InlineData(-10, true)] // 边界：提前量整点到，该弹
    [InlineData(-1, true)] // 开考前一分钟
    [InlineData(0, true)] // 开考那一刻：补弹开着 → 还算该弹
    [InlineData(119, true)] // 收卷前一分钟
    [InlineData(120, false)] // 收卷那一刻：这场已经过去了
    [InlineData(600, false)] // 早就考完了
    public void FindTrigger_HonoursLeadAndExamWindow(int minutesFromFirstStart, bool expected)
    {
        var exams = new List<ExamEntry> { Make("语文", Day1, 9, 0, 11, 0) };

        var trigger = ExamAutoShowPolicy.FindTrigger(
            exams, At(minutesFromFirstStart), leadMinutes: 10, alsoShowAfterStart: true);

        Assert.Equal(expected, trigger is not null);
        if (expected)
            Assert.Equal("语文", trigger!.Subject);
    }

    /// <summary>
    ///     补弹关掉时，开考之后就不再弹：软件在考试中途启动不该再抢一次屏幕。
    ///     开考那一刻（<c>now == StartAt</c>）已经算「开考后」，所以也不弹。
    /// </summary>
    [Theory]
    [InlineData(-10, true)] // 开考前：照弹
    [InlineData(-1, true)]
    [InlineData(0, false)] // 边界：开考那一刻 → 已经开考，不补弹
    [InlineData(30, false)] // 考试中途
    [InlineData(119, false)]
    public void FindTrigger_AlsoShowAfterStartFalseStopsAtStart(int minutesFromFirstStart, bool expected)
    {
        var exams = new List<ExamEntry> { Make("语文", Day1, 9, 0, 11, 0) };

        var trigger = ExamAutoShowPolicy.FindTrigger(
            exams, At(minutesFromFirstStart), leadMinutes: 10, alsoShowAfterStart: false);

        Assert.Equal(expected, trigger is not null);
    }

    /// <summary>同一场只弹一次：已经弹过的那场不再返回（用户长按退出后不该被弹回来）。</summary>
    [Fact]
    public void FindTrigger_SkipsExamsAlreadyShown()
    {
        var exams = new List<ExamEntry> { Make("语文", Day1, 9, 0, 11, 0) };
        var shown = new HashSet<Guid> { exams[0].Id };

        Assert.Null(ExamAutoShowPolicy.FindTrigger(
            exams, At(-5), leadMinutes: 10, alsoShowAfterStart: true, alreadyShown: shown));

        // 同一时刻、另一场没弹过的考试 → 仍然会触发。
        var other = Make("数学", Day1, 14, 0, 16, 0);
        exams.Add(other);

        // 13:55：数学 14:00 开考，提前量 10 分钟 → 13:50 起就该弹。
        var trigger = ExamAutoShowPolicy.FindTrigger(
            new List<ExamEntry> { other }, At(-5).AddHours(5), leadMinutes: 10, alsoShowAfterStart: true,
            alreadyShown: shown);

        Assert.NotNull(trigger);
        Assert.Equal("数学", trigger!.Subject);
    }

    /// <summary>两场同时满足放映条件时取开考最早的，别按列表里的录入顺序拍脑袋。</summary>
    [Fact]
    public void FindTrigger_PicksEarliestStart()
    {
        var late = Make("数学", Day1, 9, 30, 11, 0);
        var early = Make("语文", Day1, 9, 0, 11, 0);

        var trigger = ExamAutoShowPolicy.FindTrigger(
            new List<ExamEntry> { late, early }, At(-10), leadMinutes: 10, alsoShowAfterStart: true);

        Assert.NotNull(trigger);
        Assert.Equal("语文", trigger!.Subject);
    }

    /// <summary>「已经放完」只看最后一场收卷 + 延迟，与放映期的终点是同一个时刻。</summary>
    [Theory]
    [InlineData(-1, 10, false)] // 最后一场考完前一分钟
    [InlineData(0, 10, false)] // 收卷那一刻：延迟还没走完
    [InlineData(9, 10, false)] // 延迟的最后一分钟
    [InlineData(10, 10, true)] // 边界：延迟到点就算放完
    [InlineData(0, 0, true)] // 延迟 0：收卷即放完
    [InlineData(-1, 0, false)]
    public void IsFinished_HonoursCloseDelay(int minutesFromLastEnd, int closeDelayMinutes, bool expected)
    {
        var now = LastEnd.AddMinutes(minutesFromLastEnd);

        Assert.Equal(expected, ExamAutoShowPolicy.IsFinished(TwoDayExams(), now, closeDelayMinutes));
    }

    /// <summary>
    ///     状态卡的「下一次放映」：取还没考完里开考最早的一场，返回它该被弹出来的时刻
    ///     （这个时刻可能已经过去——那就是「此刻就该弹着」）。
    /// </summary>
    [Fact]
    public void FindNextTrigger_ReturnsEarliestPendingExamAndItsTriggerTime()
    {
        var exams = TwoDayExams();

        // 第 1 天开考前一小时：下一场是第一天的语文，放映时刻是 08:50。
        var beforeFirst = ExamAutoShowPolicy.FindNextTrigger(exams, At(-60), leadMinutes: 10);
        Assert.NotNull(beforeFirst);
        Assert.Equal("语文", beforeFirst!.Value.Exam.Subject);
        Assert.Equal(At(-10), beforeFirst.Value.TriggerAt);

        // 第 1 天那场已经开考：仍返回它（此刻就该弹着），放映时刻在过去。
        var inFirst = ExamAutoShowPolicy.FindNextTrigger(exams, At(30), leadMinutes: 10);
        Assert.NotNull(inFirst);
        Assert.Equal("语文", inFirst!.Value.Exam.Subject);

        // 第 1 天考完之后：换成第 2 天的数学。
        var afterFirst = ExamAutoShowPolicy.FindNextTrigger(exams, At(180), leadMinutes: 10);
        Assert.NotNull(afterFirst);
        Assert.Equal("数学", afterFirst!.Value.Exam.Subject);
        Assert.Equal(LastEnd.AddHours(-1).AddMinutes(-10), afterFirst.Value.TriggerAt);

        // 全部考完：没有下一场。
        Assert.Null(ExamAutoShowPolicy.FindNextTrigger(exams, LastEnd, leadMinutes: 10));
    }
}
