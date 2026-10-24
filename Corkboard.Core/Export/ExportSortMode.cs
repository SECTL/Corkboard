using Corkboard.Core.Notes;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Core.Export;

/// <summary>
///     导出时的排序方式。
///     <para>
///         与仓库其它配置枚举一样<b>按数字落盘</b>，只能在末尾追加成员。
///         成员与旧 PDF 排版那一套（<c>PdfSortMode</c>）逐个对应、数值也一致，
///         所以老配置里记着的 <c>pdf_sort_mode</c> 换个键名就能直接读。
///     </para>
/// </summary>
public enum ExportSortMode
{
    /// <summary>跟随板子：调用方给什么顺序就按什么顺序（板面顺序）。</summary>
    FollowBoard = 0,

    /// <summary>创建时间新 → 旧。</summary>
    CreatedDescending = 1,

    /// <summary>创建时间旧 → 新。</summary>
    CreatedAscending = 2,

    /// <summary>修改时间新 → 旧。</summary>
    UpdatedDescending = 3,

    /// <summary>按科目名排（同科目内保持原顺序）。</summary>
    Subject = 4,

    /// <summary>按手动排序序号（<see cref="Note.Order" />）排，同序号再按创建时间。</summary>
    Fixed = 5
}

/// <summary>
///     导出排序的唯一实现：表格导出与（保留下来的）图片导出都走这里，别再写第二套。
///     <para>
///         纯函数：输入顺序 + 排序方式，输出排好的清单；不读配置、不碰时钟。
///         <see cref="ExportSortMode.FollowBoard" /> 原样返回输入顺序，
///         所以「板面顺序」由调用方决定（页面按当前排布与手动排序给）。
///     </para>
/// </summary>
public static class ExportNoteSorter
{
    /// <summary>按 <paramref name="mode" /> 排序；排序是稳定的，同键元素保持输入顺序。</summary>
    public static IReadOnlyList<Note> Sort(IEnumerable<Note> notes, ExportSortMode mode)
    {
        ArgumentNullException.ThrowIfNull(notes);

        IEnumerable<Note> query = notes;
        query = mode switch
        {
            ExportSortMode.CreatedDescending => query.OrderByDescending(note => note.CreatedAt),
            ExportSortMode.CreatedAscending => query.OrderBy(note => note.CreatedAt),
            ExportSortMode.UpdatedDescending => query.OrderByDescending(note => note.UpdatedAt),
            ExportSortMode.Fixed => query.OrderBy(note => note.Order).ThenBy(note => note.CreatedAt),

            // 按「界面上看到的名字」排：没填科目的那条显示成「未分类」，就该按「未」字参与排序，
            // 而不是当成空串一律排到最前面（用户看到的是名字，不是空串）。
            ExportSortMode.Subject => query
                .OrderBy(note => SubjectDisplayName(note.Subject), StringComparer.CurrentCulture),

            _ => query
        };

        return query.ToList();
    }

    /// <summary>科目显示名：没填科目的作业用「未分类」那条资源文案。</summary>
    public static string SubjectDisplayName(string? subject)
    {
        var trimmed = subject?.Trim();
        return string.IsNullOrEmpty(trimmed) ? CR.Export_Table_NoSubject : trimmed;
    }
}
