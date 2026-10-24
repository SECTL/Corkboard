using System.Globalization;
using Corkboard.Core.Homework;
using Corkboard.Core.Notes;
using Corkboard.Core.RichText;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Core.Export;

/// <summary>
///     把作业排成一张表格（<see cref="ExportTable" />）：8 列、按科目可选分块、
///     截止日期与创建时间是<b>真日期</b>单元格。
///     <para>
///         纯逻辑：不碰 UI、不读时钟（<paramref name="now" /> 由调用方给），所以整套样式与文案都能单测。
///         样式（配色 / 列宽 / 边框）不在这里定——这里只说「这一格是什么语义」，
///         具体画成什么样由 <see cref="XlsxTableWriter" /> 负责。
///     </para>
/// </summary>
public sealed class HomeworkTableBuilder
{
    /// <summary>截止日期的 Excel 数字格式（Excel 的 <c>mm</c> 就是月份，<c>yyyy-mm-dd</c> 才是「年-月-日」）。</summary>
    public const string DateNumberFormat = "yyyy-mm-dd";

    /// <summary>创建时间的 Excel 数字格式。</summary>
    public const string DateTimeNumberFormat = "yyyy-mm-dd hh:mm";

    /// <summary>Excel 日期序列号的原点：1899-12-30（1900 年闰年 bug 之后的实际基准）。</summary>
    private static readonly DateOnly ExcelEpoch = new(1899, 12, 30);

    /// <summary>标签之间的分隔符（与卡片上胶囊之间的顿号一致）。</summary>
    private const string TagSeparator = "、";

    /// <summary>
    ///     生成表格。
    /// </summary>
    /// <param name="notes">候选作业（可以是存储层的全部作业，含已清理的）。</param>
    /// <param name="boardName">第 1 行的板名。</param>
    /// <param name="now">导出时间：写进副标题，同时决定「今天 / 明天 / 过期 N 天」怎么算。</param>
    /// <param name="groupBySubject">是否按科目分块（每块前面一行组标题）。</param>
    /// <param name="includeCleaned">是否连已清理（收起来的）作业一起导。</param>
    /// <param name="sortMode">排序方式，取 <c>ExportSettingsConfig.TableSortMode</c>。</param>
    public ExportTable Build(
        IReadOnlyList<Note> notes,
        string boardName,
        DateTime now,
        bool groupBySubject,
        bool includeCleaned,
        ExportSortMode sortMode = ExportSortMode.FollowBoard)
    {
        ArgumentNullException.ThrowIfNull(notes);

        var candidates = includeCleaned
            ? notes
            : notes.Where(note => note.CleanedAt is null).ToList();

        var ordered = ExportNoteSorter.Sort(candidates, sortMode);
        var today = DateOnly.FromDateTime(now);

        var rows = new List<ExportTableRow> { BuildHeaderRow() };

        if (groupBySubject)
        {
            // 按科目分块：组顺序按「第一次出现」的先后（排序稳定），组内保持排序结果。
            foreach (var group in ordered.GroupBy(note => ExportNoteSorter.SubjectDisplayName(note.Subject)))
            {
                var groupNotes = group.ToList();
                rows.Add(new ExportTableRow(ExportTableRowKind.Group,
                [
                    new ExportTableCell(
                        string.Format(CR.Export_Table_GroupTitle, group.Key, groupNotes.Count),
                        ExportTableCellStyle.GroupTitle)
                ]));

                foreach (var note in groupNotes)
                    rows.Add(BuildNoteRow(note, today));
            }
        }
        else
        {
            foreach (var note in ordered)
                rows.Add(BuildNoteRow(note, today));
        }

        // 序号跨分组连续：先排好行再统一编号，分块开关不会打乱编号。
        rows = NumberNoteRows(rows);

        var sheetName = ExportTable.SanitizeSheetName(CR.Export_Table_SheetName);
        var title = string.IsNullOrWhiteSpace(boardName) ? sheetName : boardName.Trim();
        var subtitle = string.Format(
            CR.Export_Table_Summary,
            ordered.Count,
            now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));

        return new ExportTable(sheetName, BuildColumns(), title, subtitle, rows);
    }

    /// <summary>8 列的表头文案。</summary>
    public static IReadOnlyList<string> BuildColumns() =>
    [
        CR.Export_Table_HeaderIndex,
        CR.Export_Table_HeaderSubject,
        CR.Export_Table_HeaderSection,
        CR.Export_Table_HeaderDueDate,
        CR.Export_Table_HeaderStatus,
        CR.Export_Table_HeaderContent,
        CR.Export_Table_HeaderTags,
        CR.Export_Table_HeaderCreated
    ];

    /// <summary>把 Excel 的日期序列号算出来（<c>1899-12-30</c> 起算，带时分）。</summary>
    public static double ToExcelSerial(DateTime value)
    {
        var days = DateOnly.FromDateTime(value).DayNumber - ExcelEpoch.DayNumber;
        var minutes = value.Hour * 60 + value.Minute;
        return Math.Round(days + minutes / 1440d, 9);
    }

    /// <summary>把 <see cref="DateOnly" /> 换算成 Excel 的日期序列号。</summary>
    public static double ToExcelSerial(DateOnly value) => value.DayNumber - ExcelEpoch.DayNumber;

    private static ExportTableRow BuildHeaderRow() =>
        new(ExportTableRowKind.Header,
            BuildColumns().Select(text => new ExportTableCell(text, ExportTableCellStyle.Header)).ToList());

    /// <summary>一条作业一行；序号先留空，最后由 <see cref="NumberNoteRows" /> 统一编。</summary>
    private static ExportTableRow BuildNoteRow(Note note, DateOnly today)
    {
        var cells = new List<ExportTableCell>(8)
        {
            new(string.Empty),
            new(ExportNoteSorter.SubjectDisplayName(note.Subject)),
            new(note.Section?.Trim() ?? string.Empty),
            BuildDueDateCell(note),
            BuildStatusCell(note, today),
            new(BuildContentText(note)),
            new(note.Tags is { Count: > 0 } tags ? string.Join(TagSeparator, tags) : string.Empty),
            BuildCreatedCell(note)
        };

        return new ExportTableRow(ExportTableRowKind.Note, cells);
    }

    /// <summary>截止日期：真日期单元格（数值 + 数字格式）；没填就是空格子。</summary>
    private static ExportTableCell BuildDueDateCell(Note note)
    {
        if (note.DueDate is not { } due)
            return new ExportTableCell(string.Empty);

        return new ExportTableCell(string.Empty, ExportTableCellStyle.Normal,
            ToExcelSerial(due), DateNumberFormat);
    }

    /// <summary>
    ///     状态列。判据与界面完全一致（都走 <see cref="HomeworkDueDateFormatter" />）：
    ///     当天是「今天截止」、第二天是「明天截止」，<b>过了当天</b>才算过期。
    /// </summary>
    private static ExportTableCell BuildStatusCell(Note note, DateOnly today)
    {
        if (note.CleanedAt is not null)
            return new ExportTableCell(CR.Export_Table_Cleaned, ExportTableCellStyle.Muted);

        if (note.DueDate is not { } due)
            return new ExportTableCell(CR.Export_Table_NoDueDate, ExportTableCellStyle.Muted);

        var daysLeft = HomeworkDueDateFormatter.DaysLeft(due, today);
        return daysLeft switch
        {
            0 => new ExportTableCell(CR.Export_Table_DueToday, ExportTableCellStyle.Emphasis),
            1 => new ExportTableCell(CR.Export_Table_DueTomorrow, ExportTableCellStyle.Emphasis),
            < 0 => new ExportTableCell(
                string.Format(CR.Export_Table_DueOverdue, -daysLeft), ExportTableCellStyle.Overdue),
            _ => new ExportTableCell(string.Format(CR.Export_Table_DueRemaining, daysLeft))
        };
    }

    /// <summary>内容列：手写作业给一句占位，其余把富文本 HTML 摊成纯文本。</summary>
    private static string BuildContentText(Note note)
    {
        if (note.ContentKind == RichTextContentKind.Handwriting)
            return CR.Export_Table_Handwriting;

        return RichTextPlainText.Convert(note.Content);
    }

    /// <summary>创建时间：真日期时间单元格，按本地时间（用户看到的就是本地时间）。</summary>
    private static ExportTableCell BuildCreatedCell(Note note)
    {
        var local = note.CreatedAt.LocalDateTime;
        return new ExportTableCell(string.Empty, ExportTableCellStyle.Normal,
            ToExcelSerial(local), DateTimeNumberFormat);
    }

    /// <summary>给作业行填上从 1 起的连续序号（跳过表头与组标题行）。</summary>
    private static List<ExportTableRow> NumberNoteRows(List<ExportTableRow> rows)
    {
        var index = 0;
        var numbered = new List<ExportTableRow>(rows.Count);
        foreach (var row in rows)
        {
            if (row.Kind != ExportTableRowKind.Note)
            {
                numbered.Add(row);
                continue;
            }

            index++;
            var cells = row.Cells.ToList();
            if (cells.Count > 0)
                cells[0] = cells[0] with { Text = index.ToString(CultureInfo.InvariantCulture) };

            numbered.Add(row with { Cells = cells });
        }

        return numbered;
    }
}
