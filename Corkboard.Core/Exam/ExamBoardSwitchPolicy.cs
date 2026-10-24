namespace Corkboard.Core.Exam;

/// <summary>
///     「当前看板跟着考试自己换」的判定规则 —— 纯函数，有单测。
///     <para>
///         全屏看板显示的一直是 <see cref="IExamService.ActiveBoard" />（标题、副标题、考试列表都读它），
///         而这块板子本来只会在「设置里点设为当前 / 打开编辑窗口 / 自动放映弹窗」时变。
///         于是手动开看板的时候，屏幕上是上次手选的那块，跟今天考什么没关系。
///         这里回答的就是「此刻该是哪块板」。
///     </para>
///     <para>
///         **只看今天**：考试前一天不切、考完的当天也不切（今天还有没考完的才切）。
///         提前把明天的板子摆出来没有意义——用户要的是「现在考什么就看什么」。
///     </para>
///     <para>
///         边界与全项目同一套口径（左闭右开）：<c>now ≥ 开考</c> 就算在考，
///         <c>now ≥ 收卷</c> 就算考完。
///     </para>
/// </summary>
public static class ExamBoardSwitchPolicy
{
    /// <summary>
    ///     此刻该把哪块看板设成当前看板。
    ///     <para>
    ///         优先级：**正在考的那块** &gt; **今天接下来要考的那块** &gt; 谁都不切（返回 <see langword="null" />）。
    ///         多块同时满足时取开考最早的那场——与自动放映的取法一致，不会按列表顺序乱挑。
    ///     </para>
    ///     <para>
    ///         返回 <see langword="null" /> 表示「没有该切的目标」：调用方应当**保持现状**，
    ///         而不是切回某个默认看板——用户自己挑的那块不该在没考试的时候被换掉。
    ///     </para>
    /// </summary>
    public static ExamBoardDocument? FindTarget(IEnumerable<ExamBoardDocument>? boards, DateTime now)
    {
        if (boards is null)
            return null;

        var today = DateOnly.FromDateTime(now);

        ExamBoardDocument? inProgressBoard = null;
        var inProgressStart = default(DateTime);

        ExamBoardDocument? upcomingBoard = null;
        var upcomingStart = default(DateTime);

        foreach (var board in boards)
        {
            if (board?.Exams is null)
                continue;

            foreach (var exam in board.Exams)
            {
                if (exam is null)
                    continue;

                var start = exam.StartAt;
                var end = exam.EndAt;

                if (now >= end)
                    continue; // 这一场已经考完了

                if (now >= start)
                {
                    // 正在考：最高优先级，多块同时有考试时取开考最早的。
                    if (inProgressBoard is null || start < inProgressStart)
                    {
                        inProgressBoard = board;
                        inProgressStart = start;
                    }

                    continue;
                }

                // 还没开考：只有**今天**的才算数（明天的板子今天不摆出来）。
                if (exam.Date != today)
                    continue;

                if (upcomingBoard is null || start < upcomingStart)
                {
                    upcomingBoard = board;
                    upcomingStart = start;
                }
            }
        }

        return inProgressBoard ?? upcomingBoard;
    }

    /// <summary>
    ///     下一场考试什么时候开考（只看 <paramref name="now" /> 之后的，所有看板一起算）。
    ///     <para>
    ///         用户手动挑了一块看板之后，自动切换要「让位到下一场考试开始」——
    ///         这个时刻就是让位的截止点。一场都没有返回 <see langword="null" />（那就一直让位下去）。
    ///     </para>
    /// </summary>
    public static DateTime? FindNextStart(IEnumerable<ExamBoardDocument>? boards, DateTime now)
    {
        if (boards is null)
            return null;

        DateTime? next = null;

        foreach (var board in boards)
        {
            if (board?.Exams is null)
                continue;

            foreach (var exam in board.Exams)
            {
                if (exam is null)
                    continue;

                var start = exam.StartAt;
                if (start <= now)
                    continue; // 已经开考的不算「下一场」

                if (next is null || start < next)
                    next = start;
            }
        }

        return next;
    }
}
