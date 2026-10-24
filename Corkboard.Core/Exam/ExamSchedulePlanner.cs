using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Core.Exam;

/// <summary>
///     快速布置排出来的一场考试（还没落盘的中间结果）。
///     <para>
///         与 <see cref="ExamEntry" /> 的区别：这里存的是**时长**而不是收卷时刻——用户在对照表里
///         改开考时刻时，收卷时刻应当跟着顺延，存两个时刻就得两边同步，早晚会不一致。
///     </para>
///     <para>
///         冲突标记也挂在这里：<see cref="ConflictReason" /> 有值就说明这一场排得不合理。
///         ⚠️ <b>只标红、不拦着用户完成</b>——他可能就是故意的（比如上午和下午各有一场同名考试）。
///     </para>
/// </summary>
public sealed class ExamPlanEntry
{
    /// <summary>一天有多少分钟。收卷时刻越过它就说明这一场跨天了。</summary>
    public const int MinutesPerDay = 24 * 60;

    /// <summary>跨天时收卷时刻被夹到的那个时刻。</summary>
    public static readonly TimeOnly ClampedEnd = new(23, 59);

    private int _durationMinutes = ExamDurationPresets.FallbackMinutes;

    /// <summary>考试日期。</summary>
    public DateOnly Date { get; set; }

    /// <summary>开考时刻。</summary>
    public TimeOnly Start { get; set; }

    /// <summary>考试时长（分钟，已夹进 <see cref="ExamDurationPresets" /> 的区间）。</summary>
    public int DurationMinutes
    {
        get => _durationMinutes;
        set => _durationMinutes = ExamDurationPresets.Clamp(value);
    }

    /// <summary>显示名（合卷是「物理 · 化学」）。</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>绑定的作业板科目（合卷时多个，单科为空）。</summary>
    public List<string> SubjectKeys { get; set; } = [];

    /// <summary>这一天里的第几场（从 1 开始，只用于显示，由 <see cref="ExamSchedulePlanner.Refresh" /> 维护）。</summary>
    public int IndexInDay { get; set; }

    /// <summary>
    ///     与同一天上一场之间歇多少分钟（当天第 1 场没有上一场，值无意义）。
    ///     <para>
    ///         存它而不是只存开考时刻，是为了「上面那场拖堂了，下面这场跟着顺延」——
    ///     用户改的是前一场，后一场的时刻应当自己往后挪，别让两场挤在一起。
    ///     </para>
    /// </summary>
    public int BreakMinutes { get; set; } = ExamPlanSlot.DefaultBreakMinutes;

    /// <summary>冲突说明（没有冲突就是 <see langword="null" />）。</summary>
    public string? ConflictReason { get; set; }

    /// <summary>由开考时刻推出来的时段（不落盘）。</summary>
    public ExamTimeSlot TimeSlot => ExamTimeSlotPolicy.Resolve(Start);

    /// <summary>这一场排得不合理。</summary>
    public bool HasConflict => !string.IsNullOrEmpty(ConflictReason);

    /// <summary>开考时刻。</summary>
    public DateTime StartAt => Date.ToDateTime(Start);

    /// <summary>
    ///     由「开考时刻 + 时长」算收卷时刻，跨天夹成 23:59。
    ///     <para>
    ///         放在这里是给模板那条路用的（模板存的是时长、不存收卷时刻），
    ///     夹取规则与 <see cref="End" /> 必须一致，别在别处另写一套。
    ///     </para>
    /// </summary>
    public static TimeOnly ResolveEnd(TimeOnly start, int durationMinutes)
    {
        var minutes = (start.Hour * 60) + start.Minute + Math.Max(0, durationMinutes);
        return minutes >= MinutesPerDay ? ClampedEnd : TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutes));
    }

    /// <summary>收卷时刻（跨天时夹成 23:59）。</summary>
    public TimeOnly End => ResolveEnd(Start, DurationMinutes);

    /// <summary>收卷时刻（跨天时是次日 00:00 之后的那个真实时刻，只用于算间隔）。</summary>
    public DateTime EndAt => StartAt.AddMinutes(DurationMinutes);

    /// <summary>时长。</summary>
    public TimeSpan Duration => TimeSpan.FromMinutes(DurationMinutes);

    /// <summary>开考时刻折算成「当天第几分钟」。</summary>
    public int StartMinutes => Start.Hour * 60 + Start.Minute;

    /// <summary>这一场跨到了第二天。</summary>
    public bool CrossesMidnight => StartMinutes + DurationMinutes > MinutesPerDay;

    /// <summary>转成要落盘的那条考试。</summary>
    public ExamEntry ToEntry() => new()
    {
        Id = Guid.NewGuid(),
        Subject = Subject,
        SubjectKeys = SubjectKeys.Count > 1 ? [.. SubjectKeys] : null,
        Date = Date,
        StartTime = Start,
        EndTime = End
    };

    /// <summary>拷一份（对照表里改草稿时用）。</summary>
    public ExamPlanEntry Clone() => new()
    {
        Date = Date,
        Start = Start,
        DurationMinutes = DurationMinutes,
        Subject = Subject,
        SubjectKeys = [.. SubjectKeys],
        IndexInDay = IndexInDay,
        BreakMinutes = BreakMinutes,
        ConflictReason = ConflictReason
    };
}

/// <summary>一次快速布置排出来的全部结果：逐场安排 + 汇总 + 冲突清单。</summary>
public sealed class ExamPlan(IReadOnlyList<ExamPlanEntry> entries)
{
    /// <summary>排出来的每一场（按日期 + 开考时刻升序）。</summary>
    public IReadOnlyList<ExamPlanEntry> Entries { get; } = entries;

    /// <summary>一场都没排出来（没选科目，或者作息一行都没有）。</summary>
    public bool IsEmpty => Entries.Count == 0;

    /// <summary>有排得不合理的场次。</summary>
    public bool HasConflicts => Entries.Any(entry => entry.HasConflict);

    /// <summary>冲突条数。</summary>
    public int ConflictCount => Entries.Count(entry => entry.HasConflict);

    /// <summary>第一天。</summary>
    public DateOnly? FirstDay => Entries.Count == 0 ? null : Entries.Min(entry => entry.Date);

    /// <summary>最后一天。</summary>
    public DateOnly? LastDay => Entries.Count == 0 ? null : Entries.Max(entry => entry.Date);

    /// <summary>跨了几天（含首尾）。</summary>
    public int DayCount => FirstDay is { } first && LastDay is { } last ? last.DayNumber - first.DayNumber + 1 : 0;

    /// <summary>按日期分组（对照表按天分段显示用）。</summary>
    public IEnumerable<IGrouping<DateOnly, ExamPlanEntry>> GroupByDay() =>
        Entries.GroupBy(entry => entry.Date).OrderBy(group => group.Key);

    /// <summary>转成要落盘的考试清单（顺序就是排布顺序）。</summary>
    public List<ExamEntry> ToEntries() => [.. Entries.Select(entry => entry.ToEntry())];
}

/// <summary>
///     把「考哪些科目」+「一天几场、几点开考、中间歇多久」+「第一天是哪天」排成一张具体的考试表。
///     <para>
///         纯函数、不碰磁盘也不碰界面：向导的对照表、模板套用、单元测试都走这一条路径，
///         免得三处各写一套顺延逻辑、算出三个不同的结果。
///     </para>
///     <para>
///         排布规则很简单，但**必须写清楚**，否则用户看到的和以为的不一样：
///         <list type="number">
///             <item>科目按用户给的顺序，依次填进一天的各个场次；</item>
///             <item>一天的场次填满就换下一天（所以「每天几场」完全由作息的行数决定）；</item>
///             <item>
///                 一天里的第 1 场永远用它的固定时刻；之后的场次看自己的
///                 <see cref="ExamPlanSlot.StartMode" />——固定时刻就按固定时刻，
///                 接上一场就「上一场收卷 + 休息分钟数」。
///             </item>
///         </list>
///     </para>
/// </summary>
public static class ExamSchedulePlanner
{
    /// <summary>
    ///     排一张考试表。
    /// </summary>
    /// <param name="subjects">考哪些科目、各考多久（顺序即考试顺序）。</param>
    /// <param name="slots">一天的作息；行数就是「每天最多几场」。</param>
    /// <param name="firstDay">第一天是哪天。</param>
    /// <returns>排布结果（科目或作息为空时是空计划）。</returns>
    public static ExamPlan Plan(
        IEnumerable<ExamPlanSubject>? subjects,
        IEnumerable<ExamPlanSlot>? slots,
        DateOnly firstDay)
    {
        var subjectList = subjects?.Where(subject => subject is not null).ToList() ?? [];
        var slotList = slots?.Where(slot => slot is not null).ToList() ?? [];

        if (subjectList.Count == 0 || slotList.Count == 0)
            return new ExamPlan([]);

        var entries = new List<ExamPlanEntry>(subjectList.Count);
        var cursor = 0;
        var dayOffset = 0;

        while (cursor < subjectList.Count)
        {
            var date = firstDay.AddDays(dayOffset);

            // 这一天已经排到几点：用来判定「固定时刻比上一场收卷还早」。
            TimeOnly? previousEnd = null;

            for (var slotIndex = 0; slotIndex < slotList.Count && cursor < subjectList.Count; slotIndex++)
            {
                var slot = slotList[slotIndex];
                var subject = subjectList[cursor];

                var start = ResolveStart(slot, slotIndex, previousEnd);

                entries.Add(new ExamPlanEntry
                {
                    Date = date,
                    Start = start,
                    DurationMinutes = subject.DurationMinutes,
                    Subject = subject.DisplayName,
                    SubjectKeys = subject.IsCombined ? [.. subject.Subjects] : [],
                    IndexInDay = slotIndex + 1,
                    BreakMinutes = slotIndex == 0
                        ? ExamPlanSlot.DefaultBreakMinutes
                        : ExamPlanSlot.ClampBreak(slot.BreakMinutes)
                });

                previousEnd = entries[^1].End;
                cursor++;
            }

            dayOffset++;
        }

        Refresh(entries);
        return new ExamPlan(entries);
    }

    /// <summary>
    ///     算这一场的开考时刻：当天第 1 场固定用 <see cref="ExamPlanSlot.FixedStart" />，
    ///     之后的场次按自己的模式来（固定时刻，或者接上一场收卷 + 休息）。
    /// </summary>
    private static TimeOnly ResolveStart(ExamPlanSlot slot, int slotIndex, TimeOnly? previousEnd)
    {
        // 第 1 场没有「上一场」可接，无论选了什么都只能用固定时刻。
        if (slotIndex == 0 || previousEnd is not { } lastEnd)
            return slot.FixedStart;

        if (slot.StartMode == ExamSlotStartMode.Fixed)
            return slot.FixedStart;

        var minutes = lastEnd.Hour * 60 + lastEnd.Minute + ExamPlanSlot.ClampBreak(slot.BreakMinutes);
        return minutes >= ExamPlanEntry.MinutesPerDay
            ? ExamPlanEntry.ClampedEnd
            : TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutes));
    }

    /// <summary>
    ///     把一批排好的场次重新捋一遍：**按日期 + 开考时刻排序**、重排「当天第几场」、
    ///     重算每一场的冲突说明。
    ///     <para>
    ///         用户在对照表里改了时刻 / 时长 / 顺序、删掉一场之后都要调它——
    ///         冲突判定依赖前后两场的关系，只改一处而不重算是错的。
    ///     </para>
    /// </summary>
    /// <param name="entries">就地排序与重算的场次（顺序会被改掉）。</param>
    /// <param name="existing">看板上已经有的考试（「与已有考试撞了」要拿它比）。</param>
    public static void Refresh(IList<ExamPlanEntry> entries, IEnumerable<ExamEntry>? existing = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
            return;

        var ordered = entries
            .OrderBy(entry => entry.Date)
            .ThenBy(entry => entry.Start)
            .ThenBy(entry => entry.Subject, StringComparer.CurrentCulture)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
            entries[i] = ordered[i];

        var taken = existing?.Where(entry => entry is not null).ToList() ?? [];

        DateOnly? currentDate = null;
        var indexInDay = 0;
        DateTime? previousEnd = null;

        foreach (var entry in ordered)
        {
            entry.ConflictReason = null;

            if (currentDate != entry.Date)
            {
                // 换天了：「上一场」清空，否则第二天的 08:00 会被当成撞了第一天的最后一场。
                currentDate = entry.Date;
                indexInDay = 0;
                previousEnd = null;
            }

            indexInDay++;
            entry.IndexInDay = indexInDay;

            if (entry.CrossesMidnight)
                entry.ConflictReason = CR.Exam_Plan_Conflict_CrossDay;
            else if (previousEnd is { } lastEnd && entry.StartAt < lastEnd)
                entry.ConflictReason = CR.Exam_Plan_Conflict_Overlap;
            else if (taken.Any(other => ExamStatusPolicy.Overlaps(other, entry.ToEntry())))
                entry.ConflictReason = CR.Exam_Plan_Conflict_Existing;

            previousEnd = entry.EndAt;
        }
    }

    /// <summary>
    ///     往一张现成的表里追加：已经有的考试不动，新排的接着往后排（用于「看板里已经排了一半，
    ///     剩下的用向导补上」）。与已有考试撞了的那几场会标出来。
    /// </summary>
    public static ExamPlan PlanAfter(
        IEnumerable<ExamPlanSubject>? subjects,
        IEnumerable<ExamPlanSlot>? slots,
        DateOnly firstDay,
        IEnumerable<ExamEntry>? existing)
    {
        var plan = Plan(subjects, slots, firstDay);
        if (plan.IsEmpty || existing is null)
            return plan;

        var entries = plan.Entries.ToList();
        Refresh(entries, existing);
        return new ExamPlan(entries);
    }

    /// <summary>这一场与同一天上一场之间歇了多久（当天第一场没有「上一场」，返回 <see langword="null" />）。</summary>
    public static TimeSpan? BreakBefore(IReadOnlyList<ExamPlanEntry> entries, ExamPlanEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(entry);

        var index = IndexOf(entries, entry);
        if (index <= 0)
            return null;

        var previous = entries[index - 1];
        if (previous.Date != entry.Date)
            return null;

        var gap = entry.StartAt - previous.EndAt;
        return gap < TimeSpan.Zero ? TimeSpan.Zero : gap;
    }

    /// <summary>找出这一场在列表里的下标（<see cref="ExamPlanEntry" /> 没重写相等性，只能按引用找）。</summary>
    public static int IndexOf(IReadOnlyList<ExamPlanEntry> entries, ExamPlanEntry entry)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (ReferenceEquals(entries[i], entry))
                return i;
        }

        return -1;
    }

    /// <summary>
    ///     把第 <paramref name="index" /> 场的「距上一场多久」按它当前的开考时刻重算一遍。
    ///     <para>
    ///         用户在对照表里直接改某一场的开考时刻时调它：改完之后这一场的间隔就变了，
    ///     后面那几场要靠 <see cref="Cascade" /> 跟着顺延。
    ///     </para>
    ///     <para>当天第一场没有上一场，间隔回到默认值（只用于显示）。</para>
    /// </summary>
    public static void SyncBreak(IReadOnlyList<ExamPlanEntry> entries, int index)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (index < 0 || index >= entries.Count)
            return;

        var entry = entries[index];
        if (index == 0 || entries[index - 1].Date != entry.Date)
        {
            entry.BreakMinutes = ExamPlanSlot.DefaultBreakMinutes;
            return;
        }

        var gap = entry.StartAt - entries[index - 1].EndAt;
        entry.BreakMinutes = ExamPlanSlot.ClampBreak((int)Math.Round(gap.TotalMinutes));
    }

    /// <summary>
    ///     从第 <paramref name="fromIndex" /> 场之后开始，把同一天后面每一场的开考时刻
    ///     重算成「上一场收卷 + 自己的休息分钟数」。
    ///     <para>
    ///         这就是用户要的顺延：上面那场从 3 点拖到 4 点收卷，下面那场就该从 4 点变 5 点，
    ///     而不是各写各的时刻、把两场叠在一起。**换天就停**——第二天第一场的时刻是独立的锚点。
    ///     </para>
    /// </summary>
    /// <param name="entries">已经按日期 + 时刻排好序的场次（顺序会被 <see cref="Refresh" /> 再捋一遍）。</param>
    /// <param name="fromIndex">改动发生的那一场的下标。</param>
    public static void Cascade(IList<ExamPlanEntry> entries, int fromIndex)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
            return;

        var start = Math.Clamp(fromIndex, 0, entries.Count - 1);

        for (var i = start + 1; i < entries.Count; i++)
        {
            var previous = entries[i - 1];
            if (previous.Date != entries[i].Date)
                break;

            var minutes = previous.StartMinutes + previous.DurationMinutes +
                          ExamPlanSlot.ClampBreak(entries[i].BreakMinutes);
            entries[i].Start = minutes >= ExamPlanEntry.MinutesPerDay
                ? ExamPlanEntry.ClampedEnd
                : TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutes));
        }

        Refresh(entries);
    }
}
