namespace Corkboard.Core.Exam;

/// <summary>
///     考试状态的判定规则（纯函数，有单测）。看板、设置页与测试都只认这一份，
///     不要在界面里另写一套「现在比开考早多少分钟」。
///     <para>
///         三档的边界是**左闭右开**：
///         <c>now &lt; start - 阈值</c> → 未开始；<c>start - 阈值 ≤ now &lt; start</c> → 即将开始；
///         <c>start ≤ now &lt; end</c> → 进行中；<c>now ≥ end</c> → 已结束。
///         所以「阈值 30 分钟」意味着开考前第 30 分钟整那一刻起就算即将开始。
///     </para>
/// </summary>
public static class ExamStatusPolicy
{
    /// <summary>「即将开始」默认窗口：开考前 30 分钟。</summary>
    public const int DefaultUpcomingMinutes = 30;

    /// <summary>阈值的下限：再小就没有意义（0 等于关掉这一档）。</summary>
    public const int MinUpcomingMinutes = 1;

    /// <summary>阈值的上限：一天。超过一天的「即将开始」不如直接看日期。</summary>
    public const int MaxUpcomingMinutes = 24 * 60;

    /// <summary>把阈值夹进合法区间（设置页滑杆、手改的 settings.json 都过这里）。</summary>
    public static int ClampUpcomingMinutes(int minutes) =>
        Math.Clamp(minutes, MinUpcomingMinutes, MaxUpcomingMinutes);

    /// <summary>判一场考试此刻处于哪一档。</summary>
    public static ExamStatus Resolve(ExamEntry exam, DateTime now, int upcomingMinutes)
    {
        ArgumentNullException.ThrowIfNull(exam);
        return Resolve(exam.StartAt, exam.EndAt, now, upcomingMinutes);
    }

    /// <summary>同上，直接给起止时刻（表单预览、单测用）。</summary>
    public static ExamStatus Resolve(DateTime start, DateTime end, DateTime now, int upcomingMinutes)
    {
        if (now >= end)
            return ExamStatus.Finished;

        if (now >= start)
            return ExamStatus.InProgress;

        var threshold = TimeSpan.FromMinutes(ClampUpcomingMinutes(upcomingMinutes));
        return start - now <= threshold ? ExamStatus.Upcoming : ExamStatus.NotStarted;
    }

    /// <summary>
    ///     下一场考试：**还没考完**的那些里开考最早的一场（进行中的那场也算「下一场」，
    ///     因为此刻该看的就是它）。没有返回 <see langword="null" />。
    /// </summary>
    public static ExamEntry? FindNext(IEnumerable<ExamEntry>? exams, DateTime now)
    {
        if (exams is null)
            return null;

        ExamEntry? next = null;
        foreach (var exam in exams)
        {
            if (exam is null || now >= exam.EndAt)
                continue;

            if (next is null || exam.StartAt < next.StartAt)
                next = exam;
        }

        return next;
    }

    /// <summary>
    ///     看板上要列出来的考试：没考完的、按开考时刻升序。已经考完的**不进列表**
    ///     （看板是给「接下来考什么」用的，历史成绩不在这里）。
    /// </summary>
    public static List<ExamEntry> ListUpcoming(IEnumerable<ExamEntry>? exams, DateTime now)
    {
        if (exams is null)
            return [];

        return exams
            .Where(exam => exam is not null && now < exam.EndAt)
            .OrderBy(exam => exam.StartAt)
            .ThenBy(exam => exam.Subject, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>离下一场开考还有多久（已经开考或没有下一场时返回 <see langword="null" />）。</summary>
    public static TimeSpan? TimeUntilStart(ExamEntry? exam, DateTime now) =>
        exam is null || now >= exam.StartAt ? null : exam.StartAt - now;

    /// <summary>离这场考完还有多久（没在考时返回 <see langword="null" />）。</summary>
    public static TimeSpan? TimeUntilEnd(ExamEntry? exam, DateTime now) =>
        exam is null || now < exam.StartAt || now >= exam.EndAt ? null : exam.EndAt - now;

    /// <summary>两场考试之间有没有重叠（设置页加考试时提示用，纯逻辑）。</summary>
    public static bool Overlaps(ExamEntry left, ExamEntry right) =>
        left.StartAt < right.EndAt && right.StartAt < left.EndAt;
}
