using Corkboard.Core.Notes;

namespace Corkboard.Core.Homework;

/// <summary>
///     「哪些作业算过期」的纯判定：不碰磁盘、不看界面，连「今天」都由调用方传进来，
///     所以能直接对着日期单测。
///     <para>
///         只有一条规则：<b>到了截止日期就算过期</b>——当天就算，不是第二天。到了那天的清理时刻，
///         <see cref="HomeworkCleanupService" /> 就把它从板子上收走（用户的原话是「截止时刻到了之后，
///         它就算没了」，所以默认「明天截止」的作业会在明天那个时刻消失）。
///         没填截止日期的作业永远不会被自动清掉——板子上本来就是些「做完就删」的便签，
///         没有期限的那些不该被时间顺手拿走。
///     </para>
///     <para>
///         ⚠️ 这里比界面上那行「今天截止 / 过期 N 天」<b>早一天</b>：界面只在过了当天之后才说「已过期」
///         （<see cref="HomeworkDueDateFormatter.IsOverdue" />），当天仍说「今天截止」，
///         免得用户看到「过期」两个字却发现期限还没过。两条判据故意不同，
///         方向必须一致（界面说已过期的，清理一定也认为过期），改一条要一起想清楚。
///     </para>
/// </summary>
public static class HomeworkExpiryPolicy
{
    /// <summary>这条作业哪一天开始算过期，也就是最早能被清掉的那一天；没填截止日期就是 <c>null</c>。</summary>
    public static DateOnly? ExpiryDate(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return note.DueDate;
    }

    /// <summary>截止日期到了没有（当天就算到）。</summary>
    public static bool IsExpired(Note note, DateOnly today)
        => ExpiryDate(note) is { } due && due <= today;
}
