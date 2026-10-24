using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Corkboard.Core.Export;
using Corkboard.Core.Notes;
using Corkboard.Core.RichText;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Core.Tests;

/// <summary>
///     表格导出：分块 / 排序 / 状态文案 / 内容摊平 / xlsx 文件结构。
///     <para>
///         全部在内存里跑：<see cref="HomeworkTableBuilder.Build" /> 的「现在」由参数给，
///         <see cref="XlsxTableWriter" /> 写到 <see cref="MemoryStream" />，不碰时钟也不写临时文件。
///         </para>
///     <para>
///         断言里用的文案一律取 <see cref="CR" />（resx 的中文条目），不写字面量——
///         这样改文案不会连带改测试，而 LocalizationTests 负责保证中英两套键齐。
///     </para>
/// </summary>
public class ExportTableTests
{
    /// <summary>导出「现在」：固定成 2026-03-01 09:30，跨月用例就落在这天前后。</summary>
    private static readonly DateTime Now = new(2026, 3, 1, 9, 30, 0, DateTimeKind.Local);

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    // ------------------------------------------------------------------
    // 分组
    // ------------------------------------------------------------------

    [Fact]
    public void Build_GroupBySubject_PutsOneGroupRowPerSubjectWithCounts()
    {
        var notes = new List<Note>
        {
            NewNote("数学", "一", At(8)),
            NewNote("语文", "二", At(9)),
            NewNote("数学", "三", At(10))
        };

        var table = new HomeworkTableBuilder().Build(notes, "作业板", Now, groupBySubject: true, includeCleaned: false);

        // 表头 + 两个组标题 + 三条作业。
        Assert.Equal(ExportTableRowKind.Header, table.Rows[0].Kind);
        Assert.Equal(6, table.Rows.Count);

        var groups = table.Rows.Where(row => row.Kind == ExportTableRowKind.Group).ToList();
        Assert.Equal(2, groups.Count);
        Assert.Equal(string.Format(CR.Export_Table_GroupTitle, "数学", 2), groups[0].Cells[0].Text);
        Assert.Equal(string.Format(CR.Export_Table_GroupTitle, "语文", 1), groups[1].Cells[0].Text);

        // 组标题行只有一格（写进 A 列、合并到 H 列），样式是 GroupTitle。
        Assert.Single(groups[0].Cells);
        Assert.Equal(ExportTableCellStyle.GroupTitle, groups[0].Cells[0].Style);
    }

    [Fact]
    public void Build_GroupBySubject_KeepsEachGroupContiguous()
    {
        var notes = new List<Note>
        {
            NewNote("数学", "一", At(8)),
            NewNote("语文", "二", At(9)),
            NewNote("数学", "三", At(10))
        };

        var table = new HomeworkTableBuilder().Build(notes, "作业板", Now, groupBySubject: true, includeCleaned: false);

        // 同一科目的作业必须挨在一起（跨科目交替的输入也要归到各自的块里）。
        var order = table.Rows.Select(row => row.Kind).ToList();
        Assert.Equal(
            new[]
            {
                ExportTableRowKind.Header,
                ExportTableRowKind.Group,
                ExportTableRowKind.Note,
                ExportTableRowKind.Note,
                ExportTableRowKind.Group,
                ExportTableRowKind.Note
            },
            order);

        var subjects = table.Rows
            .Where(row => row.Kind == ExportTableRowKind.Note)
            .Select(row => row.Cells[1].Text)
            .ToList();
        Assert.Equal(new[] { "数学", "数学", "语文" }, subjects);
    }

    [Fact]
    public void Build_GroupBySubject_OffHasNoGroupRowsAndKeepsNumberingContinuous()
    {
        var notes = new List<Note>
        {
            NewNote("数学", "一", At(8)),
            NewNote("语文", "二", At(9)),
            NewNote("数学", "三", At(10))
        };

        var table = new HomeworkTableBuilder().Build(notes, "作业板", Now, groupBySubject: false, includeCleaned: false);

        Assert.Equal(4, table.Rows.Count);
        Assert.DoesNotContain(table.Rows, row => row.Kind == ExportTableRowKind.Group);

        // 关掉分块，序号仍然从 1 起连续。
        Assert.Equal(new[] { "1", "2", "3" }, NoteRows(table).Select(row => row.Cells[0].Text));
    }

    [Fact]
    public void Build_GroupBySubject_NumberingRunsAcrossGroups()
    {
        var notes = new List<Note>
        {
            NewNote("数学", "一", At(8)),
            NewNote("语文", "二", At(9)),
            NewNote("数学", "三", At(10))
        };

        var table = new HomeworkTableBuilder().Build(notes, "作业板", Now, groupBySubject: true, includeCleaned: false);

        // 序号跨分组连续，不会每个组都从 1 重新数。
        Assert.Equal(new[] { "1", "2", "3" }, NoteRows(table).Select(row => row.Cells[0].Text));
    }

    [Fact]
    public void Build_EmptySubjectGroupUsesNoSubjectText()
    {
        var notes = new List<Note> { NewNote(string.Empty, "一", At(8)) };

        var table = new HomeworkTableBuilder().Build(notes, "作业板", Now, groupBySubject: true, includeCleaned: false);

        Assert.Equal(CR.Export_Table_NoSubject, NoteRows(table)[0].Cells[1].Text);
        Assert.Equal(
            string.Format(CR.Export_Table_GroupTitle, CR.Export_Table_NoSubject, 1),
            table.Rows[1].Cells[0].Text);
    }

    [Fact]
    public void Build_IncludeCleaned_DropsCleanedNotesWhenOff()
    {
        var cleaned = NewNote("数学", "已清", At(11));
        cleaned.CleanedAt = new DateTimeOffset(2026, 2, 28, 8, 0, 0, TimeSpan.Zero);

        var notes = new List<Note> { NewNote("数学", "在板上", At(8)), cleaned };

        Assert.Single(NoteRows(new HomeworkTableBuilder().Build(notes, "作业板", Now, true, false)));
        Assert.Equal(2, NoteRows(new HomeworkTableBuilder().Build(notes, "作业板", Now, true, true)).Count);
    }

    // ------------------------------------------------------------------
    // 排序
    // ------------------------------------------------------------------

    [Fact]
    public void Build_SortFollowBoard_KeepsCallerOrder()
    {
        var a = NewNote("数学", "甲", At(10));
        var b = NewNote("数学", "乙", At(11));
        var c = NewNote("数学", "丙", At(12));

        // 板面顺序由页面给：跟随板子就是原样不动。
        var table = new HomeworkTableBuilder().Build(
            [b, a, c], "作业板", Now, false, false, ExportSortMode.FollowBoard);

        Assert.Equal(new[] { "乙", "甲", "丙" }, Contents(table));
    }

    [Fact]
    public void Build_SortCreatedDescending_NewestFirst()
    {
        var a = NewNote("数学", "甲", At(10));
        var b = NewNote("数学", "乙", At(11));
        var c = NewNote("数学", "丙", At(12));

        var table = new HomeworkTableBuilder().Build(
            [b, a, c], "作业板", Now, false, false, ExportSortMode.CreatedDescending);

        Assert.Equal(new[] { "丙", "乙", "甲" }, Contents(table));
    }

    [Fact]
    public void Build_SortCreatedAscending_OldestFirst()
    {
        var a = NewNote("数学", "甲", At(10));
        var b = NewNote("数学", "乙", At(11));
        var c = NewNote("数学", "丙", At(12));

        var table = new HomeworkTableBuilder().Build(
            [b, a, c], "作业板", Now, false, false, ExportSortMode.CreatedAscending);

        Assert.Equal(new[] { "甲", "乙", "丙" }, Contents(table));
    }

    [Fact]
    public void Build_SortBySubject_OrdersByDisplayedSubjectName()
    {
        var maths = NewNote("数学", "甲", At(8));
        var chinese = NewNote("语文", "乙", At(9));
        var english = NewNote("英语", "丙", At(10));

        // 数学(sh) < 英语(ying) < 语文(yu)：当前文化或序数比较都是这个顺序。
        var table = new HomeworkTableBuilder().Build(
            [chinese, maths, english], "作业板", Now, false, false, ExportSortMode.Subject);

        Assert.Equal(new[] { "数学", "英语", "语文" }, NoteRows(table).Select(row => row.Cells[1].Text));
    }

    // ------------------------------------------------------------------
    // 状态列
    // ------------------------------------------------------------------

    [Fact]
    public void Build_Status_NoDueDate()
    {
        var cell = StatusCell(NewNote("数学", "一", At(8)));

        Assert.Equal(CR.Export_Table_NoDueDate, cell.Text);
        Assert.Equal(ExportTableCellStyle.Muted, cell.Style);
    }

    [Fact]
    public void Build_Status_DueToday()
    {
        var cell = StatusCell(NewNote("数学", "一", At(8), due: Today));

        Assert.Equal(CR.Export_Table_DueToday, cell.Text);
        Assert.Equal(ExportTableCellStyle.Emphasis, cell.Style);
    }

    [Fact]
    public void Build_Status_DueTomorrow()
    {
        var cell = StatusCell(NewNote("数学", "一", At(8), due: Today.AddDays(1)));

        Assert.Equal(CR.Export_Table_DueTomorrow, cell.Text);
    }

    [Fact]
    public void Build_Status_DueRemainingCountsDays()
    {
        var cell = StatusCell(NewNote("数学", "一", At(8), due: Today.AddDays(5)));

        Assert.Equal(string.Format(CR.Export_Table_DueRemaining, 5), cell.Text);
        Assert.Equal(ExportTableCellStyle.Normal, cell.Style);
    }

    [Fact]
    public void Build_Status_OverdueCountsDaysAcrossMonths()
    {
        // 2026-02-25 到期、今天 2026-03-01：跨月也要算成过期 4 天（2 月只有 28 天）。
        var cell = StatusCell(NewNote("数学", "一", At(8), due: new DateOnly(2026, 2, 25)));

        Assert.Equal(string.Format(CR.Export_Table_DueOverdue, 4), cell.Text);
        Assert.Equal(ExportTableCellStyle.Overdue, cell.Style);
    }

    [Fact]
    public void Build_Status_CleanedWinsOverDueDate()
    {
        var note = NewNote("数学", "一", At(8), due: Today.AddDays(-3));
        note.CleanedAt = new DateTimeOffset(2026, 2, 28, 8, 0, 0, TimeSpan.Zero);

        var cell = StatusCell(note);

        Assert.Equal(CR.Export_Table_Cleaned, cell.Text);
        Assert.Equal(ExportTableCellStyle.Muted, cell.Style);
    }

    [Fact]
    public void Build_DueDateIsRealDateCell()
    {
        var due = new DateOnly(2026, 3, 9);
        var table = new HomeworkTableBuilder().Build(
            [NewNote("数学", "一", At(8), due: due)], "作业板", Now, false, false);

        var cell = NoteRows(table)[0].Cells[3];

        Assert.Equal(HomeworkTableBuilder.DateNumberFormat, cell.NumberFormat);
        Assert.Equal(HomeworkTableBuilder.ToExcelSerial(due), cell.Number);
    }

    [Fact]
    public void Build_CreatedIsRealDateTimeCell()
    {
        var created = new DateTimeOffset(2026, 3, 1, 14, 5, 0, TimeSpan.Zero);
        var table = new HomeworkTableBuilder().Build(
            [NewNote("数学", "一", created)], "作业板", Now, false, false);

        var cell = NoteRows(table)[0].Cells[7];

        Assert.Equal(HomeworkTableBuilder.DateTimeNumberFormat, cell.NumberFormat);
        Assert.Equal(
            HomeworkTableBuilder.ToExcelSerial(created.LocalDateTime),
            cell.Number);
    }

    // ------------------------------------------------------------------
    // 内容列
    // ------------------------------------------------------------------

    [Fact]
    public void Build_HandwritingContentUsesPlaceholder()
    {
        var note = NewNote("数学", string.Empty, At(8));
        note.ContentKind = RichTextContentKind.Handwriting;

        Assert.Equal(CR.Export_Table_Handwriting, NoteRows(Single(note))[0].Cells[5].Text);
    }

    [Fact]
    public void Build_HtmlContentIsFlattenedToPlainText()
    {
        var note = NewNote("数学", "<p>第一段</p><p>第二段<br>还是第二段</p>", At(8));
        note.ContentKind = RichTextContentKind.Html;

        Assert.Equal("第一段\n第二段\n还是第二段", NoteRows(Single(note))[0].Cells[5].Text);
    }

    [Fact]
    public void Build_HtmlContentDecodesEntities()
    {
        var note = NewNote("数学", "<p>a &amp; b &lt;c&gt;&nbsp;d</p>", At(8));
        note.ContentKind = RichTextContentKind.Html;

        // 实体解成真字符；&nbsp; 解出来的空格被压成一个普通空格。
        Assert.Equal("a & b <c> d", NoteRows(Single(note))[0].Cells[5].Text);
    }

    [Fact]
    public void Build_TagsAreJoinedWithIdeographicComma()
    {
        var note = NewNote("数学", "一", At(8), tags: ["重点", "复习"]);

        Assert.Equal("重点、复习", NoteRows(Single(note))[0].Cells[6].Text);
    }

    [Fact]
    public void Build_SectionIsTrimmedAndMayBeEmpty()
    {
        var withSection = NewNote("数学", "一", At(8), section: "  第一章  ");
        var withoutSection = NewNote("数学", "二", At(9));

        var table = new HomeworkTableBuilder().Build([withSection, withoutSection], "作业板", Now, false, false);

        Assert.Equal("第一章", NoteRows(table)[0].Cells[2].Text);
        Assert.Equal(string.Empty, NoteRows(table)[1].Cells[2].Text);
    }

    // ------------------------------------------------------------------
    // 表格骨架
    // ------------------------------------------------------------------

    [Fact]
    public void Build_FillsTitleSubtitleAndColumns()
    {
        var table = new HomeworkTableBuilder().Build(
            [NewNote("数学", "一", At(8))], "  我的作业板  ", Now, false, false);

        Assert.Equal("我的作业板", table.Title);
        Assert.Equal(string.Format(CR.Export_Table_Summary, 1, "2026-03-01 09:30"), table.Subtitle);
        Assert.Equal(8, table.Columns.Count);
        Assert.Equal(CR.Export_Table_HeaderIndex, table.Columns[0]);
        Assert.Equal(CR.Export_Table_HeaderCreated, table.Columns[7]);
        Assert.Equal(new double[] { 6, 12, 14, 12, 11, 60, 18, 18 }, table.ColumnWidths);
        Assert.Equal(CR.Export_Table_SheetName, table.SheetName);
    }

    [Fact]
    public void Build_BlankBoardNameFallsBackToSheetName()
    {
        var table = new HomeworkTableBuilder().Build([], "   ", Now, false, false);

        Assert.Equal(CR.Export_Table_SheetName, table.Title);
        Assert.Equal(CR.Export_Table_SheetName, table.SheetName);
    }

    [Fact]
    public void SanitizeSheetName_StripsForbiddenCharactersAndTruncates()
    {
        Assert.Equal("作业板", ExportTable.SanitizeSheetName("作业板"));

        // Excel 不接受 []:*?/\ 这七个字符。
        Assert.Equal("a-bc", ExportTable.SanitizeSheetName("a-[]:*?/\\bc"));

        Assert.Equal(new string('x', 31), ExportTable.SanitizeSheetName(new string('x', 40)));
        Assert.Equal(ExportTable.FallbackSheetName, ExportTable.SanitizeSheetName("   "));
        Assert.Equal(ExportTable.FallbackSheetName, ExportTable.SanitizeSheetName(null));
    }

    // ------------------------------------------------------------------
    // xlsx 写入
    // ------------------------------------------------------------------

    [Fact]
    public void Write_ProducesReadableZipWithRequiredParts()
    {
        var bytes = WriteSampleTable();

        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var names = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("[Content_Types].xml", names);
        Assert.Contains("_rels/.rels", names);
        Assert.Contains("xl/workbook.xml", names);
        Assert.Contains("xl/_rels/workbook.xml.rels", names);
        Assert.Contains("xl/styles.xml", names);
        Assert.Contains("xl/worksheets/sheet1.xml", names);
    }

    [Fact]
    public void Write_SheetXmlIsValidXmlAndCarriesTitleAndHeader()
    {
        var bytes = WriteSampleTable();
        var sheet = ReadEntry(bytes, "xl/worksheets/sheet1.xml");

        var document = XDocument.Parse(sheet);
        var ns = document.Root!.Name.Namespace;

        // 第 1 行是板名、第 2 行是副标题，都要真的在 XML 里（inline string）。
        Assert.Contains("我的作业板", sheet);
        Assert.Contains(CR.Export_Table_HeaderIndex, sheet);
        Assert.Contains(CR.Export_Table_HeaderContent, sheet);

        // 冻结窗格在表头下面（前 3 行是标题 / 副标题 / 空行，表头是第 4 行）。
        var pane = document.Descendants(ns + "pane").Single();
        Assert.Equal("4", pane.Attribute("ySplit")!.Value);
        Assert.Equal("frozen", pane.Attribute("state")!.Value);

        // A1:H1 与 A2:H2 是合并单元格。
        var merges = document.Descendants(ns + "mergeCell")
            .Select(element => element.Attribute("ref")!.Value)
            .ToList();
        Assert.Contains("A1:H1", merges);
        Assert.Contains("A2:H2", merges);

        // 日期是真数字单元格：截止日期那一列写着序列号。
        Assert.Contains("<v>46090</v>", sheet);
    }

    [Fact]
    public void Write_EscapesSpecialCharacters()
    {
        var note = NewNote("数学", "<p>a &amp; b &lt;c&gt;</p>", At(8));
        note.ContentKind = RichTextContentKind.Html;

        var table = new HomeworkTableBuilder().Build([note], "我的作业板", Now, false, false);
        var bytes = Write(table);
        var sheet = ReadEntry(bytes, "xl/worksheets/sheet1.xml");

        // 纯文本里的 & < > 必须转义成实体，否则 Excel 直接报「文件已损坏」。
        Assert.Contains("a &amp; b &lt;c&gt;", sheet);
        _ = XDocument.Parse(sheet);
    }

    [Fact]
    public void Write_StripsInvalidControlCharacters()
    {
        var note = NewNote("数学", "正常\u0001文字", At(8));

        var table = new HomeworkTableBuilder().Build([note], "我的作业板", Now, false, false);
        var sheet = ReadEntry(Write(table), "xl/worksheets/sheet1.xml");

        // U+0001 这类控制字符在 XML 1.0 里非法：写之前剔掉，剩下的字照常保留。
        Assert.DoesNotContain('\u0001', sheet);
        Assert.Contains("正常文字", sheet);
        _ = XDocument.Parse(sheet);
    }

    [Fact]
    public void Write_GroupRowsAreMergedAcrossAllColumns()
    {
        var table = new HomeworkTableBuilder().Build(
            [NewNote("数学", "一", At(8))], "我的作业板", Now, groupBySubject: true, includeCleaned: false);

        var sheet = ReadEntry(Write(table), "xl/worksheets/sheet1.xml");
        var document = XDocument.Parse(sheet);
        var ns = document.Root!.Name.Namespace;

        var merges = document.Descendants(ns + "mergeCell")
            .Select(element => element.Attribute("ref")!.Value)
            .ToList();

        // 组标题行紧跟在表头下面（第 5 行），A:H 整行合并。
        Assert.Contains("A5:H5", merges);
    }

    // ------------------------------------------------------------------
    // 辅助
    // ------------------------------------------------------------------

    /// <summary>「今天 09:30 之后的第 <paramref name="hour" /> 点」，用来造有先后顺序的作业。</summary>
    private static DateTimeOffset At(int hour) => new(2026, 2, 20, hour, 0, 0, TimeSpan.Zero);

    private static Note NewNote(
        string subject,
        string content,
        DateTimeOffset createdAt,
        DateOnly? due = null,
        string? section = null,
        IReadOnlyList<string>? tags = null)
    {
        return new Note
        {
            Subject = subject,
            Content = content,
            ContentKind = RichTextContentKind.Html,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            DueDate = due,
            Section = section,
            Tags = tags is null ? null : new List<string>(tags)
        };
    }

    private static List<ExportTableRow> NoteRows(ExportTable table) =>
        table.Rows.Where(row => row.Kind == ExportTableRowKind.Note).ToList();

    /// <summary>单条作业的表（带已清理的：状态用例要能看到「已清理」那一行）。</summary>
    private static ExportTable Single(Note note) =>
        new HomeworkTableBuilder().Build([note], "作业板", Now, groupBySubject: false, includeCleaned: true);

    private static ExportTableCell StatusCell(Note note) => NoteRows(Single(note))[0].Cells[4];

    private static List<string> Contents(ExportTable table) =>
        NoteRows(table).Select(row => row.Cells[5].Text).ToList();

    private static byte[] WriteSampleTable()
    {
        var notes = new List<Note>
        {
            NewNote("数学", "第一段\n第二段", At(9), due: new DateOnly(2026, 3, 9), section: "第一章", tags: ["重点"])
        };

        var table = new HomeworkTableBuilder().Build(notes, "我的作业板", Now, groupBySubject: true, includeCleaned: false);
        return Write(table);
    }

    private static byte[] Write(ExportTable table)
    {
        using var stream = new MemoryStream();
        new XlsxTableWriter().Write(table, stream);
        return stream.ToArray();
    }

    private static string ReadEntry(byte[] bytes, string name)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(name) ?? throw new InvalidOperationException($"压缩包里没有 {name}");

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
