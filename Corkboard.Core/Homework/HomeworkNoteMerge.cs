using System.Text.RegularExpressions;
using Corkboard.Core.Board;
using Corkboard.Core.Notes;

namespace Corkboard.Core.Homework;

/// <summary>融合的结果：并集之后的作业，以及各自是从哪一边来的。</summary>
/// <param name="Notes">合并后的作业，按创建时间排序（和 <see cref="NoteStore.LoadAll" /> 一个顺序）。</param>
/// <param name="AddedFromRemote">本机没有、从云端补进来的条数。</param>
/// <param name="UpdatedFromRemote">两边都有、云端那份更新因而被采纳的条数。</param>
/// <param name="KeptLocal">两边都有、本机那份不旧所以留下的条数。</param>
/// <param name="RemovedDuplicates">内容一模一样的重复作业（Id 不同）被合并掉的条数。</param>
public sealed record HomeworkNoteMergeResult(
    IReadOnlyList<Note> Notes,
    int AddedFromRemote,
    int UpdatedFromRemote,
    int KeptLocal,
    int RemovedDuplicates = 0)
{
    /// <summary>这次融合有没有从云端拿东西进来。</summary>
    public bool TookAnythingFromRemote => AddedFromRemote > 0 || UpdatedFromRemote > 0;
}

/// <summary>
///     云端与本机都有改动时的作业融合：**并集**。
///     <para>
///         同一条作业（同一个 <see cref="Note.Id" />）两边都改过时，取 <see cref="Note.UpdatedAt" />
///         更新的那一份，整条替换、不逐字段拼——作业的字段是一起编辑的（内容、科目、截止日期同一次改），
///         逐字段拼出来的是谁也不认识的一条。清理标记 <see cref="Note.CleanedAt" /> 跟着这一条一起走，
///         所以「一边自动清理、一边还在板子上」也能合对。
///     </para>
///     <para>
///         <b>Id 不同的「同一条作业」也会合掉。</b>两边各建过一次同一份作业时，Id 是各自生成的，
///         光按 Id 取并集会在板子上留成两条一模一样的卡片。作业本来就没有名字，「科目 + 截止日期 +
///         内容」就是它的身份，同一天里这三样都一样就当成同一条，只留 <see cref="Note.UpdatedAt" />
///         更新的那份（条数记在 <see cref="HomeworkNoteMergeResult.RemovedDuplicates" /> 里）。
///     </para>
///     <para>
///         <b>并集不认识手动删除。</b>本机删掉的作业如果云端那份还在，融合会把它带回来：数据里没有「这条被删了」
///         的墓碑（<c>BoardService.Remove</c> 是直接删掉），只看两边的内容分不出「云端更新的」和「本机刚删的」。
///         要彻底解决得给删除留墓碑、还得处理墓碑什么时候能回收；冲突时把作业找回来比悄悄丢一条安全，
///         所以先按并集来。
///     </para>
/// </summary>
public static class HomeworkNoteMerge
{
    /// <summary>
    ///     把两边的作业按 <see cref="Note.Id" /> 取并集（同一 Id 取更新的一份），
    ///     再把 Id 不同但内容一样的重复作业合掉。
    /// </summary>
    public static HomeworkNoteMergeResult Merge(IEnumerable<Note> local, IEnumerable<Note> remote)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);

        var merged = new List<Note>();
        var positions = new Dictionary<Guid, int>();

        foreach (var note in local)
        {
            // 本机这一侧自己也可能带重复：历史数据里同一个 Id 有可能落在两个日期文件里
            // （`NoteStore.LoadAll` 是按文件读的，不按 Id 去重）。同一个 Id 只留更新的一份，
            // 否则并集会把它原样复制一份到云端，再同步回来就越滚越多。
            if (positions.TryGetValue(note.Id, out var position))
            {
                if (note.UpdatedAt > merged[position].UpdatedAt)
                    merged[position] = note;

                continue;
            }

            positions[note.Id] = merged.Count;
            merged.Add(note);
        }

        var added = 0;
        var updated = 0;
        var kept = 0;

        foreach (var candidate in remote)
        {
            if (!positions.TryGetValue(candidate.Id, out var position))
            {
                positions[candidate.Id] = merged.Count;
                merged.Add(candidate);
                added++;
                continue;
            }

            if (candidate.UpdatedAt > merged[position].UpdatedAt)
            {
                merged[position] = candidate;
                updated++;
            }
            else
            {
                kept++;
            }
        }

        // 同一条作业两边各建过一次时 Id 是不一样的（同步本身不会改 Id，是各写各的），
        // 光按 Id 取并集就会把「一模一样的作业」在板子上留成两条。作业本来就没有名字，
        // 说清一份作业的就是「科目 + 截止日期 + 内容」（见 Note 的注释），同一天里
        // 这三样都一样就当成同一条，只留更新的一份。
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var deduped = new List<Note>(merged.Count);
        var removed = 0;
        foreach (var note in merged)
        {
            var key = DuplicateKey(note);
            if (seen.TryGetValue(key, out var index))
            {
                removed++;
                if (note.UpdatedAt > deduped[index].UpdatedAt)
                    deduped[index] = note;

                continue;
            }

            seen[key] = deduped.Count;
            deduped.Add(note);
        }

        // 稳定排序：创建时间相同的作业保持本机原来的先后，免得每次融合都把顺序洗一遍。
        var ordered = deduped.OrderBy(note => note.CreatedAt).ToList();
        return new HomeworkNoteMergeResult(ordered, added, updated, kept, removed);
    }

    /// <summary>「同一条作业」的判据：科目 + 内容（空白归一）+ 截止日期 + 清理状态 + 创建那天。</summary>
    private static string DuplicateKey(Note note)
    {
        var day = note.CreatedAt.ToLocalTime().Date;
        return string.Join(
            '\u001f',
            note.Subject,
            NormalizeContent(note.Content),
            note.DueDate?.ToString("yyyy-MM-dd") ?? string.Empty,
            note.CleanedAt is null ? "0" : "1",
            day.ToString("yyyy-MM-dd"));
    }

    /// <summary>富文本片段里的换行与缩进不该影响判重，比较前先压成单个空格。</summary>
    private static string NormalizeContent(string content) =>
        Whitespace.Replace(content, " ").Trim();

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
}
