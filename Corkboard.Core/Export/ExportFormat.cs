namespace Corkboard.Core.Export;

/// <summary>
///     作业板导出格式。
///     <para>
///         与仓库其它配置枚举一样<b>按数字落盘</b>（<c>settings.json</c> 里存的是 0 / 1），
///         所以只能在末尾追加成员，不要调换顺序。
///     </para>
///     <para>
///         数值 1 曾经是 PDF（导出 PDF 已按用户要求删掉，换成表格）；这里沿用同一个数值，
///         老配置里写着 <c>"default_format": 1</c> 的读出来就是「表格」。
///     </para>
/// </summary>
public enum ExportFormat
{
    /// <summary>导出成一张图片（PNG）。</summary>
    Image = 0,

    /// <summary>导出成一张表格（.xlsx，一行一条作业）。</summary>
    Table = 1
}
