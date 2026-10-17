using System.Globalization;
using CR = Corkboard.Core.Langs.Common.Resources;
using Corkboard.Core.Notes;

namespace Corkboard.Core.Homework;

/// <summary>
///     把截止日期说成人话，给作业条上那一行用。
///     <para>
///         就近的两天说「今天 / 明天」而不是日期：这两档是用户真正会紧张的时候。
///         再远就直接报年月日，用不变文化格式化——界面文案已经本地化了，日期本身不该跟着区域变。
///     </para>
///     <para>
///         纯函数，时间从参数进来（同 <see cref="HomeworkExpiryPolicy" />），
///         不读系统时钟，单测才不用挑时间点跑。
///     </para>
/// </summary>
public static class HomeworkDueDateFormatter
{
    /// <summary>离 <paramref name="today" /> 还有几天；负数表示已经过了几天。</summary>
    public static int DaysLeft(DateOnly due, DateOnly today) => due.DayNumber - today.DayNumber;

    /// <summary>
    ///     这条作业的截止日期是不是已经过去了——界面措辞的判据：当天还说「今天截止」，
    ///     过了当天才说「已过期 N 天」。
    ///     <para>
    ///         ⚠️ 自动清理走的是 <see cref="HomeworkExpiryPolicy" />，那个<b>当天就算过期</b>（早一天动手）：
    ///         清理看的是「这一天到没到」，界面看的是「期限过没过」。两条判据故意不同，别把这边改成 <c>&lt;=</c>。
    ///     </para>
    /// </summary>
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
