using System.IO.Compression;
using System.Text;
using Corkboard.Core.DutyRoster;
using Corkboard.Core.DutyRoster.Import;

namespace Corkboard.Core.Tests;

/// <summary>
///     名单导入的纯逻辑：区域识别、列映射、解析成员、重名处理、RecordId 复用，以及 csv/xlsx 读取。
///     <para>
///         表格数据一律用英文表头与英文姓名（关键字表里本来就有），不依赖界面语言与本地化资源；
///         这样在 CI 的 en-US runner 上结果与本机完全一致。
///     </para>
/// </summary>
public class DutyImportTests
{
    private static readonly string[] StandardColumns = ["id", "name", "gender", "tags"];

    // ── 区域识别 ────────────────────────────────────────────────────────────────

    [Fact]
    public void DetectHeaderRow_SkipsTitleRowAboveRealHeader()
    {
        var grid = Grid(
            ["Class 1 roster"],
            ["id", "name", "gender", "tags"],
            ["1", "alice", "f", "leader"],
            ["2", "bob", "m", ""]);

        var headerRow = RosterImportRegionBuilder.DetectHeaderRow(grid, RosterImportKeywords.Default.All);

        Assert.Equal(2, headerRow);
    }

    [Fact]
    public void DetectRegion_TrimsTrailingEmptyRowsAndColumns()
    {
        var grid = Grid(
            ["id", "name", null, null],
            ["1", "alice", null, null],
            ["2", "bob", null, null],
            [],
            [null, null, null, null]);

        var region = RosterImportRegionBuilder.DetectRegion(grid);

        Assert.Equal(1, region.HeaderRow);
        Assert.Equal(2, region.FirstDataRow);
        Assert.Equal(3, region.LastDataRow);
        Assert.Equal(2, region.LastColumn);
    }

    [Fact]
    public void Build_WithoutHeaderRow_FallsBackToColumnLetters()
    {
        var grid = Grid(
            [],
            ["alice", "f"]);

        var table = RosterImportRegionBuilder.Build(grid, new RosterImportRegion(null, 1, null, 1, null));

        Assert.Equal(new[] { "A", "B" }, table.Columns);
        Assert.Single(table.Rows);
        Assert.Equal("alice", table.Rows[0]["A"]);
    }

    [Fact]
    public void Build_DuplicateHeaderNames_GetNumericSuffix()
    {
        var grid = Grid(
            ["name", "name", "NAME"],
            ["a", "b", "c"]);

        var table = RosterImportRegionBuilder.Build(grid, RosterImportRegion.Default(1));

        Assert.Equal(3, table.Columns.Count);
        Assert.Equal("name", table.Columns[0]);
        Assert.Equal("name (2)", table.Columns[1]);
        Assert.Equal("NAME (3)", table.Columns[2]);
        Assert.Equal(3, table.Columns.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void RegionBuild_ClampsDataRangeAfterHeader()
    {
        var grid = Grid(
            ["id", "name"],
            ["1", "alice"]);

        // 用户把 FirstDataRow 填到表头行上，也应该从表头下一行开始，别把表头当数据。
        var table = RosterImportRegionBuilder.Build(grid, new RosterImportRegion(1, 1, null, 1, null));

        Assert.Single(table.Rows);
        Assert.Equal("alice", table.Rows[0]["name"]);
    }

    [Fact]
    public void ColumnName_RoundTripsWithColumnIndex()
    {
        Assert.Equal("A", RosterImportRegionBuilder.ColumnName(1));
        Assert.Equal("Z", RosterImportRegionBuilder.ColumnName(26));
        Assert.Equal("AA", RosterImportRegionBuilder.ColumnName(27));
        Assert.Equal(1, RosterImportRegionBuilder.ColumnIndex("A"));
        Assert.Equal(27, RosterImportRegionBuilder.ColumnIndex("AA"));
        Assert.Equal(0, RosterImportRegionBuilder.ColumnIndex("1A"));
    }

    // ── 列映射 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void MapColumns_AssignsEachFieldToItsOwnColumn()
    {
        var columns = new[] { "id", "name", "gender", "group", "tags" };

        var mapping = RosterImportParser.MapColumns(columns);

        Assert.Equal("id", mapping.Id);
        Assert.Equal("name", mapping.Name);
        Assert.Equal("gender", mapping.Gender);
        Assert.Equal("group", mapping.Group);
        Assert.Equal("tags", mapping.Tags);
        Assert.True(mapping.HasRequiredColumn);
    }

    [Fact]
    public void MapColumns_GenericAndSpecificHeaders_DoNotShareOneColumn()
    {
        // 两列都带 student，最容易出现「同一列同时映射成姓名与序号」的 bug。
        var columns = new[] { "student name", "student id" };

        var mapping = RosterImportParser.MapColumns(columns);

        Assert.Equal("student name", mapping.Name);
        Assert.Equal("student id", mapping.Id);
    }

    [Fact]
    public void ScoreColumn_PrefersExactMatchOverContains()
    {
        var keywords = RosterImportKeywords.Default.Name;

        var exact = RosterImportParser.ScoreColumn("name", keywords);
        var contains = RosterImportParser.ScoreColumn("student name", keywords);

        Assert.True(exact > contains);
        Assert.Equal(0, RosterImportParser.ScoreColumn("", keywords));
        Assert.Equal(0, RosterImportParser.ScoreColumn("unrelated", keywords));
    }

    // ── 解析 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_MapsRowsToMembers()
    {
        var table = Table(StandardColumns,
            ["1", "alice", "f", "leader; backup"],
            ["2", "bob", "m", ""]);

        var result = RosterImportParser.Parse(table);

        Assert.True(result.Success);
        Assert.Null(result.Message);
        Assert.Equal(2, result.Members.Count);
        Assert.Equal("1", result.Members[0].Id);
        Assert.Equal("alice", result.Members[0].Name);
        Assert.Equal("f", result.Members[0].Gender);
        Assert.Equal("leader backup", result.Members[0].Tags);
        Assert.True(result.Members[0].IsActive);
        Assert.NotEqual(Guid.Empty, result.Members[0].RecordId);
        Assert.Equal("2", result.Members[1].Id);
    }

    [Fact]
    public void Parse_WithoutNameOrIdColumn_ReturnsMissingRequiredColumn()
    {
        var table = Table(["gender", "tags"], ["f", "leader"]);

        var result = RosterImportParser.Parse(table);

        Assert.False(result.Success);
        Assert.Equal(RosterImportError.MissingRequiredColumn, result.Error);
    }

    [Fact]
    public void Parse_RowsWithoutNameAndId_AreSkipped()
    {
        var table = Table(StandardColumns,
            ["", "", "f", "leader"],
            ["2", "bob", "m", ""]);

        var result = RosterImportParser.Parse(table);

        Assert.True(result.Success);
        Assert.Single(result.Members);
        Assert.Equal("bob", result.Members[0].Name);
    }

    [Fact]
    public void Parse_EmptyTable_ReturnsNoDataRows()
    {
        var table = Table(StandardColumns, ["", "", "", ""]);

        var result = RosterImportParser.Parse(table);

        Assert.False(result.Success);
        Assert.Equal(RosterImportError.NoDataRows, result.Error);
    }

    // ── 重名三档 ────────────────────────────────────────────────────────────────

    [Fact]
    public void DuplicateNames_DefaultHandling_RenamesWithSuffix()
    {
        var table = Table(["id", "name"], ["1", "alice"], ["2", "alice"], ["3", "alice"]);

        var result = RosterImportParser.Parse(table);

        Assert.True(result.Success);
        Assert.Equal(new[] { "alice", "alice (2)", "alice (3)" }, result.Members.Select(member => member.Name));
        Assert.Equal(new[] { "alice" }, result.DuplicateNames);
    }

    [Fact]
    public void DuplicateNames_KeepHandling_LeavesNamesUntouched()
    {
        var table = Table(["id", "name"], ["1", "alice"], ["2", "alice"]);

        var result = RosterImportParser.Parse(table, duplicateHandling: RosterDuplicateHandling.Keep);

        Assert.True(result.Success);
        Assert.Equal(new[] { "alice", "alice" }, result.Members.Select(member => member.Name));
        Assert.Single(result.DuplicateNames);
    }

    [Fact]
    public void DuplicateNames_CancelHandling_ReturnsCancelErrorWithPreview()
    {
        var table = Table(["id", "name"], ["1", "alice"], ["2", "alice"]);

        var result = RosterImportParser.Parse(table, duplicateHandling: RosterDuplicateHandling.Cancel);

        Assert.False(result.Success);
        Assert.Equal(RosterImportError.DuplicateNamesCancelled, result.Error);
        Assert.Equal(2, result.Members.Count);
    }

    [Fact]
    public void RenameDuplicates_AvoidsExistingNamesInRoster()
    {
        var members = new List<DutyMember>
        {
            new() { Id = "1", Name = "alice" },
            new() { Id = "2", Name = "alice (2)" },
            new() { Id = "3", Name = "alice" }
        };

        RosterImportParser.RenameDuplicates(members);

        Assert.Equal("alice", members[0].Name);
        Assert.Equal("alice (2)", members[1].Name);
        Assert.Equal("alice (3)", members[2].Name);
    }

    // ── RecordId 复用 ───────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ReusesRecordIdForSameIdAndName()
    {
        var recordId = Guid.NewGuid();
        var existing = new[] { new DutyMember { Id = "1", Name = "alice", RecordId = recordId } };
        var table = Table(["id", "name"], ["1", "alice"], ["2", "bob"]);

        var result = RosterImportParser.Parse(table, existingMembers: existing);

        Assert.True(result.Success);
        Assert.Equal(recordId, result.Members[0].RecordId);
        Assert.NotEqual(recordId, result.Members[1].RecordId);
        Assert.NotEqual(Guid.Empty, result.Members[1].RecordId);
    }

    [Fact]
    public void Parse_SameKeyTwice_ReusesIdOnlyOnce()
    {
        var recordId = Guid.NewGuid();
        var existing = new[] { new DutyMember { Id = "1", Name = "alice", RecordId = recordId } };
        var table = Table(["id", "name"], ["1", "alice"], ["1", "alice"]);

        var result = RosterImportParser.Parse(table, duplicateHandling: RosterDuplicateHandling.Keep,
            existingMembers: existing);

        Assert.True(result.Success);
        Assert.Equal(recordId, result.Members[0].RecordId);
        Assert.NotEqual(recordId, result.Members[1].RecordId);
        Assert.Equal(2, result.Members.Select(member => member.RecordId).Distinct().Count());
    }

    // ── CSV ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Read_CsvWithBomAndQuotedFields_ParsesEscapes()
    {
        var text = "id,name\r\n\"1\",\"Alice, A.\"\r\n2,\"Bob \"\"B\"\" Smith\"";
        var content = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(text)).ToArray();

        var result = RosterFileReader.Read(content, "roster.csv");

        Assert.True(result.Success);
        Assert.Equal(RosterFileFormat.Csv, result.Format);
        Assert.Equal(3, result.Grid.RowCount);
        Assert.Equal("Alice, A.", result.Grid.GetCell(2, 2));
        Assert.Equal("Bob \"B\" Smith", result.Grid.GetCell(3, 2));
    }

    [Fact]
    public void Read_CsvWithUnclosedQuote_DoesNotThrow()
    {
        var content = Encoding.UTF8.GetBytes("id,name\n1,\"alice");

        var result = RosterFileReader.Read(content, "roster.csv");

        Assert.True(result.Success);
        Assert.Equal(2, result.Grid.RowCount);
        Assert.Equal("alice", result.Grid.GetCell(2, 2));
    }

    [Fact]
    public void Read_EmptyContent_ReturnsEmptyFile()
    {
        var result = RosterFileReader.Read(Array.Empty<byte>(), "roster.csv");

        Assert.False(result.Success);
        Assert.Equal(RosterImportError.EmptyFile, result.Error);
    }

    [Fact]
    public void Read_LegacyXls_ReturnsExplicitError()
    {
        var content = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

        var result = RosterFileReader.Read(content, "roster.xls");

        Assert.False(result.Success);
        Assert.Equal(RosterImportError.LegacyXlsUnsupported, result.Error);
        Assert.Equal(RosterFileFormat.LegacyXls, result.Format);
    }

    [Fact]
    public void Read_CorruptXlsx_ReturnsInvalidArchive()
    {
        var content = Encoding.UTF8.GetBytes("this is not a zip file");

        var result = RosterFileReader.Read(content, "roster.xlsx");

        Assert.False(result.Success);
        Assert.Equal(RosterImportError.InvalidArchive, result.Error);
    }

    // ── xlsx ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Read_XlsxWithInlineStrings_ParsesCells()
    {
        const string worksheet = """
                                 <?xml version="1.0" encoding="UTF-8"?>
                                 <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                                   <sheetData>
                                     <row r="1"><c r="A1" t="inlineStr"><is><t>id</t></is></c><c r="B1" t="inlineStr"><is><t>name</t></is></c></row>
                                     <row r="2"><c r="A2" t="n"><v>1</v></c><c r="B2" t="inlineStr"><is><t>alice</t></is></c></row>
                                   </sheetData>
                                 </worksheet>
                                 """;

        var content = CreateXlsx(("xl/worksheets/sheet1.xml", worksheet));

        var result = RosterFileReader.Read(content, "roster.xlsx");

        Assert.True(result.Success);
        Assert.Equal(RosterFileFormat.Xlsx, result.Format);
        Assert.Equal(2, result.Grid.RowCount);
        Assert.Equal("id", result.Grid.GetCell(1, 1));
        Assert.Equal("1", result.Grid.GetCell(2, 1));
        Assert.Equal("alice", result.Grid.GetCell(2, 2));
    }

    [Fact]
    public void Read_XlsxWithSharedStrings_ResolvesIndexes()
    {
        const string workbook = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                                  <sheets><sheet name="Roster" sheetId="1" r:id="rId1"/></sheets>
                                </workbook>
                                """;
        const string relationships = """
                                     <?xml version="1.0" encoding="UTF-8"?>
                                     <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                                       <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                                       <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/>
                                     </Relationships>
                                     """;
        const string sharedStrings = """
                                     <?xml version="1.0" encoding="UTF-8"?>
                                     <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                                       <si><t>id</t></si><si><t>name</t></si><si><t>alice</t></si>
                                     </sst>
                                     """;
        const string worksheet = """
                                 <?xml version="1.0" encoding="UTF-8"?>
                                 <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                                   <sheetData>
                                     <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>
                                     <row r="2"><c r="A2" t="n"><v>1</v></c><c r="B2" t="s"><v>2</v></c></row>
                                   </sheetData>
                                 </worksheet>
                                 """;

        var content = CreateXlsx(
            ("xl/workbook.xml", workbook),
            ("xl/_rels/workbook.xml.rels", relationships),
            ("xl/sharedStrings.xml", sharedStrings),
            ("xl/worksheets/sheet1.xml", worksheet));

        var result = RosterFileReader.Read(content, "roster.xlsx");

        Assert.True(result.Success);
        Assert.Equal("name", result.Grid.GetCell(1, 2));
        Assert.Equal("alice", result.Grid.GetCell(2, 2));
    }

    [Fact]
    public void Read_XlsxWithoutWorksheet_ReturnsNoWorksheet()
    {
        var content = CreateXlsx(("xl/workbook.xml", """<?xml version="1.0"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"/>"""));

        var result = RosterFileReader.Read(content, "roster.xlsx");

        Assert.False(result.Success);
        Assert.Equal(RosterImportError.NoWorksheet, result.Error);
    }

    // ── 辅助 ────────────────────────────────────────────────────────────────────

    private static RosterImportGrid Grid(params string?[][] rows) =>
        new(rows.Select(row => (IEnumerable<string?>)row));

    private static RosterImportTable Table(string[] columns, params string[][] rows)
    {
        var list = new List<IReadOnlyDictionary<string, string>>(rows.Length);
        foreach (var row in rows)
        {
            var values = new Dictionary<string, string>(columns.Length, StringComparer.Ordinal);
            for (var index = 0; index < columns.Length; index++)
                values[columns[index]] = index < row.Length ? row[index] : string.Empty;

            list.Add(values);
        }

        return new RosterImportTable(columns, list);
    }

    private static byte[] CreateXlsx(params (string Path, string Xml)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, xml) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream, new UTF8Encoding(false));
                writer.Write(xml);
            }
        }

        return stream.ToArray();
    }
}
