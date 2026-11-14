using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Corkboard.Core.Notes;

namespace Corkboard.Core.ControlCenter;

/// <summary>
///     把作业板上的作业压成一份**只读快照**，作为集控 <c>homework.read</c> 命令的回执内容
///     （<c>cmd.result.data</c>）发给网页控制台。
///     <para>
///         这是纯逻辑：不碰 IO、不碰 DI、不依赖 Avalonia，方便单测。集合的线程安全由调用方负责——
///         App 层在 UI 线程上取一份 <see cref="IBoardService" /> 的拷贝再传进来。
///     </para>
///     <para>
///         形状（字段名冻结，服务端与网页按它渲染）：
///         <code>
///         {
///           "generatedAt": "...", "truncated": false, "noteCount": 12,
///           "subjects": [ { "subject": "语文", "items": [
///               { "id": "...", "section": "第一单元"|null, "content": "...",
///                 "dueDate": "2026-11-15"|null, "handwriting": false, "createdAt": "..." } ] } ]
///         }
///         </code>
///     </para>
///     <para>
///         为什么要截断：一节课的作业通常只有十几条，但故障时（或用户把整个学期都堆在板子上）
///         回执可能涨到几百 KB，而单帧上限只有 64 KiB。所以宁可少给、并明说「不全」，
///         也不能让整条连接因为一帧太大被服务端打断。
///     </para>
/// </summary>
public static class ControlHomeworkSnapshot
{
    /// <summary>最多回多少个科目分组。</summary>
    public const int MaxSubjects = 20;

    /// <summary>每个科目最多回多少条作业。</summary>
    public const int MaxItemsPerSubject = 30;

    /// <summary>整份快照最多回多少条作业。</summary>
    public const int MaxItemsTotal = 120;

    /// <summary>单条作业正文最多回多少个字符（超出截断并补省略号）。</summary>
    public const int MaxContentLength = 160;

    /// <summary>
    ///     生成快照。
    /// </summary>
    /// <param name="notes">作业板上的全部作业（已清理的会被本方法滤掉）。</param>
    /// <param name="subjects">设置里配置的科目清单（决定没有手动顺序时的分组先后）。</param>
    /// <param name="subjectOrder">用户拖动排序存下的科目先后。</param>
    /// <param name="now">生成时刻，写进 <c>generatedAt</c>。</param>
    public static JsonObject Build(
        IReadOnlyList<Note> notes,
        IEnumerable<string> subjects,
        IEnumerable<string> subjectOrder,
        DateTimeOffset now)
    {
        // 只回还在板子上的作业：已清理的那些用户自己也看不见，没必要送到网页上。
        var onBoard = notes.Where(note => note.IsOnBoard).ToList();

        // 科目顺序：手动顺序 → 设置里的科目表 → 只在作业里出现的科目（按首次出现）。
        var orderedSubjects = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var subject in subjectOrder)
        {
            var name = (subject ?? string.Empty).Trim();
            if (seen.Add(name))
                orderedSubjects.Add(name);
        }

        foreach (var subject in subjects)
        {
            var name = (subject ?? string.Empty).Trim();
            if (seen.Add(name))
                orderedSubjects.Add(name);
        }

        var bySubject = new Dictionary<string, List<Note>>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in onBoard)
        {
            var key = (note.Subject ?? string.Empty).Trim();
            if (!bySubject.TryGetValue(key, out var list))
            {
                list = [];
                bySubject[key] = list;
            }

            list.Add(note);

            if (seen.Add(key))
                orderedSubjects.Add(key);
        }

        var payloadSubjects = new JsonArray();
        var noteCount = 0;
        var truncated = false;

        foreach (var subject in orderedSubjects)
        {
            if (!bySubject.TryGetValue(subject, out var group) || group.Count == 0)
                continue;

            if (payloadSubjects.Count >= MaxSubjects || noteCount >= MaxItemsTotal)
            {
                truncated = true;
                break;
            }

            var items = new JsonArray();
            foreach (var note in group.OrderBy(n => n.DueDate is null).ThenBy(n => n.DueDate).ThenByDescending(n => n.CreatedAt))
            {
                if (items.Count >= MaxItemsPerSubject || noteCount >= MaxItemsTotal)
                {
                    truncated = true;
                    break;
                }

                items.Add(BuildItem(note));
                noteCount++;
            }

            payloadSubjects.Add(new JsonObject
            {
                ["subject"] = subject,
                ["items"] = items
            });
        }

        return new JsonObject
        {
            ["generatedAt"] = now.ToString("O", CultureInfo.InvariantCulture),
            ["noteCount"] = noteCount,
            ["truncated"] = truncated,
            ["subjects"] = payloadSubjects
        };
    }

    /// <summary>单条作业：正文归一化成一行、截断，日期按日期格式给。</summary>
    private static JsonObject BuildItem(Note note)
    {
        return new JsonObject
        {
            ["id"] = note.Id.ToString("D"),
            ["section"] = string.IsNullOrWhiteSpace(note.Section) ? null : note.Section.Trim(),
            ["content"] = NormalizeContent(note.Content),
            ["dueDate"] = note.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["handwriting"] = !string.IsNullOrWhiteSpace(note.Handwriting),
            ["createdAt"] = note.CreatedAt.ToString("O", CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    ///     正文压成一行：所有空白（含换行、制表）折成一个空格并去掉首尾空白——
    ///     网页那边是按行渲染的列表，正文里带换行会把版式撑坏。超过上限就截断补省略号。
    /// </summary>
    private static string NormalizeContent(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        var builder = new StringBuilder(content.Length);
        var lastWasSpace = false;

        foreach (var ch in content)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            builder.Append(ch);
            lastWasSpace = false;
        }

        while (builder.Length > 0 && builder[^1] == ' ')
            builder.Length--;

        var text = builder.ToString();
        return text.Length <= MaxContentLength ? text : text[..MaxContentLength] + "…";
    }
}