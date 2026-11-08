using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Actions;
using Corkboard.Core.Actions.Schedule;
using Corkboard.Services.Actions;
using LR = Corkboard.Langs.Action.Resources;

namespace Corkboard.ViewModels.Actions;

/// <summary>
///     列表 / 周视图 / 月视图明细里的一行：既可能是「我的行动」的一次发生点，也可能是「系统日程」的
///     一条投影区间。文本都在工厂里拼好（视图只绑字符串），因此这里没有 Cell 之外的业务判断。
/// </summary>
public sealed partial class ActionOccurrenceItem : ObservableObject
{
    private bool _suppressToggle;

    // ── 数据来源 ──────────────────────────────────────────────────────────────

    /// <summary>自定义规则的 Id；系统日程为 null。</summary>
    public Guid? RuleId { get; private set; }

    /// <summary>来源 Id（规则 Id 的字符串形式，或日程源的 Id）。</summary>
    public string SourceId { get; private set; } = "";

    /// <summary>系统日程（只读，来自投影）。</summary>
    public bool IsSystem { get; private set; }

    /// <summary>自定义行动（可编辑、可启停、可立即执行）。</summary>
    public bool IsRule => !IsSystem;

    public ScheduleItemKind Kind { get; private set; }

    public ActionRule? Rule { get; private set; }

    public ScheduleItem? Item { get; private set; }

    /// <summary>来自周 / 月视图的一次具体发生点（「立即执行」按计划时刻记 OccurrenceAt）。</summary>
    public bool IsOccurrencePoint { get; private set; }

    // ── 时间 ──────────────────────────────────────────────────────────────────

    public DateTime Start { get; private set; }

    public DateTime? End { get; private set; }

    /// <summary>区间事件（系统日程）为真。</summary>
    public bool IsInterval => End is not null;

    /// <summary>「07:40」。</summary>
    public string TimeText { get; private set; } = "";

    /// <summary>「07:40」或「08:45–11:30」。</summary>
    public string RangeText { get; private set; } = "";

    // ── 文本列 ────────────────────────────────────────────────────────────────

    public string Title { get; private set; } = "";

    /// <summary>条件列第一行：「每天 07:40」/「每周一 三 五 12:10」/「单次 11-20 08:00」。</summary>
    public string TriggerText { get; private set; } = "";

    /// <summary>条件列第二行小字：「11-01 至 01-31 生效」；没有生效范围时为空。</summary>
    public string ConditionText { get; private set; } = "";

    public bool HasCondition { get; private set; }

    /// <summary>功能列：「作业板 / 考试看板」。</summary>
    public string TargetsText { get; private set; } = "";

    public bool HasTargets { get; private set; }

    /// <summary>执行列：「显示作业板；隐藏考试看板」。</summary>
    public string EffectText { get; private set; } = "";

    /// <summary>周视图条目第二行（系统日程用标题 + 功能，点事件用执行摘要）。</summary>
    public string SummaryText { get; private set; } = "";

    // ── 状态 ──────────────────────────────────────────────────────────────────

    public bool IsEditable { get; private set; }

    public bool CanPause { get; private set; }

    public string? SettingsPageId { get; private set; }

    /// <summary>系统日程今天被「今天不执行」压住了。</summary>
    [ObservableProperty] private bool _isPausedToday;

    public string PauseTodayText => IsPausedToday ? LR.Action_System_ResumeToday : LR.Action_System_PauseToday;

    /// <summary>最多 10 条最近运行记录，供胶囊里的 Popup 显示。</summary>
    public IReadOnlyList<ActionRunViewItem> RecentRuns { get; private set; } = [];

    public bool HasRecentRuns => RecentRuns.Count > 0;

    /// <summary>最近一次结果的小胶囊文案：「成功」/「未执行」。</summary>
    public string ResultText { get; private set; } = LR.Action_Status_NotRun;

    /// <summary>「今天 07:40」/「昨天 07:40」/「11-05 07:40」；没有记录时为空。</summary>
    public string ResultTimeText { get; private set; } = "";

    public bool IsResultSuccess { get; private set; }

    public bool IsResultPartial { get; private set; }

    public bool IsResultFailed { get; private set; }

    public bool IsResultSkipped { get; private set; }

    public bool IsResultNone { get; private set; } = true;

    /// <summary>停用的行动整行压到 60% 不透明度；系统日程不受影响。</summary>
    public double RowOpacity => IsSystem || IsEnabled ? 1d : 0.6d;

    [ObservableProperty] private bool _isEnabled;

    [ObservableProperty] private bool _isHistoryOpen;

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(RowOpacity));
        if (_suppressToggle) return;
        EnabledChanged?.Invoke(this, value);
    }

    partial void OnIsPausedTodayChanged(bool value) => OnPropertyChanged(nameof(PauseTodayText));

    /// <summary>开关被用户拨动时的回调（由窗口 ViewModel 写回 Store）。</summary>
    public Action<ActionOccurrenceItem, bool>? EnabledChanged { get; set; }


    // ── 工厂：列表里的「我的行动」一行 ────────────────────────────────────────

    public static ActionOccurrenceItem FromRule(ActionRule rule, DateTime now, IReadOnlyList<ActionRun> runs)
    {
        var next = ActionSchedule.NextOccurrence(rule, now);
        var effect = ActionTextFormatter.DescribeSteps(rule.Steps);
        var item = new ActionOccurrenceItem
        {
            Rule = rule,
            RuleId = rule.Id,
            SourceId = rule.Id.ToString(),
            Kind = ScheduleItemKind.CustomAction,
            Start = next ?? rule.CreatedAt,
            Title = ActionTextFormatter.Summarize(rule),
            TriggerText = ActionTextFormatter.DescribeTrigger(rule.Trigger),
            ConditionText = ActionTextFormatter.DescribeCondition(rule.Condition),
            TargetsText = DescribeTargets(rule.Steps.Select(step => step.TargetId)),
            EffectText = string.IsNullOrEmpty(effect) ? LR.Action_None : effect,
            SummaryText = effect,
            IsEditable = true,
            TimeText = next is { } n ? n.ToString("HH:mm", CultureInfo.CurrentCulture) : "",
            RangeText = next is { } nn ? nn.ToString("HH:mm", CultureInfo.CurrentCulture) : ""
        };
        item.HasCondition = !string.IsNullOrEmpty(item.ConditionText);
        item.HasTargets = !string.IsNullOrEmpty(item.TargetsText);
        item._suppressToggle = true;
        item.IsEnabled = rule.Enabled;
        item._suppressToggle = false;
        item.ApplyRuns(runs, now);
        return item;
    }

    // ── 工厂：列表里的「系统日程」一行（一个日程源一行） ──────────────────────

    public static ActionOccurrenceItem FromSource(IScheduleSource source, IReadOnlyList<ScheduleItem> items,
        bool isPausedToday, IReadOnlyList<ActionRun> runs, DateTime now)
    {
        var first = items.FirstOrDefault();
        var kind = first?.Kind ?? ScheduleItemKind.ExamAutoShow;
        var title = string.IsNullOrWhiteSpace(source.DisplayName) ? KindTitle(kind) : source.DisplayName;
        var next = items.FirstOrDefault(i => i.Start >= now) ?? first;
        var effect = kind switch
        {
            ScheduleItemKind.DisplayAutoShow => LR.Action_System_Effect_Display,
            _ => LR.Action_System_Effect_Exam
        };
        var description = kind switch
        {
            ScheduleItemKind.DisplayAutoShow => LR.Action_System_DisplayAutoShow_Description,
            _ => LR.Action_System_ExamAutoShow_Description
        };
        var item = new ActionOccurrenceItem
        {
            SourceId = source.Id,
            IsSystem = true,
            Kind = kind,
            Start = next?.Start ?? now,
            End = next?.End,
            Item = next,
            Title = title,
            TriggerText = description,
            ConditionText = next is null
                ? LR.Action_System_NotEnabled
                : string.Format(CultureInfo.CurrentCulture, LR.Action_NextFormat,
                    ActionViewText.DescribeWhen(next.Start, now)),
            TargetsText = DescribeTargets(items.SelectMany(i => i.TargetIds)),
            EffectText = effect,
            SummaryText = next?.Title is { Length: > 0 } t ? t : description,
            IsEditable = false,
            CanPause = source.CanPause,
            SettingsPageId = items.Select(i => i.SettingsPageId).FirstOrDefault(p => !string.IsNullOrEmpty(p))
                             ?? DefaultSettingsPageId(kind),
            TimeText = next?.Start.ToString("HH:mm", CultureInfo.CurrentCulture) ?? "",
            RangeText = next is null ? "" : ActionTextFormatter.DescribeInterval(next.Start, next.End)
        };
        item._suppressToggle = true;
        item.IsEnabled = items.Any(i => i.IsEnabled);
        item._suppressToggle = false;
        item.HasCondition = !string.IsNullOrEmpty(item.ConditionText);
        item.HasTargets = !string.IsNullOrEmpty(item.TargetsText);
        item.IsPausedToday = isPausedToday;
        item.ApplyRuns(runs, now);
        return item;
    }

    // ── 工厂：周 / 月里的点事件 ───────────────────────────────────────────────

    public static ActionOccurrenceItem FromOccurrence(ActionRule rule, DateTime at, IReadOnlyList<ActionRun> runs,
        DateTime now)
    {
        var effect = ActionTextFormatter.DescribeSteps(rule.Steps);
        var item = new ActionOccurrenceItem
        {
            Rule = rule,
            RuleId = rule.Id,
            SourceId = rule.Id.ToString(),
            Kind = ScheduleItemKind.CustomAction,
            Start = at,
            IsOccurrencePoint = true,
            Title = ActionTextFormatter.Summarize(rule),
            TriggerText = ActionTextFormatter.DescribeTrigger(rule.Trigger),
            EffectText = effect,
            SummaryText = string.IsNullOrEmpty(effect) ? LR.Action_None : effect,
            IsEditable = true,
            TimeText = at.ToString("HH:mm", CultureInfo.CurrentCulture),
            RangeText = at.ToString("HH:mm", CultureInfo.CurrentCulture)
        };
        item._suppressToggle = true;
        item.IsEnabled = rule.Enabled;
        item._suppressToggle = false;
        item.HasTargets = false;
        item.ApplyRuns(runs, now);
        return item;
    }

    // ── 工厂：周 / 月里的区间块（系统日程） ───────────────────────────────────

    public static ActionOccurrenceItem FromScheduleItem(ScheduleItem scheduleItem, bool isPausedToday,
        IReadOnlyList<ActionRun> runs, DateTime now)
    {
        var item = new ActionOccurrenceItem
        {
            SourceId = scheduleItem.Id,
            IsSystem = true,
            Kind = scheduleItem.Kind,
            Item = scheduleItem,
            Start = scheduleItem.Start,
            End = scheduleItem.End,
            Title = scheduleItem.Title,
            TriggerText = scheduleItem.Title,
            SummaryText = scheduleItem.Title,
            EffectText = scheduleItem.Kind switch
            {
                ScheduleItemKind.DisplayAutoShow => LR.Action_System_Effect_Display,
                _ => LR.Action_System_Effect_Exam
            },
            IsEditable = false,
            CanPause = scheduleItem.CanPause,
            SettingsPageId = scheduleItem.SettingsPageId,
            TimeText = scheduleItem.Start.ToString("HH:mm", CultureInfo.CurrentCulture),
            RangeText = ActionTextFormatter.DescribeInterval(scheduleItem.Start, scheduleItem.End)
        };
        item._suppressToggle = true;
        item.IsEnabled = scheduleItem.IsEnabled;
        item._suppressToggle = false;
        item.HasTargets = scheduleItem.TargetIds.Count > 0;
        item.IsPausedToday = isPausedToday || scheduleItem.IsSuppressed;
        item.ApplyRuns(runs, now);
        return item;
    }

    // ── 内部 ──────────────────────────────────────────────────────────────────

    private void ApplyRuns(IReadOnlyList<ActionRun> runs, DateTime now)
    {
        RecentRuns = runs
            .OrderByDescending(run => run.FinishedAt)
            .Take(10)
            .Select(run => new ActionRunViewItem
            {
                StatusText = ActionTextFormatter.DescribeRunStatus(run.Status),
                TimeText = ActionViewText.DescribeWhen(run.StartedAt, now),
                SourceText = ActionTextFormatter.DescribeTriggeredBy(run.TriggeredBy),
                DetailText = DescribeRunDetail(run),
                IsSuccess = run.Status == ActionRunStatus.Success,
                IsPartial = run.Status == ActionRunStatus.Partial,
                IsFailed = run.Status == ActionRunStatus.Failed,
                IsSkipped = run.Status == ActionRunStatus.Skipped
            })
            .ToList();

        var last = runs.OrderByDescending(run => run.FinishedAt).FirstOrDefault();
        if (last is null)
        {
            ResultText = LR.Action_Status_NotRun;
            ResultTimeText = "";
            IsResultNone = true;
            IsResultSuccess = IsResultPartial = IsResultFailed = IsResultSkipped = false;
            return;
        }

        IsResultNone = false;
        IsResultSuccess = last.Status == ActionRunStatus.Success;
        IsResultPartial = last.Status == ActionRunStatus.Partial;
        IsResultFailed = last.Status == ActionRunStatus.Failed;
        IsResultSkipped = last.Status == ActionRunStatus.Skipped;
        ResultText = ActionTextFormatter.DescribeRunStatus(last.Status);
        ResultTimeText = ActionViewText.DescribeWhen(last.StartedAt, now);
    }

    private static string DescribeRunDetail(ActionRun run)
    {
        if (run.Steps.Count == 0)
            return string.IsNullOrEmpty(run.Title) ? "" : run.Title;

        var parts = new List<string>(run.Steps.Count);
        foreach (var step in run.Steps)
        {
            var name = $"{ActionTextFormatter.DescribeTarget(step.TargetId)} · " +
                       ActionTextFormatter.DescribeOperation(step.Operation);
            var status = ActionTextFormatter.DescribeRunStatus(step.Status);
            parts.Add(string.IsNullOrEmpty(step.Message) ? $"{name} · {status}" : $"{name} · {status} · {step.Message}");
        }

        return string.Join("；", parts);
    }

    private static string DescribeTargets(IEnumerable<string> targetIds)
    {
        var names = targetIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Select(ActionTextFormatter.DescribeTarget)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        return string.Join(" / ", names);
    }

    private static string KindTitle(ScheduleItemKind kind) => kind switch
    {
        ScheduleItemKind.DisplayAutoShow => LR.Action_System_DisplayAutoShow_Title,
        ScheduleItemKind.ExamAutoShow => LR.Action_System_ExamAutoShow_Title,
        _ => LR.Action_Group_System
    };

    private static string DefaultSettingsPageId(ScheduleItemKind kind) => kind switch
    {
        ScheduleItemKind.DisplayAutoShow => DisplayScheduleProjection.SettingsPageId,
        ScheduleItemKind.ExamAutoShow => ExamScheduleProjection.SettingsPageId,
        _ => ""
    };
}

/// <summary>「最近结果」胶囊点开后要显示的一条运行记录。</summary>
public sealed class ActionRunViewItem
{
    public string StatusText { get; init; } = "";

    public string TimeText { get; init; } = "";

    public string SourceText { get; init; } = "";

    public string DetailText { get; init; } = "";

    public bool IsSuccess { get; init; }

    public bool IsPartial { get; init; }

    public bool IsFailed { get; init; }

    public bool IsSkipped { get; init; }
}

/// <summary>周视图的一列（一天）。</summary>
public sealed class ActionWeekColumn
{
    public DateOnly Date { get; init; }

    /// <summary>「周一」。</summary>
    public string HeaderText { get; init; } = "";

    /// <summary>「11/09」。</summary>
    public string DayText { get; init; } = "";

    public bool IsToday { get; init; }

    public ObservableCollection<ActionOccurrenceItem> Items { get; } = [];
}

/// <summary>月视图日格里的日程 chip（我的行动 / 考试自动放映 / 看板模式）。</summary>
public sealed class ActionMonthChip
{
    /// <summary>「07:40 隐藏作业板」。</summary>
    public string Text { get; init; } = "";

    public bool IsMine { get; init; }

    public bool IsExam { get; init; }

    public bool IsDisplay { get; init; }

    /// <summary>可点开的那一条（点事件 → 编辑器；系统日程 → 详情）。</summary>
    public ActionOccurrenceItem? Item { get; init; }
}

/// <summary>月视图的一个日格。</summary>
public sealed partial class ActionMonthCell : ObservableObject
{
    public DateOnly Date { get; init; }

    /// <summary>日期数字（跨月补齐的格子也写数字，靠降透明度区分）。</summary>
    public string DayText { get; init; } = "";

    public bool IsCurrentMonth { get; init; }

    public bool IsToday { get; init; }

    /// <summary>当天的日程条数（点事件 + 与当天有交集的区间，跨夜两天各计一次）。</summary>
    public int Count { get; init; }

    /// <summary>日格里最多显示 3 条 chip，多出来的写「+N」。</summary>
    public IReadOnlyList<ActionMonthChip> Chips { get; private set; } = [];

    /// <summary>装不下的条数（0 表示全显示出来了）。</summary>
    public int HiddenCount { get; private set; }

    public bool HasHidden => HiddenCount > 0;

    public string MoreText => HiddenCount <= 0
        ? ""
        : string.Format(CultureInfo.CurrentCulture, LR.Action_More, HiddenCount);

    public bool HasChips => Chips.Count > 0;

    /// <summary>0 条留白、超过 9 条写「9+」。</summary>
    public string CountText => Count <= 0 ? "" : Count > 9 ? "9+" : Count.ToString(CultureInfo.InvariantCulture);

    [ObservableProperty] private bool _isSelected;

    public void SetChips(IReadOnlyList<ActionMonthChip> chips, int hidden)
    {
        Chips = chips;
        HiddenCount = hidden;
        OnPropertyChanged(nameof(Chips));
        OnPropertyChanged(nameof(HasChips));
        OnPropertyChanged(nameof(HasHidden));
        OnPropertyChanged(nameof(MoreText));
    }
}
