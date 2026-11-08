using System.Globalization;
using System.Text;
using Corkboard.Core.DutyRoster.Import;
using Corkboard.Core.Export;
using QRCoder;

namespace Corkboard.Core.DutyRoster.Export;

/// <summary>
///     把值日名单排成可导出的表格 / 离线二维码数据。
///     <para>
///         纯逻辑：不碰 UI、不读时钟（<paramref name="now" /> 由调用方给），
///         所以 CSV 转义、XLSX 字节、二维码帧都能直接单测。二维码 PNG 用纯托管的 QRCoder 生成，
///         界面层只负责把字节包成 <c>Bitmap</c>。
///     </para>
/// </summary>
public static class DutyRosterExporter
{
    /// <summary>表头：CSV 与 XLSX 共用同一套列。</summary>
    public static IReadOnlyList<string> BuildColumns() =>
    [
        "序号",
        "名称",
        "性别",
        "小组",
        "标签",
        "参加值日"
    ];

    /// <summary>XLSX 里每列的宽度（序号窄、标签宽）。</summary>
    public static IReadOnlyList<double> ColumnWidths { get; } = [6, 12, 8, 14, 18, 10];

    /// <summary>副标题里的导出时间格式（与作业板导出保持一致）。</summary>
    public const string ExportTimeFormat = "yyyy-MM-dd HH:mm";

    /// <summary>名单名为空时用的占位名。</summary>
    public const string DefaultRosterName = "当前名单";

    /// <summary>二维码帧默认每个模块的像素数：在 256×256 的显示框里足够清晰。</summary>
    public const int DefaultQrPixelsPerModule = 4;

    /// <summary>行尾用 CRLF（RFC 4180 / Excel 都认）。</summary>
    private const string LineBreak = "\r\n";

    /// <summary>CSV 里「参加值日」的两档文案。</summary>
    private const string AttendingText = "是";
    private const string NotAttendingText = "否";

    /// <summary>
    ///     生成 CSV 文本（不带 BOM；写文件请用 <see cref="BuildCsvBytes" />）。
    /// </summary>
    /// <param name="members">要导出的成员（含不参加值日的，照实写「否」）。</param>
    /// <param name="groups">按 Id 索引的小组；为空时「小组」列留空。</param>
    public static string BuildCsv(
        IReadOnlyList<DutyMember> members,
        IReadOnlyDictionary<Guid, DutyGroup>? groups = null)
    {
        ArgumentNullException.ThrowIfNull(members);

        var lines = new List<string>(members.Count + 1)
        {
            string.Join(',', BuildColumns().Select(EscapeCsv))
        };

        foreach (var member in members)
        {
            var fields = new[]
            {
                member.Id,
                member.Name,
                member.Gender,
                ResolveGroupName(member, groups),
                member.Tags,
                member.IsActive ? AttendingText : NotAttendingText
            };

            lines.Add(string.Join(',', fields.Select(EscapeCsv)));
        }

        return string.Join(LineBreak, lines);
    }

    /// <summary>生成带 UTF-8 BOM 的 CSV 字节（Excel 双击打开中文不乱码）。</summary>
    public static byte[] BuildCsvBytes(
        IReadOnlyList<DutyMember> members,
        IReadOnlyDictionary<Guid, DutyGroup>? groups = null)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var body = encoding.GetBytes(BuildCsv(members, groups));
        var preamble = encoding.GetPreamble();
        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }

    /// <summary>生成 XLSX 工作簿的字节（内存里的 zip + OOXML）。</summary>
    /// <param name="members">要导出的成员。</param>
    /// <param name="groups">按 Id 索引的小组；为空时「小组」列留空。</param>
    /// <param name="rosterName">名单名：同时用作工作表名与标题。</param>
    /// <param name="now">导出时间：写进副标题。</param>
    public static byte[] BuildXlsx(
        IReadOnlyList<DutyMember> members,
        IReadOnlyDictionary<Guid, DutyGroup>? groups,
        string rosterName,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(members);

        var table = BuildTable(members, groups, rosterName, now);
        using var buffer = new MemoryStream();
        new XlsxTableWriter().Write(table, buffer);
        return buffer.ToArray();
    }

    /// <summary>拼出 XLSX 的 <see cref="ExportTable" />（标题 = 名单名，副标题 = 人数 + 导出时间）。</summary>
    public static ExportTable BuildTable(
        IReadOnlyList<DutyMember> members,
        IReadOnlyDictionary<Guid, DutyGroup>? groups,
        string rosterName,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(members);

        var name = ResolveRosterName(rosterName);
        var rows = new List<ExportTableRow>(members.Count + 1)
        {
            new(
                ExportTableRowKind.Header,
                BuildColumns()
                    .Select(text => new ExportTableCell(text, ExportTableCellStyle.Header))
                    .ToList())
        };

        foreach (var member in members)
        {
            var cells = new List<ExportTableCell>(6)
            {
                new(member.Id ?? string.Empty),
                new(member.Name ?? string.Empty),
                new(member.Gender ?? string.Empty),
                new(ResolveGroupName(member, groups)),
                new(member.Tags ?? string.Empty),
                new(member.IsActive ? AttendingText : NotAttendingText)
            };
            rows.Add(new ExportTableRow(ExportTableRowKind.Note, cells));
        }

        var subtitle = $"共 {members.Count} 人 · 导出时间 {now.ToString(ExportTimeFormat, CultureInfo.InvariantCulture)}";
        return new ExportTable(
            ExportTable.SanitizeSheetName(name),
            BuildColumns(),
            name,
            subtitle,
            rows,
            ColumnWidths);
    }

    /// <summary>
    ///     生成离线二维码传输文档：成员行按 <see cref="RosterTransferRow" /> 的字段装
    ///     （性别 → DetailOne，小组 → DetailTwo）。
    /// </summary>
    public static RosterTransferDocument BuildTransferDocument(
        IReadOnlyList<DutyMember> members,
        IReadOnlyDictionary<Guid, DutyGroup>? groups,
        string rosterName)
    {
        ArgumentNullException.ThrowIfNull(members);

        var name = ResolveRosterName(rosterName);
        var rows = members
            .Select(member => new RosterTransferRow(
                member.IsActive,
                member.Id ?? string.Empty,
                member.Name ?? string.Empty,
                member.Gender,
                ResolveGroupName(member, groups),
                member.Tags))
            .ToList();

        return new RosterTransferDocument(
            OfflineQrProtocol.DocumentVersion,
            RosterTransferKind.DutyRoster,
            $"{name}.secrandom-roster.json",
            rows);
    }

    /// <summary>直接出二维码帧（首帧是清单，其余是数据分片）。</summary>
    public static IReadOnlyList<string> BuildQrFrames(
        IReadOnlyList<DutyMember> members,
        IReadOnlyDictionary<Guid, DutyGroup>? groups,
        string rosterName,
        string? sessionId = null)
    {
        var document = BuildTransferDocument(members, groups, rosterName);
        return OfflineQrProtocol.CreateFrames(document, sessionId);
    }

    /// <summary>把一帧二维码文本编码成 PNG 字节（纯托管，界面层用 Bitmap 显示）。</summary>
    public static byte[] CreateQrPng(string text, int pixelsPerModule = DefaultQrPixelsPerModule)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelsPerModule);

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule, true);
    }

    /// <summary>名单名归一：去空白，空则用「当前名单」。</summary>
    public static string ResolveRosterName(string? rosterName) =>
        string.IsNullOrWhiteSpace(rosterName) ? DefaultRosterName : rosterName.Trim();

    /// <summary>查成员所属小组名；没有小组 / 小组不在字典里就留空。</summary>
    private static string ResolveGroupName(
        DutyMember member,
        IReadOnlyDictionary<Guid, DutyGroup>? groups)
    {
        if (member.GroupId is not { } groupId || groups is null)
            return string.Empty;

        return groups.TryGetValue(groupId, out var group) ? group.Name ?? string.Empty : string.Empty;
    }

    /// <summary>CSV 字段转义：含逗号 / 引号 / 换行的加引号，内部引号翻倍。</summary>
    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var needsQuotes = value.Contains(',') || value.Contains('"')
                          || value.Contains('\r') || value.Contains('\n');
        return needsQuotes ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}