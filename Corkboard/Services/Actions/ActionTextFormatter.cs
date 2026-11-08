using System.Globalization;
using System.Resources;
using Corkboard.Core.Actions;

namespace Corkboard.Services.Actions;

/// <summary>
///     行动域的文案拼装：规则摘要、触发 / 条件描述、状态与来源名，以及执行记录里的短文案。
///     <para>
///         全部是静态纯函数，不碰界面状态；文案取自 <c>Corkboard/Langs/Action</c> 的资源
///         （运行时按 <c>Corkboard.Langs.Action.Resources</c> 取，资源缺失时回退到中文兜底，
///         所以编译期不依赖 ui 队友的手写 Designer 类型）。
///     </para>
///     <para>
///         时间格式只在这里拼：界面上的「每天 08:00」「2026-11-07 08:00」到处各写一套很快就会不一致。
///     </para>
/// </summary>
public static class ActionTextFormatter
{
    /// <summary>「未执行」的状态名（执行记录还没产生时显示它）。</summary>
    public static string NotRun => ActionResources.Get("Action_Status_NotRun", "未执行");

    /// <summary>规则摘要：有名字用名字，没名字就按步骤临时拼一个。</summary>
    public static string Summarize(ActionRule? rule)
    {
        if (rule is null)
            return ActionResources.Get("Action_Editor_Unnamed", "未命名行动");

        if (!string.IsNullOrWhiteSpace(rule.Name))
            return rule.Name.Trim();

        var steps = DescribeSteps(rule.Steps);
        return string.IsNullOrEmpty(steps)
            ? ActionResources.Get("Action_Editor_Unnamed", "未命名行动")
            : steps;
    }

    /// <summary>触发方式描述（不含过期判断）。</summary>
    public static string DescribeTrigger(ActionTrigger? trigger)
    {
        if (trigger is null)
            return ActionResources.Get("Action_Schedule_Once", "单次");

        return trigger.Kind switch
        {
            ActionTriggerKind.Daily =>
                $"{ActionResources.Get("Action_Schedule_Daily", "每天")} {trigger.Time:HH\\:mm}",
            ActionTriggerKind.Weekly =>
                $"{ActionResources.Get("Action_Schedule_Weekly", "每周")}{DescribeDays(trigger.Days)} {trigger.Time:HH\\:mm}",
            _ => trigger.Date is { } date
                ? $"{date:yyyy-MM-dd} {trigger.Time:HH\\:mm}"
                : $"{ActionResources.Get("Action_Schedule_Once", "单次")} {trigger.Time:HH\\:mm}"
        };
    }

    /// <summary>触发方式描述，单次规则已经过了发生点就补一句「已过期」。</summary>
    public static string DescribeTrigger(ActionTrigger? trigger, DateTime now)
    {
        var text = DescribeTrigger(trigger);
        if (trigger is not { Kind: ActionTriggerKind.Once, Date: { } date })
            return text;

        var at = date.ToDateTime(trigger.Time);
        return at < now
            ? $"{text} · {ActionResources.Get("Action_Expired", "已过期")}"
            : text;
    }

    /// <summary>星期描述（「一三五」这种，周一到周日顺序）。</summary>
    public static string DescribeDays(ActionDayFlags days)
    {
        if (days == ActionDayFlags.None)
            return string.Empty;

        if ((days & ActionDayFlags.EveryDay) == ActionDayFlags.EveryDay)
            return string.Empty;

        var parts = new List<string>(7);
        AppendDay(parts, days, ActionDayFlags.Monday, "Action_Weekday_Monday", "一");
        AppendDay(parts, days, ActionDayFlags.Tuesday, "Action_Weekday_Tuesday", "二");
        AppendDay(parts, days, ActionDayFlags.Wednesday, "Action_Weekday_Wednesday", "三");
        AppendDay(parts, days, ActionDayFlags.Thursday, "Action_Weekday_Thursday", "四");
        AppendDay(parts, days, ActionDayFlags.Friday, "Action_Weekday_Friday", "五");
        AppendDay(parts, days, ActionDayFlags.Saturday, "Action_Weekday_Saturday", "六");
        AppendDay(parts, days, ActionDayFlags.Sunday, "Action_Weekday_Sunday", "日");

        return parts.Count == 0 ? string.Empty : string.Concat(parts);
    }

    private static void AppendDay(List<string> parts, ActionDayFlags days, ActionDayFlags day, string key, string fallback)
    {
        if ((days & day) != 0)
            parts.Add(ActionResources.Get(key, fallback));
    }

    /// <summary>条件描述：日期范围（两端可为空 = 不限）；没有条件返回空串。</summary>
    public static string DescribeCondition(ActionCondition? condition)
    {
        if (condition is null || condition.Kind != ActionConditionKind.DateRange)
            return string.Empty;

        var prefix = ActionResources.Get("Action_Condition_DateRange", "生效");
        var start = condition.Start;
        var end = condition.End;
        if (start is null && end is null)
            return string.Empty;

        if (start is { } from && end is { } to)
            return $"{prefix} {from:yyyy-MM-dd} – {to:yyyy-MM-dd}";

        if (start is { } onlyStart)
            return $"{prefix} {onlyStart:yyyy-MM-dd} " + (ActionResources.IsChinese ? "起" : "onwards");

        return $"{prefix} " + (ActionResources.IsChinese ? "至 " : "until ") + $"{end:yyyy-MM-dd}";
    }

    /// <summary>操作名（显示 / 隐藏）。</summary>
    public static string DescribeOperation(BoardOperation operation) =>
        operation == BoardOperation.Hide
            ? ActionResources.Get("Action_Operation_Hide", "隐藏")
            : ActionResources.Get("Action_Operation_Show", "显示");

    /// <summary>目标名：按冻结的看板标识取本地化名；认不出就显示「未知板子」。</summary>
    public static string DescribeTarget(string? targetId) => targetId switch
    {
        BoardTargetIds.Homework or BoardTargetIds.HomeWork =>
            ActionResources.Get("Action_Target_Homework", "作业板"),
        BoardTargetIds.ExamBoard =>
            ActionResources.Get("Action_Target_Exam", "考试看板"),
        BoardTargetIds.DisplayBoard =>
            ActionResources.Get("Action_Target_Display", "看板模式"),
        _ => ActionResources.Get("Action_Target_Unknown", "未知板子")
    };

    /// <summary>目标名：实现方给什么用什么（<see cref="IBoardTarget.DisplayName" />）。</summary>
    public static string DescribeTarget(IBoardTarget? target) =>
        target is null ? DescribeTarget((string?)null) : target.DisplayName;

    /// <summary>一个步骤的短描述：「显示考试看板」。</summary>
    public static string DescribeStep(ActionStep? step) =>
        step is null ? string.Empty : DescribeOperation(step.Operation) + DescribeTarget(step.TargetId);

    /// <summary>多个步骤的短描述，用顿号（英文用逗号）连接。</summary>
    public static string DescribeSteps(IReadOnlyList<ActionStep>? steps)
    {
        if (steps is null || steps.Count == 0)
            return string.Empty;

        var separator = ActionResources.IsChinese ? "、" : ", ";
        return string.Join(separator, steps.Select(DescribeStep));
    }

    /// <summary>执行状态名。</summary>
    public static string DescribeRunStatus(ActionRunStatus status) => status switch
    {
        ActionRunStatus.Success => ActionResources.Get("Action_Status_Success", "成功"),
        ActionRunStatus.Partial => ActionResources.Get("Action_Status_Partial", "部分完成"),
        ActionRunStatus.Failed => ActionResources.Get("Action_Status_Failed", "失败"),
        ActionRunStatus.Skipped => ActionResources.Get("Action_Status_Skipped", "跳过"),
        _ => NotRun
    };

    /// <summary>触发来源（自动 / 手动）。</summary>
    public static string DescribeTriggeredBy(ActionRunTrigger trigger) =>
        trigger == ActionRunTrigger.Manual
            ? ActionResources.Get("Action_Source_Manual", "手动")
            : ActionResources.Get("Action_Source_Schedule", "自动");

    /// <summary>执行记录的来源种类名。</summary>
    public static string DescribeSourceKind(ActionRunSourceKind kind) => kind switch
    {
        ActionRunSourceKind.ExamAutoShow => ActionResources.Get("Action_SourceKind_ExamAutoShow", "考试自动放映"),
        ActionRunSourceKind.DisplayAutoShow => ActionResources.Get("Action_SourceKind_DisplayAutoShow", "看板模式"),
        _ => ActionResources.Get("Action_SourceKind_CustomRule", "我的行动")
    };

    /// <summary>时刻格式（24 小时、分钟级）。</summary>
    public static string DescribeClock(DateTime at) => at.ToString("HH:mm", CultureInfo.CurrentCulture);

    /// <summary>区间格式：「08:00 – 09:30」；结束为空只给开始时刻。</summary>
    public static string DescribeInterval(DateTime start, DateTime? end) =>
        end is { } endAt
            ? $"{DescribeClock(start)} – {DescribeClock(endAt)}"
            : DescribeClock(start);
}

/// <summary>
///     行动文案的资源读取器。
///     <para>
///         刻意<b>不</b>在编译期引用 <c>Corkboard.Langs.Action.Resources</c> 类型：那份 Designer 由界面侧手写维护，
///         行动服务层不该因为它改一次键就编译不过。键缺失时回退到中文兜底，界面看到的是可用文案而不是空串。
///     </para>
/// </summary>
internal static class ActionResources
{
    private const string BaseName = "Corkboard.Langs.Action.Resources";

    private static readonly ResourceManager Manager = new(BaseName, typeof(ActionResources).Assembly);

    private static readonly bool Chinese =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    /// <summary>当前界面语言是不是中文（拼连接符 / 语序时用）。</summary>
    public static bool IsChinese => Chinese;

    /// <summary>取一条文案；资源缺失或读取失败时用 <paramref name="fallback" />。</summary>
    public static string Get(string key, string fallback)
    {
        try
        {
            return Manager.GetString(key, CultureInfo.CurrentUICulture) ?? fallback;
        }
        catch (Exception)
        {
            // 资源读不出来不该让执行路径抛异常（键名改了、resx 没打进包都不至于崩）。
            return fallback;
        }
    }
}
