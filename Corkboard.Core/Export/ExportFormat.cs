namespace Corkboard.Core.Export;

/// <summary>
///     作业板导出格式。
///     <para>
///         与仓库其它配置枚举一样<b>按数字落盘</b>（<c>settings.json</c> 里存的是 0 / 1），
///         所以只能在末尾追加成员，不要调换顺序。
///     </para>
/// </summary>
public enum ExportFormat
{
    /// <summary>导出成一张图片（PNG）。</summary>
    Image = 0,

    /// <summary>导出成一个 PDF 文件（每页一张 JPEG，科目当书签）。</summary>
    Pdf = 1
}
