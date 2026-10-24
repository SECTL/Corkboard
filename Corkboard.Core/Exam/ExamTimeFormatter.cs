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
    ///     <para>
    ///         全屏看板的表格里这两句是**分两行**显示的（状态名一行、这句一行），
    ///         否则一行就要 280 多 DIP，把整张表挤得没地方（用户反馈过「表格有点拥挤」）。
    ///         拆开的那一半见 <see cref="FormatStatusHint" />。
    ///     </para>
    /// </summary>
    public static string FormatStatusDetail(ExamStatus status, ExamEntry exam, DateTime now)
    {
        var hint = FormatStatusHint(status, exam, now);
        return string.IsNullOrEmpty(hint) ? FormatStatus(status) : $"{FormatStatus(status)} · {hint}";
    }

    /// <summary>
    ///     状态名底下那句「还有 12 分钟」/「还剩 40 分钟」：未开始与已结束没有这句，返回空串
    ///     （调用方据此隐藏那一行，跟卷面信息一样）。
    /// </summary>
    public static string FormatStatusHint(ExamStatus status, ExamEntry exam, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(exam);

        return status switch
        {
            ExamStatus.Upcoming when ExamStatusPolicy.TimeUntilStart(exam, now) is { } until =>
                FormatCountdown(until),
            ExamStatus.InProgress when ExamStatusPolicy.TimeUntilEnd(exam, now) is { } left =>
                string.Format(CultureInfo.CurrentCulture, Resources.Exam_Status_Remaining, FormatDuration(left)),
            _ => string.Empty
        };
    }

    /// <summary>
    ///     看板左侧那组「考试信息」的抬头：进行中 / 即将开始照旧用状态名，
    ///     离得还远（未开始）时说「下一场」——这时说「未开始」等于什么都没说。
    /// </summary>
    public static string FormatInfoCaption(ExamStatus status) =>
        status == ExamStatus.NotStarted ? Resources.Exam_Board_NextExam : FormatStatus(status);

    /// <summary>
    ///     考试信息里那一行倒计时：进行中「还剩 40 分钟」、即将开始「还有 12 分钟」，
    ///     离得远（未开始）时按天说「还有 13 天」——表格的状态列不写这一句（那一列要短），
    ///     所以它与 <see cref="FormatStatusHint" /> 是两套：这里永远给得出字。
    ///     <para>
    ///         精确度的规矩（<paramref name="withSeconds" /> 是「设置 → 考试看板 → 看板显示 →
    ///         剩余时间精确到秒」）：
    ///         一天以上按天说；一小时以上说「X 小时 Y 分」（小时更好读，谁也不想看「还剩 187 分钟」）；
    ///         最后一小时说分钟，开了秒再连秒一起跳。
    ///     </para>
    ///     <para>
    ///         ⚠️ 秒以下一律先丢掉（见 <see cref="TruncateToSecond" />）：时钟那行是「HH:mm:ss」，
    ///         本身就截到了秒，倒计时必须从同一个整秒起算，两边加起来才正好落在考试结束那一刻。
    ///         不截断就会差一秒——时钟读 23:39:44、倒计时说「还剩 16 分 15 秒」，44 + 15 = 59 而不是整分，
    ///         并排看着像数错了（用户说的「强迫症犯了」）。
    ///     </para>
    /// </summary>
    public static string FormatInfoRemaining(ExamStatus status, ExamEntry exam, DateTime now, bool withSeconds)
    {
        ArgumentNullException.ThrowIfNull(exam);

        now = TruncateToSecond(now);

        var remaining = status == ExamStatus.InProgress
            ? ExamStatusPolicy.TimeUntilEnd(exam, now)
            : ExamStatusPolicy.TimeUntilStart(exam, now);

        if (remaining is not { } span)
            return string.Empty;

        // 一天以上说「还有 N 天」：这段时间里秒、分、小时都在变，天天盯着看没意义。
        if (span.TotalDays >= 1)
            return FormatCountdown(span);

        var phrase = FormatRemainingPhrase(span, withSeconds);
        return status == ExamStatus.InProgress
            ? string.Format(CultureInfo.CurrentCulture, Resources.Exam_Status_Remaining, phrase)
            : string.Format(CultureInfo.CurrentCulture, Resources.Exam_Countdown_Remaining, phrase);
    }

    /// <summary>
    ///     去掉毫秒，只留到秒——和时钟那行「HH:mm:ss」同一个精度。
    ///     之后每一步取整都基于整秒，所以「时钟的秒 + 倒计时的秒」正好进位到整分；
    ///     取整方向也因此变成「向上」（真实还剩 16 分 15.4 秒时写 16 分 16 秒），
    ///     倒计时不会比真实时间更早说「没了」。
    /// </summary>
    private static DateTime TruncateToSecond(DateTime now) =>
        new(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, now.Kind);

    /// <summary>
    ///     不到一天那一段的说法：一小时以上「X 小时 Y 分」（零头为 0 时只写小时），
    ///     最后一小时写分钟；<paramref name="withSeconds" /> 为真时最后一小时带秒。
    /// </summary>
    private static string FormatRemainingPhrase(TimeSpan span, bool withSeconds)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        if (span.TotalHours >= 1)
        {
            var hours = (int)Math.Floor(span.TotalHours);
            var minutes = (int)Math.Floor(span.TotalMinutes) - hours * 60;
            return minutes == 0
                ? string.Format(CultureInfo.CurrentCulture, Resources.Exam_Duration_Hours, hours)
                : string.Format(CultureInfo.CurrentCulture, Resources.Exam_Duration_HoursMinutes, hours, minutes);
        }

        if (!withSeconds)
        {
            // 倒计时一律向下取整（与 <see cref="FormatDuration" /> 的「向上取整」相反）：
            // 剩 40 分 30 秒时说「还剩 40 分钟」是安全的，说「41 分钟」会让人以为还有时间。
            var wholeMinutes = (int)Math.Floor(span.TotalMinutes);
            return wholeMinutes <= 0
                ? Resources.Exam_Duration_LessThanMinute
                : string.Format(CultureInfo.CurrentCulture, Resources.Exam_Duration_Minutes, wholeMinutes);
        }

        var totalSeconds = (int)Math.Floor(span.TotalSeconds);
        if (totalSeconds <= 0)
            return Resources.Exam_Duration_LessThanMinute;

        var minutePart = totalSeconds / 60;
        var secondPart = totalSeconds % 60;
        return minutePart == 0
            ? string.Format(CultureInfo.CurrentCulture, Resources.Exam_Duration_Seconds, secondPart)
            : string.Format(CultureInfo.CurrentCulture, Resources.Exam_Duration_MinutesSeconds, minutePart, secondPart);
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
