namespace Corkboard.Core.Exam;

/// <summary>
///     快速布置里「一场考试要考什么」：一个科目，或者几个科目合成一场（理综 / 文综那种合卷）。
///     <para>
///         合卷仍然是一条 <see cref="ExamEntry" />——它在看板上就是一行「物理 · 化学 09:00 – 11:30」，
///         而不是两行同时进行的考试。绑定的科目名记在 <see cref="ExamEntry.SubjectKeys" /> 里，
///         <see cref="ExamEntry.Subject" /> 存拼出来的显示名。
///     </para>
/// </summary>
public sealed class ExamPlanSubject
{
    /// <summary>合卷显示名里各科之间的连接符。</summary>
    public const string CombineSeparator = " · ";

    public ExamPlanSubject(IEnumerable<string> subjects, int durationMinutes)
    {
        ArgumentNullException.ThrowIfNull(subjects);

        var names = new List<string>();
        foreach (var subject in subjects)
        {
            var name = (subject ?? string.Empty).Trim();
            if (name.Length > 0 && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                names.Add(name);
        }

        Subjects = names;
        DurationMinutes = ExamDurationPresets.Clamp(durationMinutes);
    }

    /// <summary>绑定的科目（合卷时多个，按用户勾选顺序）。</summary>
    public IReadOnlyList<string> Subjects { get; }

    /// <summary>考试时长（分钟，已夹进 <see cref="ExamDurationPresets" /> 的区间）。</summary>
    public int DurationMinutes { get; }

    /// <summary>显示名：单科就是科目名，合卷是「物理 · 化学」。</summary>
    public string DisplayName => string.Join(CombineSeparator, Subjects);

    /// <summary>这是合卷（绑了两个以上科目）。</summary>
    public bool IsCombined => Subjects.Count > 1;

    /// <summary>时长的可显示形式（<see cref="TimeSpan" />）。</summary>
    public TimeSpan Duration => TimeSpan.FromMinutes(DurationMinutes);

    public override string ToString() => $"{DisplayName} ({DurationMinutes}m)";
}
