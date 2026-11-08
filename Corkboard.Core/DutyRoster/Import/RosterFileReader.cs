using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Corkboard.Core.DutyRoster.Import;

/// <summary>读入的文件形态。仅用于界面提示与后续流程分支，不落盘。</summary>
public enum RosterFileFormat
{
    /// <summary>扩展名与文件头都不认识。</summary>
    Unknown = 0,

    /// <summary>逗号分隔文本。</summary>
    Csv = 1,

    /// <summary>OOXML 工作簿（.xlsx / .xlsm）。</summary>
    Xlsx = 2,

    /// <summary>Excel 97-2003 的 BIFF/OLE2 老格式（.xls），明确不支持。</summary>
    LegacyXls = 3
}

/// <summary>
///     文件读取结果。<see cref="Grid" /> 只在成功时有内容；失败时 <see cref="Error" /> 说明原因，
///     全程<b>不抛异常</b>——导入向导要能在坏文件上继续显示提示，异常会把界面打成白屏。
/// </summary>
public sealed record RosterFileReadResult(
    RosterImportGrid Grid,
    RosterFileFormat Format,
    RosterImportError Error,
    string? Message)
{
    public bool Success => Error == RosterImportError.None;

    internal static RosterFileReadResult Ok(RosterFileFormat format, RosterImportGrid grid) =>
        new(grid, format, RosterImportError.None, null);

    internal static RosterFileReadResult Failure(RosterFileFormat format, RosterImportError error, string message) =>
        new(RosterImportGrid.Empty, format, error, message);
}

/// <summary>
///     名单文件读取：把 .csv / .xlsx 读成 <see cref="RosterImportGrid" />，不引任何 NuGet 包。
///     <para>
///         xlsx 走 <see cref="System.IO.Compression.ZipArchive" /> + 手写 OOXML 解析（与
///         <c>Export/XlsxTableWriter</c> 同一套思路）：只读单元格的<b>值</b>，不读样式、公式、合并单元格。
///         值日名单是二维平面数据，这些信息对导入没有意义，而完整实现 OOXML 是无底洞。
///     </para>
///     <para>
///         老格式 .xls 是 BIFF（OLE2 复合文档），没有现成库时解析成本极高且极易出错，
///         所以直接报 <see cref="RosterImportError.LegacyXlsUnsupported" /> 并提示另存为 xlsx/csv。
///     </para>
///     <para>
///         CSV 一律按 UTF-8 解（识别 UTF-8/UTF-16 的 BOM），<b>不做编码推断</b>——
///         GBK 编码的 CSV 会被解成乱码，这是有意的：推断错编码比报乱码更难排查。用户另存为 UTF-8 即可。
///     </para>
/// </summary>
public static class RosterFileReader
{
    /// <summary>读入文件的上限（64 MB）。名单文件本来只有几十 KB，超过这个大小基本是选错了文件。</summary>
    public const long MaximumFileBytes = 64L * 1024 * 1024;

    /// <summary>网格行数上限。再大的表不是值日名单，继续读只会拖死界面。</summary>
    public const int MaximumGridRows = 200_000;

    /// <summary>网格列数上限（Excel 本身是 16384 列，但名单表不会超过 1024 列）。</summary>
    public const int MaximumGridColumns = 1024;

    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>
    ///     读一个磁盘文件。扩展名与文件头都会看：扩展名不认识但文件头是 zip 的，按 xlsx 试读；
    ///     反过来扩展名是 .csv、内容却是二进制的，会在解码/解析阶段自然报错。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <param name="sheetName">只对 .xlsx 有意义：要读的工作表名；为空时读第一个工作表。</param>
    public static RosterFileReadResult Read(string? path, string? sheetName = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return RosterFileReadResult.Failure(RosterFileFormat.Unknown, RosterImportError.ReadFailed, "没有指定要导入的文件。");

        var format = DetectFormat(path);
        if (format == RosterFileFormat.LegacyXls)
        {
            return RosterFileReadResult.Failure(format, RosterImportError.LegacyXlsUnsupported,
                "不支持 .xls（Excel 97-2003）格式，请在 Excel 里另存为 .xlsx 或 .csv 再导入。");
        }

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return RosterFileReadResult.Failure(format, RosterImportError.ReadFailed, $"找不到文件：{info.Name}");

            if (info.Length == 0)
                return RosterFileReadResult.Failure(format, RosterImportError.EmptyFile, "文件是空的。");

            if (info.Length > MaximumFileBytes)
                return RosterFileReadResult.Failure(format, RosterImportError.ReadFailed, "文件超过 64 MB，请确认选的是名单文件。");

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var content = new byte[info.Length];
            var offset = 0;
            while (offset < content.Length)
            {
                var read = stream.Read(content, offset, content.Length - offset);
                if (read <= 0)
                    break;

                offset += read;
            }

            if (offset < content.Length)
                Array.Resize(ref content, offset);

            return Read(content, Path.GetFileName(path), sheetName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or SecurityException)
        {
            return RosterFileReadResult.Failure(format, RosterImportError.ReadFailed, $"读取文件失败：{ex.Message}");
        }
    }

    /// <summary>
    ///     列出一个 xlsx 里的工作表名（按工作簿里的顺序）。CSV 没有工作表概念，返回空表；
    ///     读不出来（损坏、非 zip、无工作表）时也返回空表，让调用方按单表继续——
    ///     区域选择器只在名字多于一个时才显示工作表下拉，空表不会把它带偏。
    /// </summary>
    public static IReadOnlyList<string> ReadSheetNames(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || DetectFormat(path) != RosterFileFormat.Xlsx)
            return [];

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > MaximumFileBytes)
                return [];

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return ReadSheetNames(buffer.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or SecurityException)
        {
            return [];
        }
    }

    /// <summary>列出一个 xlsx 字节内容里的工作表名（单测与内存导入用）；读不出来时返回空表。</summary>
    public static IReadOnlyList<string> ReadSheetNames(byte[]? content)
    {
        if (content is null || content.Length == 0)
            return [];

        try
        {
            using var archive = new ZipArchive(new MemoryStream(content, false), ZipArchiveMode.Read);
            return ResolveSheets(archive).Sheets.Select(sheet => sheet.Name).ToArray();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or XmlException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>读一段字节内容（单测与「从剪贴板/内存导入」都走这里，不必落盘）。</summary>
    public static RosterFileReadResult Read(byte[]? content, string? fileName = null, string? sheetName = null)
    {
        if (content is null || content.Length == 0)
            return RosterFileReadResult.Failure(RosterFileFormat.Unknown, RosterImportError.EmptyFile, "文件是空的。");

        if (content.LongLength > MaximumFileBytes)
            return RosterFileReadResult.Failure(RosterFileFormat.Unknown, RosterImportError.ReadFailed, "文件超过 64 MB，请确认选的是名单文件。");

        var format = DetectFormat(fileName, content);
        switch (format)
        {
            case RosterFileFormat.LegacyXls:
                return RosterFileReadResult.Failure(format, RosterImportError.LegacyXlsUnsupported,
                    "不支持 .xls（Excel 97-2003）格式，请在 Excel 里另存为 .xlsx 或 .csv 再导入。");

            case RosterFileFormat.Unknown:
                return RosterFileReadResult.Failure(format, RosterImportError.UnsupportedFormat,
                    "只能导入 .csv 或 .xlsx 文件。");

            case RosterFileFormat.Csv:
            {
                var grid = ParseCsv(DecodeText(content));
                return IsGridEmpty(grid)
                    ? RosterFileReadResult.Failure(format, RosterImportError.EmptyFile, "CSV 里没有任何内容。")
                    : RosterFileReadResult.Ok(format, grid);
            }

            default:
                return ReadXlsx(content, sheetName);
        }
    }

    /// <summary>读一个流；内部会先拷进内存（ZipArchive 需要可寻址流），上限同 <see cref="MaximumFileBytes" />。</summary>
    public static RosterFileReadResult Read(Stream? stream, string? fileName = null, string? sheetName = null)
    {
        if (stream is null)
            return RosterFileReadResult.Failure(RosterFileFormat.Unknown, RosterImportError.ReadFailed, "没有可读取的内容。");

        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + read > MaximumFileBytes)
                    return RosterFileReadResult.Failure(RosterFileFormat.Unknown, RosterImportError.ReadFailed,
                        "文件超过 64 MB，请确认选的是名单文件。");

                buffer.Write(chunk, 0, read);
            }

            return Read(buffer.ToArray(), fileName, sheetName);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ObjectDisposedException)
        {
            return RosterFileReadResult.Failure(RosterFileFormat.Unknown, RosterImportError.ReadFailed, $"读取文件失败：{ex.Message}");
        }
    }

    /// <summary>
    ///     判断文件形态：先看扩展名（最可靠，用户也最直观），扩展名不认识再看文件头。
    ///     zip 头（PK\x03\x04）说明是 OOXML；OLE2 头（D0 CF 11 E0）说明是老 .xls。
    /// </summary>
    public static RosterFileFormat DetectFormat(string? fileName, ReadOnlySpan<byte> head = default)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        switch (extension)
        {
            case ".csv":
            case ".txt":
                return RosterFileFormat.Csv;
            case ".xlsx":
            case ".xlsm":
                return RosterFileFormat.Xlsx;
            case ".xls":
                return RosterFileFormat.LegacyXls;
        }

        if (head.Length >= 4 && head[0] == 0x50 && head[1] == 0x4B && head[2] == 0x03 && head[3] == 0x04)
            return RosterFileFormat.Xlsx;

        if (head.Length >= 8 && head[0] == 0xD0 && head[1] == 0xCF && head[2] == 0x11 && head[3] == 0xE0)
            return RosterFileFormat.LegacyXls;

        return RosterFileFormat.Unknown;
    }

    /// <summary>
    ///     CSV → 网格。支持引号包裹（字段里可以有逗号、换行、双写引号 <c>""</c>）、CRLF/LF/CR 三种换行、
    ///     UTF-8 BOM。字段<b>不做 Trim</b>：裁剪在 <see cref="RosterImportRegionBuilder.Build" /> 里统一做，
    ///     这里保留原样才能让区域选择器看到的行号/列号与文件一致。
    /// </summary>
    public static RosterImportGrid ParseCsv(string? text) => ParseDelimited(text, ',');

    /// <summary>
    ///     粘贴的表格文本 → 网格。分隔符自动识别：Tab（从 Excel 复制出来常见）、分号（欧洲式 CSV）
    ///     与逗号都认，取首个非空行里出现最多的那个；引号转义规则与 <see cref="ParseCsv" /> 完全相同。
    ///     剪贴板里粘出来的内容没有扩展名，不能靠文件形态判断，所以单独给一个入口。
    /// </summary>
    public static RosterImportGrid ParseDelimitedText(string? text) => ParseDelimited(text, DetectDelimiter(text));

    /// <summary>
    ///     识别粘贴文本的分隔符：只数首个非空行里、引号外的候选字符。
    ///     全都不出现时退回逗号（单列表格用哪个分隔符结果都一样）。
    /// </summary>
    private static char DetectDelimiter(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return ',';

        var tabs = 0;
        var semicolons = 0;
        var commas = 0;
        var inQuotes = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (inQuotes)
                continue;

            switch (character)
            {
                case '\t':
                    tabs++;
                    break;
                case ';':
                    semicolons++;
                    break;
                case ',':
                    commas++;
                    break;
                case '\r':
                case '\n':
                    // 只看第一行：列多的那一行才是真正定分隔符的地方
                    if (tabs + semicolons + commas > 0)
                        goto done;
                    break;
            }
        }

        done:
        // Tab 优先：从 Excel 复制出来的内容里，字段本身常带逗号，只有 Tab 才是列分隔符。
        if (tabs > 0 && tabs >= semicolons && tabs >= commas)
            return '\t';

        return semicolons > commas ? ';' : ',';
    }

    /// <summary>按指定分隔符解析文本；CSV 文件走逗号，粘贴文本走自动识别出来的那个。</summary>
    private static RosterImportGrid ParseDelimited(string? text, char delimiter)
    {
        if (string.IsNullOrEmpty(text))
            return RosterImportGrid.Empty;

        var rows = new List<List<string?>>();
        var row = new List<string?>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (inQuotes)
            {
                if (character == '"')
                {
                    // 引号内出现 "" 是转义出来的一个字面引号，否则就是引号字段的结束。
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            switch (character)
            {
                case '"' when !fieldStarted:
                    inQuotes = true;
                    fieldStarted = true;
                    break;

                case var separator when separator == delimiter:
                    row.Add(field.ToString());
                    field.Clear();
                    fieldStarted = false;
                    break;

                case '\r':
                    if (index + 1 < text.Length && text[index + 1] == '\n')
                        index++;
                    EndRow();
                    break;

                case '\n':
                    EndRow();
                    break;

                default:
                    field.Append(character);
                    fieldStarted = true;
                    break;
            }

            continue;

            void EndRow()
            {
                row.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
                rows.Add(row);
                row = [];
            }
        }

        // 文件不以下一个换行结尾时，最后一行还在缓冲里；引号没闭合也照此收尾（宽容解析，不抛）。
        if (fieldStarted || field.Length > 0 || row.Count > 0 || inQuotes)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return new RosterImportGrid(rows);
    }

    /// <summary>
    ///     xlsx → 网格。读指定工作表（默认第一个）的值：
    ///     共享字符串（<c>t="s"</c>）、内联字符串（<c>t="inlineStr"</c>）与普通数值都按文本取。
    ///     数值单元格会保持存储原样（日期就是一个序列号）——值日名单里不需要日期列。
    /// </summary>
    private static RosterFileReadResult ReadXlsx(byte[] content, string? sheetName)
    {
        RosterImportGrid grid;
        RosterImportError error;
        string? message;
        try
        {
            using var archive = new ZipArchive(new MemoryStream(content, false), ZipArchiveMode.Read);
            grid = ReadXlsxArchive(archive, sheetName, out error, out message);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or XmlException or NotSupportedException)
        {
            return RosterFileReadResult.Failure(RosterFileFormat.Xlsx, RosterImportError.InvalidArchive,
                $"xlsx 文件已损坏或不是有效的工作簿：{ex.Message}");
        }

        return error == RosterImportError.None
            ? RosterFileReadResult.Ok(RosterFileFormat.Xlsx, grid)
            : RosterFileReadResult.Failure(RosterFileFormat.Xlsx, error, message ?? "读取工作表失败。");
    }

    private static RosterImportGrid ReadXlsxArchive(
        ZipArchive archive,
        string? requestedSheetName,
        out RosterImportError error,
        out string? message)
    {
        error = RosterImportError.None;
        message = null;

        var (sheets, sharedStringsPath) = ResolveSheets(archive);
        if (sheets.Count == 0)
        {
            error = RosterImportError.NoWorksheet;
            message = "xlsx 里没有工作表。";
            return RosterImportGrid.Empty;
        }

        SheetEntry? selected;
        if (string.IsNullOrWhiteSpace(requestedSheetName))
        {
            selected = sheets[0];
        }
        else
        {
            selected = sheets.FirstOrDefault(sheet =>
                string.Equals(sheet.Name, requestedSheetName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (selected is null)
            {
                error = RosterImportError.NoWorksheet;
                message = $"找不到工作表「{requestedSheetName}」。";
                return RosterImportGrid.Empty;
            }
        }

        if (selected.PartPath is null)
        {
            error = RosterImportError.NoWorksheet;
            message = "工作表内容是空的或读不出来。";
            return RosterImportGrid.Empty;
        }

        var sharedStrings = ReadSharedStrings(archive, sharedStringsPath);
        var sheet = LoadXml(archive, selected.PartPath);
        if (sheet?.Root is null)
        {
            error = RosterImportError.NoWorksheet;
            message = "工作表内容是空的或读不出来。";
            return RosterImportGrid.Empty;
        }

        var rows = new List<List<string?>>();
        var lastRowNumber = 0;
        foreach (var rowElement in sheet.Descendants().Where(element => element.Name.LocalName == "row"))
        {
            var rowNumber = ParseInteger((string?)rowElement.Attribute("r")) ?? lastRowNumber + 1;
            if (rowNumber < 1)
                rowNumber = lastRowNumber + 1;

            if (rowNumber > MaximumGridRows)
            {
                error = RosterImportError.ReadFailed;
                message = $"工作表超过 {MaximumGridRows} 行，请先裁剪后再导入。";
                return RosterImportGrid.Empty;
            }

            lastRowNumber = rowNumber;
            while (rows.Count < rowNumber)
                rows.Add([]);

            var cells = rows[rowNumber - 1];
            var lastColumn = 0;
            foreach (var cellElement in rowElement.Elements().Where(element => element.Name.LocalName == "c"))
            {
                var column = ParseCellColumn((string?)cellElement.Attribute("r"), lastColumn + 1);
                if (column > MaximumGridColumns)
                {
                    error = RosterImportError.ReadFailed;
                    message = $"工作表超过 {MaximumGridColumns} 列，请先裁剪后再导入。";
                    return RosterImportGrid.Empty;
                }

                lastColumn = column;
                while (cells.Count < column)
                    cells.Add(null);

                var text = ReadCellText(cellElement, sharedStrings);
                cells[column - 1] = text.Length == 0 ? null : text;
            }
        }

        return new RosterImportGrid(rows);
    }

    /// <summary>工作簿里的一张工作表：显示名 + 包内的 XML part 路径（关系表缺失时可能为 null）。</summary>
    private sealed record SheetEntry(string Name, string? PartPath);

    /// <summary>
    ///     列出工作簿里的工作表与共享字符串表的 part 路径。
    ///     <para>
    ///         正常 xlsx 走「workbook.xml 里的 sheet 顺序 + r:id → _rels/workbook.xml.rels 里对应的 Target」；
    ///         找不到关系文件时按 <c>xl/worksheets/sheet*.xml</c> 的排序兜底——手搓/导出工具产出的
    ///         极简工作簿常常没有完整关系表，但结构性内容仍然可读。
    ///     </para>
    ///     <para>
    ///         工作表下拉要按名字读取，所以这里不能只管第一个 sheet：把 workbook 里每个 sheet 的
    ///         显示名与 part 路径成对返回，顺序就是 Excel 里看到的顺序。
    ///     </para>
    /// </summary>
    private static (List<SheetEntry> Sheets, string? SharedStringsPath) ResolveSheets(ZipArchive archive)
    {
        string? sharedStringsPath = null;
        var worksheetTargetsById = new Dictionary<string, string>(StringComparer.Ordinal);
        var worksheetTargets = new List<string>();

        var relationships = LoadXml(archive, "xl/_rels/workbook.xml.rels")?
            .Descendants()
            .Where(element => element.Name.LocalName == "Relationship")
            .ToArray() ?? [];

        foreach (var relationship in relationships)
        {
            var type = (string?)relationship.Attribute("Type") ?? string.Empty;
            var target = (string?)relationship.Attribute("Target");
            if (string.IsNullOrWhiteSpace(target))
                continue;

            if (type.EndsWith("/sharedStrings", StringComparison.Ordinal))
            {
                sharedStringsPath ??= NormalizePartPath("xl", target);
                continue;
            }

            if (!type.EndsWith("/worksheet", StringComparison.Ordinal))
                continue;

            var partPath = NormalizePartPath("xl", target);
            worksheetTargets.Add(partPath);
            if (relationship.Attribute("Id") is { } idAttribute)
                worksheetTargetsById.TryAdd(idAttribute.Value, partPath);
        }

        // 关系表缺失时按包内文件的自然顺序兜底，名字后面统一补成「工作表 1」「工作表 2」…
        var fallbackTargets = archive.Entries
            .Where(entry => entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                            && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.FullName)
            .ToArray();

        var sheets = new List<SheetEntry>();
        var workbook = LoadXml(archive, "xl/workbook.xml");
        if (workbook is not null)
        {
            var index = 0;
            foreach (var sheetElement in workbook.Descendants().Where(element => element.Name.LocalName == "sheet"))
            {
                var name = ((string?)sheetElement.Attribute("name"))?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                    name = $"工作表 {index + 1}";

                var relationshipId = (string?)sheetElement.Attribute(XName.Get("id", RelationshipNamespace));
                string? partPath = null;
                if (!string.IsNullOrWhiteSpace(relationshipId))
                    worksheetTargetsById.TryGetValue(relationshipId, out partPath);

                if (partPath is null && worksheetTargets.Count > index)
                    partPath = worksheetTargets[index];

                if (partPath is null && fallbackTargets.Length > index)
                    partPath = fallbackTargets[index];

                sheets.Add(new SheetEntry(name, partPath));
                index++;
            }
        }

        if (sheets.Count == 0 && fallbackTargets.Length > 0)
        {
            for (var index = 0; index < fallbackTargets.Length; index++)
                sheets.Add(new SheetEntry($"工作表 {index + 1}", fallbackTargets[index]));
        }

        // 没有关系表时共享字符串就在约定位置；找不到这份表时所有字符串单元格都会读成空，
        // 所以这里给一个约定路径兜底，让 LoadXml 返回 null 时自然退化成空表。
        sharedStringsPath ??= "xl/sharedStrings.xml";

        return (sheets, sharedStringsPath);
    }

    private static List<string> ReadSharedStrings(ZipArchive archive, string? partPath)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(partPath))
            return result;

        var document = LoadXml(archive, partPath);
        if (document?.Root is null)
            return result;

        foreach (var item in document.Root.Elements().Where(element => element.Name.LocalName == "si"))
            result.Add(string.Concat(item.Descendants().Where(element => element.Name.LocalName == "t").Select(element => element.Value)));

        return result;
    }

    private static string ReadCellText(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = (string?)cell.Attribute("t") ?? string.Empty;
        switch (type)
        {
            case "s":
            {
                var index = ParseInteger(FindChild(cell, "v")?.Value);
                return index is { } sharedStringIndex && sharedStringIndex >= 0 && sharedStringIndex < sharedStrings.Count
                    ? sharedStrings[sharedStringIndex]
                    : string.Empty;
            }

            case "inlineStr":
                return string.Concat(FindChild(cell, "is")?.Descendants()
                    .Where(element => element.Name.LocalName == "t")
                    .Select(element => element.Value) ?? []);

            case "str":
            case "n":
            case "b":
            case "e":
            case "":
            default:
                return FindChild(cell, "v")?.Value ?? string.Empty;
        }
    }

    private static XElement? FindChild(XElement element, string localName) =>
        element.Elements().FirstOrDefault(child => child.Name.LocalName == localName);

    /// <summary>
    ///     读包内 XML。DTD 一律禁掉、外部解析器置空：导入的是用户给的任意文件，
    ///     放开 DTD 就等于允许 XXE / 实体膨胀攻击；这里只需要元素与属性，不需要任何实体。
    /// </summary>
    private static XDocument? LoadXml(ZipArchive archive, string partPath)
    {
        var entry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(entry.FullName, partPath, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return null;

        // zip 里声明的解压后大小可能是假的，所以读之前先按条目大小挡一道；真正的解压上限由 XmlReader 读到的内容兜底。
        if (entry.Length > MaximumFileBytes)
            throw new InvalidDataException($"包内 XML 过大：{entry.FullName}");

        using var stream = entry.Open();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            CloseInput = false
        };
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader);
    }

    /// <summary>把 rels 里的 Target（可能是相对路径、也可能是以 / 开头的包内绝对路径）归一成 zip 里的条目路径。</summary>
    private static string NormalizePartPath(string baseDirectory, string target)
    {
        var path = target.Replace('\\', '/');
        var combined = path.StartsWith('/')
            ? path.TrimStart('/')
            : string.IsNullOrEmpty(baseDirectory) ? path : $"{baseDirectory.TrimEnd('/')}/{path}";

        var segments = new List<string>();
        foreach (var segment in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (segment)
            {
                case ".":
                    continue;
                case "..":
                    if (segments.Count > 0)
                        segments.RemoveAt(segments.Count - 1);
                    continue;
                default:
                    segments.Add(segment);
                    break;
            }
        }

        return string.Join('/', segments);
    }

    /// <summary>单元格引用（如 <c>AB12</c>）里的列号；没有引用时按顺序取下一列。</summary>
    private static int ParseCellColumn(string? reference, int fallback)
    {
        if (string.IsNullOrEmpty(reference))
            return Math.Max(1, fallback);

        var column = 0;
        foreach (var character in reference)
        {
            if (character is >= 'A' and <= 'Z')
                column = column * 26 + (character - 'A' + 1);
            else if (character is >= 'a' and <= 'z')
                column = column * 26 + (character - 'a' + 1);
            else
                break;
        }

        return column > 0 ? column : Math.Max(1, fallback);
    }

    private static int? ParseInteger(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    /// <summary>
    ///     字节 → 文本。只认 BOM 明确的三种编码，其余按 UTF-8 解（非法字节替换成 U+FFFD，
    ///     不抛异常——宽容解析的要求在读取层也成立）。不做 GBK 等编码推断，见类型注释。
    /// </summary>
    private static string DecodeText(byte[] content)
    {
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
            return Encoding.UTF8.GetString(content, 3, content.Length - 3);

        if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE)
            return Encoding.Unicode.GetString(content, 2, content.Length - 2);

        if (content.Length >= 2 && content[0] == 0xFE && content[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(content, 2, content.Length - 2);

        return Encoding.UTF8.GetString(content);
    }

    private static bool IsGridEmpty(RosterImportGrid grid)
    {
        for (var row = 1; row <= grid.RowCount; row++)
        {
            if (!grid.IsRowEmpty(row))
                return false;
        }

        return true;
    }
}
