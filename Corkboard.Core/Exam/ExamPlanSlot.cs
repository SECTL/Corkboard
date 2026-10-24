namespace Corkboard.Core.Exam;

/// <summary>一天里第 N 场考试怎么定开考时刻。</summary>
public enum ExamSlotStartMode
{
    /// <summary>固定时刻（到点就开考）。</summary>
    Fixed = 0,

    /// <summary>接在上一场收卷之后，再歇若干分钟。</summary>
    AfterPrevious = 1
}

/// <summary>
///     作息里的一场：这一场几点开考、跟上一场之间歇多久。
///     <para>
///         一天里的第 1 场**永远用 <see cref="FixedStart" />**（没有「上一场」可以接），
///         所以 <see cref="StartMode" /> 只对第 2 场及之后有意义；界面上第 1 行也就只显示时刻选择器。
///     </para>
/// </summary>
public sealed class ExamPlanSlot
{
    /// <summary>休息时长的合法区间（分钟）。</summary>
    public const int MinBreakMinutes = 0;

    /// <summary>两场之间最多歇多久——四个小时，再长就该拆成两天了。</summary>
    public const int MaxBreakMinutes = 240;

    /// <summary>默认两场之间歇多久（二十分钟，够上个厕所喝口水）。</summary>
    public const int DefaultBreakMinutes = 20;

    public ExamPlanSlot()
    {
    }

    public ExamPlanSlot(TimeOnly fixedStart, ExamSlotStartMode startMode = ExamSlotStartMode.Fixed, int breakMinutes = 20)
    {
        FixedStart = fixedStart;
        StartMode = startMode;
        BreakMinutes = ClampBreak(breakMinutes);
    }

    /// <summary>固定开考时刻（<see cref="ExamSlotStartMode.AfterPrevious" /> 时只在「当天第一场」生效）。</summary>
    public TimeOnly FixedStart { get; set; } = new(8, 0);

    /// <summary>开考时刻怎么定。</summary>
    public ExamSlotStartMode StartMode { get; set; } = ExamSlotStartMode.Fixed;

    /// <summary>与上一场之间的休息分钟数（仅 <see cref="ExamSlotStartMode.AfterPrevious" /> 用）。</summary>
    public int BreakMinutes { get; set; } = 20;

    /// <summary>把休息时长夹进合法区间。</summary>
    public static int ClampBreak(int minutes) => Math.Clamp(minutes, MinBreakMinutes, MaxBreakMinutes);

    /// <summary>拷一份（界面改草稿时用，取消不影响原对象）。</summary>
    public ExamPlanSlot Clone() => new()
    {
        FixedStart = FixedStart,
        StartMode = StartMode,
        BreakMinutes = BreakMinutes
    };
}

/// <summary>
///     常见的作息预设。用户点一下就能拿到一天一场 / 两场 / 四场 / 五场的排法，
///     之后再逐行改时刻与休息。
/// </summary>
public static class ExamPlanSlotPresets
{
    /// <summary>一天一场：上午 08:00。</summary>
    public static IReadOnlyList<ExamPlanSlot> SingleMorning() =>
    [
        new(new TimeOnly(8, 0))
    ];

    /// <summary>一天两场：上午 08:00、下午 14:00。</summary>
    public static IReadOnlyList<ExamPlanSlot> TwoPerDay() =>
    [
        new(new TimeOnly(8, 0)),
        new(new TimeOnly(14, 0))
    ];

    /// <summary>
    ///     一天三场：上午两场（中间歇 20 分钟）、下午一场。
    ///     早上连着考两科、下午只考一科时用这个，比一天两场排得快。
    /// </summary>
    public static IReadOnlyList<ExamPlanSlot> ThreePerDay() =>
    [
        new(new TimeOnly(8, 0)),
        new(new TimeOnly(10, 0), ExamSlotStartMode.AfterPrevious, 20),
        new(new TimeOnly(14, 0))
    ];

    /// <summary>一天四场：上午两场（中间歇 20 分钟）、下午两场。</summary>
    public static IReadOnlyList<ExamPlanSlot> FourPerDay() =>
    [
        new(new TimeOnly(8, 0)),
        new(new TimeOnly(10, 0), ExamSlotStartMode.AfterPrevious, 20),
        new(new TimeOnly(14, 0)),
        new(new TimeOnly(16, 0), ExamSlotStartMode.AfterPrevious, 20)
    ];

    /// <summary>一天五场：上午两场、下午两场、晚上一场。</summary>
    public static IReadOnlyList<ExamPlanSlot> FivePerDay() =>
    [
        new(new TimeOnly(8, 0)),
        new(new TimeOnly(10, 0), ExamSlotStartMode.AfterPrevious, 20),
        new(new TimeOnly(14, 0)),
        new(new TimeOnly(16, 0), ExamSlotStartMode.AfterPrevious, 20),
        new(new TimeOnly(19, 0))
    ];

    /// <summary>
    ///     一天四场、但下午接上午的长场：上午 08:00 一场、下午 14:00 一场、16:30 一场、19:00 一场。
    ///     给「上午一门 150 分钟、下午连着两门 75 分钟」这种常见排法用。
    /// </summary>
    public static IReadOnlyList<ExamPlanSlot> LongMorningPlusTwo() =>
    [
        new(new TimeOnly(8, 0)),
        new(new TimeOnly(14, 0)),
        new(new TimeOnly(16, 0), ExamSlotStartMode.AfterPrevious, 30),
        new(new TimeOnly(19, 0))
    ];

    /// <summary>把一份预设拷成可改的列表。</summary>
    public static List<ExamPlanSlot> Materialize(IEnumerable<ExamPlanSlot> preset) =>
        [.. preset.Select(slot => slot.Clone())];

    /// <summary>
    ///     按科目数挑一份合适的作息：科目多就一天多排几场，别让 9 门课按一天一场排成 9 天。
    ///     <para>1–2 门一天一场、3–4 门一天两场、5–6 门一天三场、7–8 门一天四场、9 门以上一天五场。</para>
    /// </summary>
    public static IReadOnlyList<ExamPlanSlot> Recommend(int subjectCount) => subjectCount switch
    {
        <= 2 => SingleMorning(),
        <= 4 => TwoPerDay(),
        <= 6 => ThreePerDay(),
        <= 8 => FourPerDay(),
        _ => FivePerDay()
    };

    /// <summary>
    ///     按「一天几场 + 第一场几点开考 + 每场之间歇多久」生成一份作息。
    ///     <para>
    ///         这是向导现在用的那一种：用户只需要填三个数字，不用逐场去配时刻。
    ///     第 1 场用 <paramref name="firstStart" />，其余全部「接上一场 + 休息」——
    ///     于是改第 1 场的时间、或者在对照表里改某一场，后面都会跟着顺延（见
    ///     <see cref="ExamSchedulePlanner.Cascade" />），不会出现「上面 3 点、下面还是 4 点」。
    ///     </para>
    /// </summary>
    /// <param name="count">一天几场（小于 1 按 1 算）。</param>
    /// <param name="firstStart">第一场开考时刻。</param>
    /// <param name="breakMinutes">场与场之间歇多久。</param>
    public static IReadOnlyList<ExamPlanSlot> ForCount(int count, TimeOnly firstStart, int breakMinutes)
    {
        var slots = new List<ExamPlanSlot>(Math.Max(1, count));
        for (var i = 0; i < Math.Max(1, count); i++)
        {
            slots.Add(i == 0
                ? new ExamPlanSlot(firstStart)
                : new ExamPlanSlot(firstStart, ExamSlotStartMode.AfterPrevious, breakMinutes));
        }

        return slots;
    }
}

/// <summary>
///     作息整体平移：改了第 1 场的开考时刻，后面那些「固定时刻」的场次跟着挪同样的量。
///     <para>
///         为什么不干脆把后面几场都做成「接上一场」：预设里 14:00、19:00 这种是**绝对锚点**
///         （下午就是下午），跟着上一场漂会让「上午拖堂 → 下午一路晚点」把整天排歪；
///         而用户真去改第 1 场时刻时，意思几乎总是「整场考试整体提前/推后」，所以按平移处理。
///     </para>
/// </summary>
public static class ExamPlanSlotShift
{
    /// <summary>一天的分钟数（用来把平移后的时刻绕回当天）。</summary>
    private const int MinutesPerDay = 24 * 60;

    /// <summary>
    ///     把 <paramref name="slots" /> 里第 1 场之后的「固定时刻」场次一起平移
    ///     <paramref name="deltaMinutes" /> 分钟，接上一场的那些不动（它们本来就跟着走）。
    ///     平移后落到当天之外的，夹在 00:00–23:59。
    /// </summary>
    public static void Apply(IReadOnlyList<ExamPlanSlot> slots, int deltaMinutes)
    {
        if (slots is null || slots.Count == 0 || deltaMinutes == 0)
            return;

        for (var i = 1; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot.StartMode != ExamSlotStartMode.Fixed)
                continue;

            var minutes = slot.FixedStart.Hour * 60 + slot.FixedStart.Minute + deltaMinutes;
            slot.FixedStart = new TimeOnly(Math.Clamp(minutes, 0, MinutesPerDay - 1) / 60,
                Math.Clamp(minutes, 0, MinutesPerDay - 1) % 60);
        }
    }

    /// <summary>算出「把第 1 场从 <paramref name="before" /> 改到 <paramref name="after" />」需要平移多少分钟。</summary>
    public static int Delta(TimeOnly before, TimeOnly after) =>
        after.Hour * 60 + after.Minute - (before.Hour * 60 + before.Minute);
}
