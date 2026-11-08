using System.Globalization;
using System.IO.Compression;
using System.Text;
using Corkboard.Core.DutyRoster;
using Corkboard.Core.DutyRoster.Export;
using Corkboard.Core.DutyRoster.Import;

namespace Corkboard.Core.Tests;

/// <summary>
///     值日名单导出：CSV 表头 / 转义 / BOM、XLSX 包内容、离线二维码文档映射与帧往返、二维码 PNG。
///     <para>
///         不依赖界面、不读机器时钟（时间用固定值）与 <see cref="Guid.NewGuid" />，
///         重跑结果一致，也不会在 en-US 的 CI 上变红。
///     </para>
/// </summary>
public class DutyRosterExportTests
{
    private static readonly DateTime FixedNow = new(2026, 11, 8, 3, 30, 0);

    private const string SessionId = "0123456789abcdef0123456789abcdef";

    // ── CSV ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Csv_HeaderAndRows_UseExportColumns()
    {
        var fixture = CreateFixture();

        var lines = DutyRosterExporter.BuildCsv(fixture.Members, fixture.Groups)
            .Split("\r\n");

        Assert.Equal("序号,名称,性别,小组,标签,参加值日", lines[0]);
        Assert.Equal(4, lines.Length);
        Assert.Equal("1,张三,男,第一组,班长,是", lines[1]);
        Assert.Equal("2,李四,女,第一组,学习委员,是", lines[2]);
        Assert.Equal("3,王五,男,,,否", lines[3]);
    }

    [Fact]
    public void Csv_EscapesCommaQuoteAndNewline()
    {
        var group = Group("第一组");
        var quoted = Member("1", "张,三", "男", "he said \"hi\"", true, group.Id);
        var multiline = Member("2", "李四", "女", "标签一\r\n标签二", true, group.Id);

        var csv = DutyRosterExporter.BuildCsv([quoted, multiline], new Dictionary<Guid, DutyGroup> { [group.Id] = group });

        Assert.Contains("\"张,三\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"he said \"\"hi\"\"\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"标签一\r\n标签二\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void Csv_EmptyValues_StayEmpty()
    {
        var member = Member(string.Empty, string.Empty, string.Empty, string.Empty, false);

        var lines = DutyRosterExporter.BuildCsv([member]).Split("\r\n");

        Assert.Equal(",,,,,否", lines[1]);
    }

    [Fact]
    public void CsvBytes_StartWithUtf8Bom()
    {
        var fixture = CreateFixture();

        var bytes = DutyRosterExporter.BuildCsvBytes(fixture.Members, fixture.Groups);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.StartsWith("序号,名称", text, StringComparison.Ordinal);
        Assert.Contains("张三", text, StringComparison.Ordinal);
    }

    // ── XLSX ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Xlsx_IsValidZipWithTitleSubtitleHeaderAndRows()
    {
        var fixture = CreateFixture();

        var bytes = DutyRosterExporter.BuildXlsx(fixture.Members, fixture.Groups, "值日名单", FixedNow);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));

        var workbook = ReadEntry(archive, "xl/workbook.xml");
        Assert.Contains("name=\"值日名单\"", workbook, StringComparison.Ordinal);

        var sheet = ReadEntry(archive, "xl/worksheets/sheet1.xml");
        Assert.Contains("序号", sheet, StringComparison.Ordinal);
        Assert.Contains("名称", sheet, StringComparison.Ordinal);
        Assert.Contains("参加值日", sheet, StringComparison.Ordinal);
        Assert.Contains("张三", sheet, StringComparison.Ordinal);
        Assert.Contains("第一组", sheet, StringComparison.Ordinal);
        Assert.Contains("共 3 人 · 导出时间 2026-11-08 03:30", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void Xlsx_EmptyRoster_StillWritesHeaderOnlyWorkbook()
    {
        var bytes = DutyRosterExporter.BuildXlsx([], null, string.Empty, FixedNow);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var workbook = ReadEntry(archive, "xl/workbook.xml");
        Assert.Contains("name=\"当前名单\"", workbook, StringComparison.Ordinal);

        var sheet = ReadEntry(archive, "xl/worksheets/sheet1.xml");
        Assert.Contains("序号", sheet, StringComparison.Ordinal);
        Assert.Contains("共 0 人", sheet, StringComparison.Ordinal);
    }

    // ── 离线二维码 ─────────────────────────────────────────────────────────────

    [Fact]
    public void TransferDocument_MapsMemberFields()
    {
        var fixture = CreateFixture();

        var document = DutyRosterExporter.BuildTransferDocument(fixture.Members, fixture.Groups, "值日名单");

        Assert.Equal(OfflineQrProtocol.DocumentVersion, document.Version);
        Assert.Equal(RosterTransferKind.DutyRoster, document.Kind);
        Assert.Equal("值日名单.secrandom-roster.json", document.FileName);
        Assert.Equal(3, document.Rows.Count);

        Assert.Equal(new RosterTransferRow(true, "1", "张三", "男", "第一组", "班长"), document.Rows[0]);
        Assert.Equal(new RosterTransferRow(false, "3", "王五", "男", string.Empty, string.Empty), document.Rows[2]);
    }

    [Fact]
    public void TransferDocument_EmptyRosterName_FallsBackToCurrentRoster()
    {
        var document = DutyRosterExporter.BuildTransferDocument([], null, "  ");

        Assert.Equal("当前名单.secrandom-roster.json", document.FileName);
        Assert.Empty(document.Rows);
        Assert.Equal("当前名单", DutyRosterExporter.ResolveRosterName(null));
        Assert.Equal("值日名单", DutyRosterExporter.ResolveRosterName("  值日名单  "));
    }

    [Fact]
    public void QrFrames_RoundTripThroughAccumulator()
    {
        var fixture = CreateFixture();

        var frames = DutyRosterExporter.BuildQrFrames(fixture.Members, fixture.Groups, "值日名单", SessionId);
        var accumulator = new OfflineQrFrameAccumulator();

        Assert.StartsWith(OfflineQrProtocol.ManifestPrefix, frames[0], StringComparison.Ordinal);
        foreach (var frame in frames)
            Assert.Equal(RosterQrFrameImportResult.Accepted, accumulator.AddFrame(frame));

        Assert.True(accumulator.IsComplete);
        Assert.Equal(SessionId, accumulator.SessionId);
        Assert.Equal(frames.Count - 1, accumulator.TotalFrameCount);

        var result = accumulator.Complete();
        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.Equal(RosterTransferKind.DutyRoster, result.Document!.Kind);
        Assert.Equal("值日名单.secrandom-roster.json", result.Document.FileName);
        Assert.Equal(3, result.Document.Rows.Count);
        Assert.Equal("张三", result.Document.Rows[0].Name);
        Assert.Equal("第一组", result.Document.Rows[0].DetailTwo);
        Assert.Equal("王五", result.Document.Rows[2].Name);
        Assert.False(result.Document.Rows[2].Exists);
    }

    [Fact]
    public void QrPng_StartsWithPngSignature()
    {
        var png = DutyRosterExporter.CreateQrPng("SRQR1M|frame");

        Assert.True(png.Length > 8);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png.Take(8).ToArray());
    }

    // ── 辅助 ────────────────────────────────────────────────────────────────────

    private sealed record Fixture(
        IReadOnlyList<DutyMember> Members,
        IReadOnlyDictionary<Guid, DutyGroup> Groups);

    private static Fixture CreateFixture()
    {
        var group = Group("第一组");
        var members = new List<DutyMember>
        {
            Member("1", "张三", "男", "班长", true, group.Id),
            Member("2", "李四", "女", "学习委员", true, group.Id),
            Member("3", "王五", "男", string.Empty, false)
        };

        return new Fixture(members, new Dictionary<Guid, DutyGroup> { [group.Id] = group });
    }

    private static DutyGroup Group(string name) => new() { Name = name };

    private static DutyMember Member(
        string id,
        string name,
        string gender,
        string tags,
        bool isActive,
        Guid? groupId = null) =>
        new()
        {
            Id = id,
            Name = name,
            Gender = gender,
            Tags = tags,
            IsActive = isActive,
            GroupId = groupId
        };

    private static string ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}