using Corkboard.Core.Notes;

namespace Corkboard.Core.Board;

/// <summary>
///     先按设置排序，再合并同名科目；「手动排序」的科目顺序由 <paramref name="subjectOrder" /> 决定，
///     「按科目排序」的科目顺序由设置里的科目表 <paramref name="subjects" /> 决定，
///     其余排序方式仍由排序后首次出现的位置决定。
/// </summary>
public static class NoteGrouping
{
    /// <param name="subjectOrder">
    ///     手动排序的科目先后（<c>BoardService.SubjectOrder</c>）。只有
    ///     <see cref="BoardSortMode.Manual" /> 会用到，其它排序方式忽略它。
    ///     不在表里的科目排在最后，彼此保持首次出现的相对顺序。
    /// </param>
    /// <param name="subjects">
    ///     设置里配置的科目表（<c>BoardService.Subjects</c>，即作业板设置里的「科目」）。
    ///     只有 <see cref="BoardSortMode.Subject" /> 会用到：按表里的先后排，
    ///     不在表里的科目（含「未分类」）排在最后，彼此按名称先后。表为空时退回按名称排。
    /// </param>
    public static IReadOnlyList<IGrouping<string, Note>> Group(
        IEnumerable<Note> notes, BoardSortMode sortMode,
        IReadOnlyList<string>? subjectOrder = null, IReadOnlyList<string>? subjects = null)
    {
        var sorted = sortMode switch
        {
            BoardSortMode.CreatedAscending => notes.OrderBy(note => note.CreatedAt),
            BoardSortMode.UpdatedDescending => notes.OrderByDescending(note => note.UpdatedAt),
            BoardSortMode.Subject => OrderByConfiguredSubject(notes, subjects),

            // 手动：区块顺序交给落盘的科目表，区块内保持加入顺序（Order 由 BoardService.Add 递增）。
            BoardSortMode.Manual => notes.OrderBy(note => note.Order).ThenBy(note => note.CreatedAt),
            _ => notes.OrderByDescending(note => note.CreatedAt)
        };

        var groups = sorted.GroupBy(note => note.Subject.Trim(), StringComparer.Ordinal).ToList();
        if (sortMode != BoardSortMode.Manual || subjectOrder is not { Count: > 0 })
        {
            return groups;
        }

        // OrderBy 是稳定排序：没记进顺序表的科目会整体落到后面，并保持原本的相对先后。
        return [.. groups.OrderBy(group => IndexOf(subjectOrder, group.Key))];
    }

    /// <summary>
    ///     在 <see cref="Group" /> 之上再按二级分类拆一层：科目区块的先后、区块内作业的顺序
    ///     都完全沿用 <see cref="Group" /> 的规则，只在科目内部多分一层。
    /// </summary>
    /// <param name="sectionsOf">
    ///     取某个科目在设置里定义的二级分类（按定义先后，见 <c>IBoardService.GetSections</c>）。
    ///     没有归进任何分类的作业（<see cref="Note.Section" /> 为空）单独成一组、名字是空串，
    ///     放在科目区块最前面，由界面决定怎么显示（直接跟在一级标题下，不画二级标题）；
    ///     定义里没有作业的分类不会出现在结果里，不留空组。
    ///     不在定义里的分类（历史数据、已删分类）排在已定义分类后面，彼此按名称。
    /// </param>
    public static IReadOnlyList<NoteSubjectGroup> GroupWithSections(
        IEnumerable<Note> notes, BoardSortMode sortMode,
        IReadOnlyList<string>? subjectOrder = null, IReadOnlyList<string>? subjects = null,
        Func<string, IReadOnlyList<string>>? sectionsOf = null)
    {
        var result = new List<NoteSubjectGroup>();
        foreach (var subjectGroup in Group(notes, sortMode, subjectOrder, subjects))
        {
            // 先按作业的先后收集分类；分组本身保持顺序，等会儿再按定义顺序重排。
            var notesBySection = new Dictionary<string, List<Note>>(StringComparer.Ordinal);
            var appearanceOrder = new List<string>();
            foreach (var note in subjectGroup)
            {
                var name = (note.Section ?? string.Empty).Trim();
                if (!notesBySection.TryGetValue(name, out var sectionNotes))
                {
                    sectionNotes = [];
                    notesBySection[name] = sectionNotes;
                    appearanceOrder.Add(name);
                }

                sectionNotes.Add(note);
            }

            var sections = new List<NoteSectionGroup>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            // 没归类的作业直接贴在科目区块最上面：不画二级标题，也不占一个「未分类」组的空位。
            if (notesBySection.TryGetValue(string.Empty, out var uncategorized))
            {
                sections.Add(new NoteSectionGroup(string.Empty, uncategorized));
                seen.Add(string.Empty);
            }

            // 定义过的分类按定义先后；定义里没有作业的跳过。
            var defined = sectionsOf?.Invoke(subjectGroup.Key);
            if (defined is not null)
            {
                foreach (var rawName in defined)
                {
                    var name = (rawName ?? string.Empty).Trim();
                    if (name.Length == 0 || !seen.Add(name))
                    {
                        continue;
                    }

                    if (notesBySection.TryGetValue(name, out var sectionNotes))
                    {
                        sections.Add(new NoteSectionGroup(name, sectionNotes));
                    }
                }
            }

            // 定义里找不到的分类（历史数据、已删分类）整体落到后面，彼此按名称。
            foreach (var name in appearanceOrder
                         .Where(name => !seen.Contains(name))
                         .OrderBy(name => name, StringComparer.CurrentCulture))
            {
                sections.Add(new NoteSectionGroup(name, notesBySection[name]));
            }

            result.Add(new NoteSubjectGroup(subjectGroup.Key, sections));
        }

        return result;
    }

    // 「按科目排序」按设置里的科目表排：表里的科目依次在前，表外的科目（含「未分类」）整体落到后面、彼此按名称。
    private static IOrderedEnumerable<Note> OrderByConfiguredSubject(
        IEnumerable<Note> notes, IReadOnlyList<string>? subjects)
    {
        if (subjects is not { Count: > 0 })
        {
            return notes.OrderBy(note => note.Subject.Trim(), StringComparer.CurrentCulture);
        }

        return notes.OrderBy(note => IndexOf(subjects, note.Subject.Trim()))
            .ThenBy(note => note.Subject.Trim(), StringComparer.CurrentCulture);
    }

    private static int IndexOf(IReadOnlyList<string> subjectOrder, string subject)
    {
        for (var i = 0; i < subjectOrder.Count; i++)
        {
            if (string.Equals(subjectOrder[i].Trim(), subject, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}

/// <summary>
///     科目区块里的一个二级分类分组。<paramref name="Name" /> 是空串时表示「没有归进分类」，
///     界面上这批作业直接跟在一级标题下面、不画二级标题。
/// </summary>
public sealed record NoteSectionGroup(string Name, IReadOnlyList<Note> Notes)
{
    /// <summary>没有归进任何分类（科目下直接摆放的那批作业）。</summary>
    public bool IsUncategorized => Name.Length == 0;

    public int Count => Notes.Count;
}

/// <summary>
///     一个科目区块：科目名 + 按二级分类拆开的分组。每个分组至少有一条作业，
///     没有作业的科目根本不会出现在结果里（空板逻辑由调用方看集合是否为空）。
/// </summary>
public sealed record NoteSubjectGroup(string Subject, IReadOnlyList<NoteSectionGroup> Sections)
{
    public int Count => Sections.Sum(section => section.Notes.Count);
}
