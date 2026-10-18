using System.Globalization;
using Corkboard.Core.Langs.Common;

namespace Corkboard.Core.Exam;

/// <summary>
///     考试相关的文案拼装：日期（今天 / 明天 / 10月20日 周一）、时间区间、倒计时、卷面信息。
///     <para>
///         全部集中在这里，是因为同一段文字在**全屏看板**与**设置页列表**上都要出现，
///         两边各拼一套迟早会不一致（一处显示「今天」、另一处显示「10月20日」）。
///     </para>
/// </summary>
public static class ExamTimeFormatter
{
    /// <summary>日期那一列：今天 / 明天 / 后天 用相对说法，再远就写月日（跨年时带上年份）。</summary>
    public static string FormatDate(DateOnly date, DateOnly today)
    {
        var offset = date.DayNumber - today.DayNumber;
        return offset switch
        {
            0 => Resources.Exam_Date_Today,
            1 => Resources.Exam_Date_Tomorrow,
            2 => Resources.Exam_Date_DayAfterTomorrow,
            _ => date.Year == today.Year
                ? string.Format(CultureInfo.CurrentCulture, Resources.Exam_Date_Format, date.Month, date.Day,
                    WeekdayName(date))
                : string.Format(CultureInfo.CurrentCulture, Resources.Exam_Date_FormatWithYear, date.Year, date.Month,
                    date.Day, WeekdayName(date))
        };
    }

    /// <summary>星期几的短名（跟着当前界面语言：中文「周一」、英文「Mon」）。</summary>
    public static string WeekdayName(DateOnly date) =>
        CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(date.DayOfWeek);

    /// <summary>时间区间：「08:00 – 10:00」。用短横线而不是「至」，表格里更紧凑。</summary>
    public static string FormatTimeRange(ExamEntry exam)
    {
        ArgumentNullException.ThrowIfNull(exam);
        return $"{FormatTime(exam.StartTime)} – {FormatTime(exam.EndTime)}";
    }

    /// <summary>单个时刻：「08:00」（24 小时制，跟界面语言无关）。</summary>
    public static string FormatTime(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>把时长说成「2 小时 15 分」/「12 分钟」/「不到 1 分钟」。</summary>
    public static string FormatDuration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        var minutes = (int)Math.Ceiling(span.TotalMinutes);
        if (minutes <= 0)
            return Resources.Exam_Duration_LessThanMinute;

        if (minutes < 60)
            return string.Format(CultureInfo.CurrentCulture, Resources.Exam_Duration_Minutes, minutes);

        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0
            ? string.Format(CultureInfo.CurrentCulture, Resources.Exam_Duration_Hours, hours)
            : string.Format(CultureInfo.CurrentCulture, Resources.Exam_Duration_HoursMinutes, hours, rest);
    }

    /// <summary>
    ///     距离开考还有多久（按天 / 小时 / 分钟给一个粗略但好读的说法）：
    ///     「还有 3 天」「还有 5 小时 20 分」「还有 12 分钟」。
    /// </summary>
    public static string FormatCountdown(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        if (span.TotalDays >= 1)
            return string.Format(CultureInfo.CurrentCulture, Resources.Exam_Countdown_Days,
                (int)Math.Ceiling(span.TotalDays));

        return string.Format(CultureInfo.CurrentCulture, Resources.Exam_Countdown_Remaining, FormatDuration(span));
    }

    /// <summary>状态短名：未开始 / 即将开始 / 进行中 / 已结束。</summary>
    public static string FormatStatus(ExamStatus status) => status switch
    {
        ExamStatus.Upcoming => Resources.Exam_Status_Upcoming,
        ExamStatus.InProgress => Resources.Exam_Status_InProgress,
        ExamStatus.Finished => Resources.Exam_Status_Finished,
        _ => Resources.Exam_Status_NotStarted
    };

    /// <summary>
    ///     状态那一列的完整说法：除了状态名，再补一句此刻最关心的信息
    ///     （「即将开始 · 还有 12 分钟」「进行中 · 还剩 40 分钟」）。
    /// </summary>
    public static string FormatStatusDetail(ExamStatus status, ExamEntry exam, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(exam);

        return status switch
        {
            ExamStatus.Upcoming when ExamStatusPolicy.TimeUntilStart(exam, now) is { } until =>
                $"{Resources.Exam_Status_Upcoming} · {FormatCountdown(until)}",
            ExamStatus.InProgress when ExamStatusPolicy.TimeUntilEnd(exam, now) is { } left =>
                $"{Resources.Exam_Status_InProgress} · {string.Format(CultureInfo.CurrentCulture,
                    Resources.Exam_Status_Remaining, FormatDuration(left))}",
            _ => FormatStatus(status)
        };
    }

    /// <summary>卷面信息那一行：「2 张 · 8 页 · 3 大题」；一项都没填时返回空串（调用方据此隐藏）。</summary>
    public static string FormatPaperInfo(ExamEntry exam)
    {
        ArgumentNullException.ThrowIfNull(exam);

        var parts = new List<string>(3);
        if (exam.PaperCount is { } papers)
            parts.Add(string.Format(CultureInfo.CurrentCulture, Resources.Exam_PaperCount, papers));
        if (exam.PageCount is { } pages)
            parts.Add(string.Format(CultureInfo.CurrentCulture, Resources.Exam_PageCount, pages));
        if (exam.QuestionCount is { } questions)
            parts.Add(string.Format(CultureInfo.CurrentCulture, Resources.Exam_QuestionCount, questions));

        return string.Join(Resources.Exam_PaperInfoSeparator, parts);
    }
}
