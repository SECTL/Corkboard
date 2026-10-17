using Corkboard.Core.Notes;

namespace Corkboard.Core.Homework;

/// <summary>
///     「哪些作业算过期」的纯判定：不碰磁盘、不看界面，连「今天」都由调用方传进来，
///     所以能直接对着日期单测。
///     <para>
///         只有一条规则：<b>过了截止日期才算过期</b>——截止当天不算，要等第二天。
///         用户明确要求「今天截止的作业今天不应该清理」，所以截止日期那天的清理时刻到了也不动它，
///         到第二天清理时刻才收走。
///         没填截止日期的作业永远不会被自动清掉——板子上本来就是些「做完就删」的便签，
///         没有期限的那些不该被时间顺手拿走。
///     </para>
///     <para>
///         这条判据与界面上那行「今天截止 / 过期 N 天」现在<b>完全一致</b>
///         （<see cref="HomeworkDueDateFormatter.IsOverdue" /> 也是当天不算过期）：
///         卡片写着「今天截止」的作业不会再被当天的清理顺手收走。
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

    /// <summary>截止日期过了没有（截止当天还不算，第二天才算）。</summary>
    public static bool IsExpired(Note note, DateOnly today)
        => ExpiryDate(note) is { } due && due < today;
}
