using System.Globalization;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Core.Services.Board;

/// <summary>
///     把截止日期说成人话，给作业条上那一行用。
///     <para>
///         就近的两天说「今天 / 明天」而不是日期：这两档是用户真正会紧张的时候。
///         再远就直接报年月日，用不变文化格式化——界面文案已经本地化了，日期本身不该跟着区域变。
///     </para>
///     <para>
///         纯函数，时间从参数进来（同 <see cref="BoardExpiryPolicy" />），
///         不读系统时钟，单测才不用挑时间点跑。
///     </para>
/// </summary>
public static class BoardDueDateFormatter
{
    /// <summary>离 <paramref name="today" /> 还有几天；负数表示已经过了几天。</summary>
    public static int DaysLeft(DateOnly due, DateOnly today) => due.DayNumber - today.DayNumber;

    /// <summary>这条作业的截止日期是不是已经过去了。</summary>
    public static bool IsOverdue(DateOnly due, DateOnly today) => due < today;

    /// <summary>截止日期的显示文案。</summary>
    public static string Describe(DateOnly due, DateOnly today)
    {
        var days = DaysLeft(due, today);

        return days switch
        {
            0 => CR.Board_DueDateToday,
            1 => CR.Board_DueDateTomorrow,
            < 0 => string.Format(CR.Board_DueDateOverdueFormat, -days),
            _ => string.Format(CR.Board_DueDateFormat, due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        };
    }
}
