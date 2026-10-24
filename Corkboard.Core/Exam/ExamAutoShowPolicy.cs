namespace Corkboard.Core.Exam;

/// <summary>
///     自动放映（到点自动全屏弹考试看板）的判定规则 —— 纯函数，有单测。
///     <para>
///         三件事都在这一个地方：这块看板的**放映期**多长、此刻该不该弹、这一轮是不是该收了。
///         后台看门狗与设置页的状态文案都只认这一份，不要在别处另写一套「现在离考试还有几分钟」。
///     </para>
///     <para>
///         边界一律**左闭右开**，与 <see cref="ExamStatusPolicy" /> 同一套口径：
///         <c>now ≥ 第一场开考 − 提前量</c> 就算进放映期，<c>now ≥ 最后一场收卷 + 延迟</c> 就算放映期结束。
///     </para>
///     <para>
///         放映范围由考试本身决定：**只有看板上真有考试的那几天才放**。提前量最大两小时，
///         所以永远不会出现「今天打开开关、明天就开始放」——没有考试就没有放映期。
///     </para>
/// </summary>
public static class ExamAutoShowPolicy
{
    /// <summary>开考前多少分钟开始放映（默认 10 分钟）。</summary>
    public const int DefaultLeadMinutes = 10;

    /// <summary>提前量的下限：0 等于「开考那一刻才弹」，赶不上进场，没有意义。</summary>
    public const int MinLeadMinutes = 1;

    /// <summary>提前量的上限：两小时。再早就不叫「快到考试了」，也会把学生按在屏幕前干等。</summary>
    public const int MaxLeadMinutes = 2 * 60;

    /// <summary>最后一场考完后过多少分钟收起看板（默认 10 分钟，0 = 收卷即收）。</summary>
    public const int DefaultCloseDelayMinutes = 10;

    /// <summary>延迟的下限：0 表示「收卷就收」，用户明确要的「不一定立刻关」就靠往上加。</summary>
    public const int MinCloseDelayMinutes = 0;

    /// <summary>延迟的上限：两小时。</summary>
    public const int MaxCloseDelayMinutes = 2 * 60;

    /// <summary>把提前量夹进合法区间（设置页输入框、手改的 settings.json 都过这里）。</summary>
    public static int ClampLeadMinutes(int minutes) =>
        Math.Clamp(minutes, MinLeadMinutes, MaxLeadMinutes);

    /// <summary>把「考完延迟」夹进合法区间。</summary>
    public static int ClampCloseDelayMinutes(int minutes) =>
        Math.Clamp(minutes, MinCloseDelayMinutes, MaxCloseDelayMinutes);

    /// <summary>
    ///     这块看板的放映期：第一场开考往前推提前量，到最后一场收卷再往后推延迟。
    ///     一场考试都没有返回 <see langword="null" />（没有考试就没有放映期）。
    /// </summary>
    public static ExamAutoShowWindow? ResolveWindow(
        IEnumerable<ExamEntry>? exams, int leadMinutes, int closeDelayMinutes)
    {
        if (ResolveBounds(exams) is not { } bounds)
            return null;

        return new ExamAutoShowWindow(
            bounds.FirstStart.AddMinutes(-ClampLeadMinutes(leadMinutes)),
            bounds.LastEnd.AddMinutes(ClampCloseDelayMinutes(closeDelayMinutes)));
    }

    /// <summary>
    ///     这一轮是不是已经放完了（最后一场收卷 + 延迟都过去了）。
    ///     一场考试都没有时返回 <see langword="false" />：没有考试就没什么好收的，
    ///     别让「看板被清空」变成「到点自动关别人的窗口」。
    /// </summary>
    public static bool IsFinished(IEnumerable<ExamEntry>? exams, DateTime now, int closeDelayMinutes)
    {
        if (ResolveBounds(exams) is not { } bounds)
            return false;

        return now >= bounds.LastEnd.AddMinutes(ClampCloseDelayMinutes(closeDelayMinutes));
    }

    /// <summary>
    ///     此刻该为哪一场弹看板：放映时刻已到、这场还没考完、
    ///     （<paramref name="alsoShowAfterStart" /> 为假时）还没开考，且这一场**没弹过**。
    ///     多场同时满足时取开考最早的那场；一场都不满足返回 <see langword="null" />。
    /// </summary>
    /// <param name="alreadyShown">
    ///     这一轮已经为哪些考试弹过。同一场只弹一次——用户长按退出之后不该再被弹回来。
    /// </param>
    public static ExamEntry? FindTrigger(
        IEnumerable<ExamEntry>? exams,
        DateTime now,
        int leadMinutes,
        bool alsoShowAfterStart,
        IReadOnlyCollection<Guid>? alreadyShown = null)
    {
        if (exams is null)
            return null;

        var lead = TimeSpan.FromMinutes(ClampLeadMinutes(leadMinutes));
        ExamEntry? candidate = null;

        foreach (var exam in exams)
        {
            if (exam is null || alreadyShown?.Contains(exam.Id) == true)
                continue;

            if (now >= exam.EndAt)
                continue; // 考完了

            if (now < exam.StartAt - lead)
                continue; // 还没到放映时刻

            if (!alsoShowAfterStart && now >= exam.StartAt)
                continue; // 开考之后不补弹

            if (candidate is null || exam.StartAt < candidate.StartAt)
                candidate = exam;
        }

        return candidate;
    }

    /// <summary>
    ///     这块看板**接下来**该弹的那一场（设置页状态卡用）：没考完的里开考最早的一场，
    ///     外加它该被弹出来的时刻。返回的时刻可能已经过去——那就是「此刻就该弹着」。
    ///     <para>
    ///         与 <see cref="FindTrigger" /> 的区别只是**不检查放映时刻到没到**，
    ///         也不看 <c>alreadyShown</c>：状态卡回答的是「按现在的设置，下一次放映是什么时候」。
    ///     </para>
    /// </summary>
    public static (ExamEntry Exam, DateTime TriggerAt)? FindNextTrigger(
        IEnumerable<ExamEntry>? exams, DateTime now, int leadMinutes)
    {
        if (exams is null)
            return null;

        var lead = TimeSpan.FromMinutes(ClampLeadMinutes(leadMinutes));
        ExamEntry? next = null;

        foreach (var exam in exams)
        {
            if (exam is null || now >= exam.EndAt)
                continue;

            if (next is null || exam.StartAt < next.StartAt)
                next = exam;
        }

        return next is null ? null : (next, next.StartAt - lead);
    }

    /// <summary>这块看板最早的「开考 / 收卷」两个时刻；一场考试都没有返回 <see langword="null" />。</summary>
    private static (DateTime FirstStart, DateTime LastEnd)? ResolveBounds(IEnumerable<ExamEntry>? exams)
    {
        if (exams is null)
            return null;

        DateTime? firstStart = null;
        DateTime? lastEnd = null;

        foreach (var exam in exams)
        {
            if (exam is null)
                continue;

            var start = exam.StartAt;
            var end = exam.EndAt;

            if (firstStart is null || start < firstStart)
                firstStart = start;

            if (lastEnd is null || end > lastEnd)
                lastEnd = end;
        }

        return firstStart is { } first && lastEnd is { } last ? (first, last) : null;
    }
}

/// <summary>一块看板的自动放映期：从这一刻开始放，到这一刻收（左闭右开）。</summary>
/// <param name="Start">第一场开考 − 提前量。</param>
/// <param name="End">最后一场收卷 + 考完延迟。</param>
public readonly record struct ExamAutoShowWindow(DateTime Start, DateTime End)
{
    /// <summary>某个时刻是不是落在放映期里（左闭右开，与全项目的时间判定口径一致）。</summary>
    public bool Contains(DateTime now) => now >= Start && now < End;

    /// <summary>放映期有多长（提前量 + 考试跨度 + 延迟）。</summary>
    public TimeSpan Duration => End - Start;
}
