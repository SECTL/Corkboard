using Corkboard.Core.Exam;
using Xunit;

namespace Corkboard.Core.Tests;

/// <summary>
///     「当前看板跟着考试自己换」的判定。
///     <para>
///     与 <see cref="ExamAutoShowPolicyTests" /> 同一套写法：「现在」一律由参数传进来，
///     用例里没有一处读 <see cref="DateTime.Now" />。
///     </para>
/// </summary>
public class ExamBoardSwitchPolicyTests
{
    private static readonly DateOnly Today = new(2026, 6, 7);
    private static readonly DateOnly Tomorrow = new(2026, 6, 8);

    /// <summary>今天上午的那场（09:00 – 11:00），大多数用例围着它转。</summary>
    private static readonly DateTime MorningStart = new(2026, 6, 7, 9, 0, 0, DateTimeKind.Unspecified);

    private static DateTime At(int hour, int minute = 0) =>
        new(2026, 6, 7, hour, minute, 0, DateTimeKind.Unspecified);

    private static ExamEntry Make(string subject, DateOnly date, int startHour, int endHour) => new()
    {
        Subject = subject,
        Date = date,
        StartTime = new TimeOnly(startHour, 0),
        EndTime = new TimeOnly(endHour, 0)
    };

    private static ExamBoardDocument Board(string title, params ExamEntry[] exams) => new()
    {
        Title = title,
        Exams = [.. exams]
    };

    /// <summary>正在考的那块优先：今天后面还有别的板子要考，也不该先摆出来。</summary>
    [Fact]
    public void FindTarget_PrefersBoardInProgress()
    {
        var inProgress = Board("期中", Make("语文", Today, 9, 11));
        var upcoming = Board("期末", Make("数学", Today, 14, 16));

        var target = ExamBoardSwitchPolicy.FindTarget([upcoming, inProgress], At(9, 30));

        Assert.Same(inProgress, target);
    }

    /// <summary>没有在考的，就切到今天接下来要考的那块。</summary>
    [Fact]
    public void FindTarget_FallsBackToTodaysNextExam()
    {
        var board = Board("期中", Make("语文", Today, 9, 11));

        var target = ExamBoardSwitchPolicy.FindTarget([board], At(7, 30));

        Assert.Same(board, target);
    }

    /// <summary>明天的考试今天不摆出来：提前一天把板子换掉没有意义。</summary>
    [Fact]
    public void FindTarget_IgnoresExamsOnOtherDays()
    {
        var board = Board("期末", Make("数学", Tomorrow, 9, 11));

        Assert.Null(ExamBoardSwitchPolicy.FindTarget([board], At(20)));
    }

    /// <summary>今天考完了就保持现状（返回 null），不切回什么默认看板。</summary>
    [Fact]
    public void FindTarget_IgnoresExamsAlreadyFinished()
    {
        var board = Board("期中", Make("语文", Today, 9, 11));

        Assert.Null(ExamBoardSwitchPolicy.FindTarget([board], At(11)));
        Assert.Null(ExamBoardSwitchPolicy.FindTarget([board], At(15)));
    }

    /// <summary>开考那一刻就算「正在考」（左闭）；收卷那一刻就算考完（右开）。</summary>
    [Fact]
    public void FindTarget_UsesHalfOpenBounds()
    {
        var board = Board("期中", Make("语文", Today, 9, 11));

        Assert.Same(board, ExamBoardSwitchPolicy.FindTarget([board], MorningStart));
        Assert.Same(board, ExamBoardSwitchPolicy.FindTarget([board], MorningStart.AddMinutes(-1)));
        Assert.Null(ExamBoardSwitchPolicy.FindTarget([board], At(11)));
    }

    /// <summary>两块板子同时在考：取开考最早的那块，不按列表顺序。</summary>
    [Fact]
    public void FindTarget_PicksEarliestStartAmongBoardsInProgress()
    {
        var later = Board("期末", Make("数学", Today, 10, 12));
        var earlier = Board("期中", Make("语文", Today, 9, 11));

        var target = ExamBoardSwitchPolicy.FindTarget([later, earlier], At(10, 30));

        Assert.Same(earlier, target);
    }

    /// <summary>两块板子今天都要考、都还没开考：同样取开考最早的。</summary>
    [Fact]
    public void FindTarget_PicksEarliestStartAmongTodaysUpcoming()
    {
        var later = Board("期末", Make("数学", Today, 14, 16));
        var earlier = Board("期中", Make("语文", Today, 9, 11));

        var target = ExamBoardSwitchPolicy.FindTarget([later, earlier], At(7));

        Assert.Same(earlier, target);
    }

    /// <summary>一块看板都没有 / 都没有考试：不切。</summary>
    [Fact]
    public void FindTarget_ReturnsNullWithoutBoardsOrExams()
    {
        Assert.Null(ExamBoardSwitchPolicy.FindTarget(null, At(9)));
        Assert.Null(ExamBoardSwitchPolicy.FindTarget([], At(9)));
        Assert.Null(ExamBoardSwitchPolicy.FindTarget([Board("空板")], At(9)));
    }

    /// <summary>让位截止点 = 所有看板里下一场开考的时刻（不分今天明天）。</summary>
    [Fact]
    public void FindNextStart_ReturnsEarliestFutureStartAcrossBoards()
    {
        var tomorrow = Board("期末", Make("数学", Tomorrow, 9, 11));
        var thisAfternoon = Board("期中", Make("语文", Today, 14, 16));

        Assert.Equal(At(14), ExamBoardSwitchPolicy.FindNextStart([tomorrow, thisAfternoon], At(9)));
    }

    /// <summary>已经开考的不算「下一场」；一场都没排就返回 null（调用方据此一直让位）。</summary>
    [Fact]
    public void FindNextStart_IgnoresStartedExamsAndEmptySchedules()
    {
        var board = Board("期中", Make("语文", Today, 9, 11));

        Assert.Null(ExamBoardSwitchPolicy.FindNextStart([board], At(9)));
        Assert.Null(ExamBoardSwitchPolicy.FindNextStart([board], At(12)));
        Assert.Null(ExamBoardSwitchPolicy.FindNextStart(null, At(9)));
        Assert.Null(ExamBoardSwitchPolicy.FindNextStart([], At(9)));
    }
}
