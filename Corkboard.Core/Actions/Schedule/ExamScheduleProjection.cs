using Corkboard.Core.Exam;
using Corkboard.Core.Models.SubConfigs.Exam;

namespace Corkboard.Core.Actions.Schedule;

/// <summary>
///     把「当前看板的考试排期 + 自动放映配置」投影成日程中心里的系统日程条目。
///     <para>
///         ⚠️ 放映窗口的边界<b>不在这里算</b>：一律复用 <see cref="ExamAutoShowPolicy.ResolveWindow" />
///         （第一场开考减提前量 ～ 最后一场收卷加延迟），否则界面上看到的时间和看门狗真的会弹的时间会漂移。
///     </para>
///     <para>
///         一块看板在日程里只占<b>一条</b>（整场放映是一件事，用户要的是「这场考试期间会全屏」），
///         所以返回 0 或 1 条。标题用考试科目拼：单场「考试看板 · 语文」、多场「考试看板 · 语文 等 N 场」
///         （投影拿不到看板标题，看板级标题由 App 层的源按需替换）。
///     </para>
/// </summary>
public static class ExamScheduleProjection
{
    /// <summary>系统日程源 Id（也是条目标识与「今天不执行」的抑制键）。</summary>
    public const string SourceId = "exam-auto-show";

    /// <summary>对应的设置页 Id（「自动放映」页）。</summary>
    public const string SettingsPageId = "settings.exam.autoplay";

    /// <summary>标题前缀。</summary>
    public const string TitlePrefix = "考试看板";

    /// <summary>
    ///     投影 <c>[fromInclusive, toExclusive)</c> 里与放映期有交集的条目。
    ///     条目本身<b>不被裁切</b>（保留真实放映起止），裁切由 <see cref="ScheduleMerge.Merge" /> 负责。
    /// </summary>
    public static IReadOnlyList<ScheduleItem> Project(
        IEnumerable<ExamEntry>? exams,
        ExamAutoShowSettingsConfig settings,
        DateTime fromInclusive,
        DateTime toExclusive)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (fromInclusive >= toExclusive)
            return [];

        var list = exams?.Where(exam => exam is not null).ToList() ?? [];
        if (list.Count == 0)
            return [];

        var window = ExamAutoShowPolicy.ResolveWindow(list, settings.LeadMinutes, settings.CloseDelayMinutes);
        if (window is not { } resolved)
            return [];

        // 与查询区间没有交集就不出现（相切也不算：区间是左闭右开）。
        if (resolved.Start >= toExclusive || resolved.End <= fromInclusive)
            return [];

        var targets = settings.AutoSwitchBoard
            ? new[] { BoardTargetIds.ExamBoard, BoardTargetIds.DisplayBoard }
            : new[] { BoardTargetIds.ExamBoard };

        return
        [
            new ScheduleItem
            {
                Id = SourceId,
                Kind = ScheduleItemKind.ExamAutoShow,
                Title = BuildTitle(list),
                Start = resolved.Start,
                End = resolved.End,
                IsEnabled = true,
                IsEditable = false,
                CanPause = true,
                IsSuppressed = false,
                SettingsPageId = SettingsPageId,
                TargetIds = targets,
                RuleId = null
            }
        ];
    }

    private static string BuildTitle(IReadOnlyList<ExamEntry> exams)
    {
        var ordered = exams.OrderBy(exam => exam.StartAt).ToList();
        var subject = ordered[0].Subject;
        if (string.IsNullOrWhiteSpace(subject))
            return TitlePrefix;

        return ordered.Count == 1
            ? $"{TitlePrefix} · {subject}"
            : $"{TitlePrefix} · {subject} 等 {ordered.Count} 场";
    }
}
