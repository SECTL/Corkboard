namespace Corkboard.Core.Export;

/// <summary>表格里一行是什么东西。</summary>
public enum ExportTableRowKind
{
    /// <summary>表头行（列名）。</summary>
    Header,

    /// <summary>分组标题行（按科目分块时每组一行，整行合并）。</summary>
    Group,

    /// <summary>一条作业。</summary>
    Note
}

/// <summary>
///     单元格的语义样式。<see cref="XlsxTableWriter" /> 把它翻译成具体的字体 / 底色 / 边框，
///     所以「哪个格子该醒目」由构建器决定、配色由写入器决定，两边不互相污染。
/// </summary>
public enum ExportTableCellStyle
{
    /// <summary>正文（作业行里的普通格子）。</summary>
    Normal,

    /// <summary>次要文字（灰字）：没期限、已清理这类「有但不重要」的状态。</summary>
    Muted,

    /// <summary>强调（加粗）：今天 / 明天截止这种要抓紧的。</summary>
    Emphasis,

    /// <summary>表头：深底白字。</summary>
    Header,

    /// <summary>分组标题：浅灰底、加粗、左对齐。</summary>
    GroupTitle,

    /// <summary>过期：红字加粗。</summary>
    Overdue
}

/// <summary>
///     一个单元格。
/// </summary>
/// <param name="Text">单元格文字。为空串时写一个只有样式的空格子（边框仍然在）。</param>
/// <param name="Style">语义样式，见 <see cref="ExportTableCellStyle" />。</param>
/// <param name="Number">
///     非空时按<b>数值</b>写（不写文字）：日期就是 Excel 的日期序列号，
///     这样单元格是真日期，能排序、能筛选、能改成别的日期格式。
/// </param>
/// <param name="NumberFormat">
///     Excel 数字格式串（如 <c>yyyy-mm-dd</c>、<c>yyyy-mm-dd hh:mm</c>），
///     <see cref="Number" /> 非空时才用得上。
/// </param>
public sealed record ExportTableCell(
    string Text,
    ExportTableCellStyle Style = ExportTableCellStyle.Normal,
    double? Number = null,
    string? NumberFormat = null);

/// <summary>
///     表格里的一行。<see cref="ExportTableRowKind.Group" /> 的行只放一个格子（文案），
///     写入器会把整行合并成一格。
/// </summary>
public sealed record ExportTableRow(ExportTableRowKind Kind, IReadOnlyList<ExportTableCell> Cells);

/// <summary>
///     一份要写成 xlsx 的表格：标题、表头、数据行与列宽。
///     <para>
///         纯数据，不知道 Excel 长什么样——样式全在 <see cref="XlsxTableWriter" /> 里。
///         构建器（<see cref="HomeworkTableBuilder" />）负责「表格里写什么」，
///         写入器负责「写成文件」，中间这一层才好单测。
///     </para>
/// </summary>
public sealed class ExportTable
{
    /// <summary>默认列宽（Excel 的字符宽）：序号 / 科目 / 分类 / 截止日期 / 状态 / 内容 / 标签 / 创建时间。</summary>
    public static readonly IReadOnlyList<double> DefaultColumnWidths = [6, 12, 14, 12, 11, 60, 18, 18];

    /// <summary>Excel 工作表名的长度上限。</summary>
    public const int MaxSheetNameLength = 31;

    /// <summary>工作表名被清洗成空时用的兜底名字。</summary>
    public const string FallbackSheetName = "Sheet1";

    public ExportTable(
        string sheetName,
        IReadOnlyList<string> columns,
        string title,
        string subtitle,
        IReadOnlyList<ExportTableRow> rows,
        IReadOnlyList<double>? columnWidths = null)
    {
        SheetName = SanitizeSheetName(sheetName);
        Columns = columns ?? [];
        Title = title ?? string.Empty;
        Subtitle = subtitle ?? string.Empty;
        Rows = rows ?? [];
        ColumnWidths = columnWidths is { Count: > 0 } widths ? widths : DefaultColumnWidths;
    }

    /// <summary>工作表名（已按 Excel 的规则清洗过，见 <see cref="SanitizeSheetName" />）。</summary>
    public string SheetName { get; }

    /// <summary>表头文案，按列顺序。</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>第 1 行的板名。</summary>
    public string Title { get; }

    /// <summary>第 2 行的副标题（共几条 · 导出时间）。</summary>
    public string Subtitle { get; }

    /// <summary>表头之后的行（分组标题与作业行）。</summary>
    public IReadOnlyList<ExportTableRow> Rows { get; }

    /// <summary>列宽，Excel 字符宽；比 <see cref="Columns" /> 短时缺的列用默认宽度。</summary>
    public IReadOnlyList<double> ColumnWidths { get; }

    /// <summary>表格一共几列（表头列数为准，至少 1）。</summary>
    public int ColumnCount => Math.Max(1, Columns.Count);

    /// <summary>
    ///     按 Excel 的规矩清洗工作表名：去掉 <c>[]:*?/\</c>、首尾空白与首尾单引号、
    ///     截到 31 个字符，清完为空时回退成 <c>Sheet1</c>。
    ///     <para>
    ///         名字不合法 Excel 会直接判定文件损坏（打开时弹「需要修复」），所以在写文件之前就收拾干净。
    ///     </para>
    /// </summary>
    public static string SanitizeSheetName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return FallbackSheetName;

        var buffer = new System.Text.StringBuilder(name.Length);
        foreach (var character in name)
        {
            if (character is '[' or ']' or ':' or '*' or '?' or '/' or '\\')
                continue;

            // 控制字符同样会让 Excel 报错。
            if (char.IsControl(character))
                continue;

            buffer.Append(character);
        }

        // 首尾空白与单引号：Excel 不允许工作表名以 ' 开头或结尾。
        var cleaned = buffer.ToString().Trim().Trim('\'').Trim();

        if (cleaned.Length > MaxSheetNameLength)
            cleaned = cleaned[..MaxSheetNameLength].Trim();

        return cleaned.Length == 0 ? FallbackSheetName : cleaned;
    }
}
