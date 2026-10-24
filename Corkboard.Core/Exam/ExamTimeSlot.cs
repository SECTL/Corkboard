using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Core.Exam;

/// <summary>
///     一天里的时段：上午 / 下午 / 晚上。
///     <para>
///         ⚠️ <b>不是落盘字段</b>：考试数据里只有日期与起止时刻，时段由开考时刻推出来
///         （<see cref="ExamTimeSlotPolicy.Resolve" />）。这样用户改一下开考时间，
///         分组自动跟着变，不会出现「数据写着上午、实际 15:00 开考」这种自相矛盾的记录。
///     </para>
/// </summary>
public enum ExamTimeSlot
{
    /// <summary>上午（12:00 之前开考）。</summary>
    Morning = 0,

    /// <summary>下午（12:00–18:00 开考）。</summary>
    Afternoon = 1,

    /// <summary>晚上（18:00 及之后开考）。</summary>
    Evening = 2
}

/// <summary>
///     时段判定与文案。分界线与「默认开考时刻」都放这里，向导与看板分组共用同一套，
///     免得一处按 12 点切、另一处按 13 点切。
/// </summary>
public static class ExamTimeSlotPolicy
{
    /// <summary>12:00 及之后算下午。</summary>
    public const int AfternoonStartHour = 12;

    /// <summary>18:00 及之后算晚上。</summary>
    public const int EveningStartHour = 18;

    /// <summary>按开考时刻推时段。</summary>
    public static ExamTimeSlot Resolve(TimeOnly start) =>
        start.Hour < AfternoonStartHour ? ExamTimeSlot.Morning
        : start.Hour < EveningStartHour ? ExamTimeSlot.Afternoon
        : ExamTimeSlot.Evening;

    /// <summary>时段名（上午 / 下午 / 晚上）。</summary>
    public static string Describe(ExamTimeSlot slot) => slot switch
    {
        ExamTimeSlot.Afternoon => CR.Exam_Slot_Afternoon,
        ExamTimeSlot.Evening => CR.Exam_Slot_Evening,
        _ => CR.Exam_Slot_Morning
    };

    /// <summary>这个时段的「习惯开考时刻」：上午 8:00、下午 14:00、晚上 19:00。</summary>
    public static TimeOnly DefaultStart(ExamTimeSlot slot) => slot switch
    {
        ExamTimeSlot.Afternoon => new TimeOnly(14, 0),
        ExamTimeSlot.Evening => new TimeOnly(19, 0),
        _ => new TimeOnly(8, 0)
    };
}
