using Corkboard.Core.Models.Board;

namespace Corkboard.Core.Services.Board;

/// <summary>
///     「哪些作业算过期」的纯判定：不碰磁盘、不看界面，连「今天」都由调用方传进来，
///     所以能直接对着日期单测。
///     <para>
///         只有一条规则：<b>过了截止日期才算过期</b>。没填截止日期的作业永远不会被自动清掉——
///         板子上本来就是些「做完就删」的便签，没有期限的那些不该被时间顺手拿走。
///     </para>
///     <para>
///         判据与作业条上那行「过期 N 天」同源（<see cref="BoardDueDateFormatter.IsOverdue" />）：
///         界面说「已过期」的，清理才会动手；到期当天还不算过期，第二天才算。
///     </para>
/// </summary>
public static class BoardExpiryPolicy
{
    /// <summary>这条作业哪一天开始算过期，也就是最早能被清掉的那一天；没填截止日期就是 <c>null</c>。</summary>
    public static DateOnly? ExpiryDate(BoardNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return note.DueDate;
    }

    /// <summary>今天是不是已经过了这条作业的截止日期。</summary>
    public static bool IsExpired(BoardNote note, DateOnly today)
        => ExpiryDate(note) is { } due && BoardDueDateFormatter.IsOverdue(due, today);
}
