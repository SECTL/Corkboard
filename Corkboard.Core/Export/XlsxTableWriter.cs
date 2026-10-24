using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Corkboard.Core.Export;

/// <summary>
///     把 <see cref="ExportTable" /> 写成 xlsx（OOXML）：一个 zip 包 + 几份 XML，不依赖任何第三方库。
///     <para>
///         手写的理由与仓库里那个 PDF 组装器一样：只是「一页表」，为它引一个 XLSX 依赖不划算，
///         而 xlsx 的必需件少得出奇——<c>[Content_Types].xml</c>、两处 rels、workbook、
///         styles 与一张 sheet，字符串全用 inline string（<c>t="inlineStr"</c>）就不用 sharedStrings。
///     </para>
///     <para>
///         版式（定稿）：第 1 行板名、第 2 行副标题、第 3 行空行、第 4 行表头（冻结在这一行下面）、
///         第 5 行起是数据；表头深蓝底白字，作业行交替底色 + 细边框，内容列自动换行，
///         截止日期与创建时间是<b>真日期单元格</b>（数值 + 数字格式）。
///     </para>
/// </summary>
public sealed class XlsxTableWriter
{
    private const string XmlHeader = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";

    private const string MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>表头下面那一行（数据从第 5 行开始，冻结 4 行）。</summary>
    private const int FrozenRows = 4;

    private const string ContentTypesXml = XmlHeader
        + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
        + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
        + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
        + "<Override PartName=\"/xl/workbook.xml\""
        + " ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
        + "<Override PartName=\"/xl/worksheets/sheet1.xml\""
        + " ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
        + "<Override PartName=\"/xl/styles.xml\""
        + " ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
        + "</Types>";

    private const string PackageRelsXml = XmlHeader
        + "<Relationships xmlns=\"" + PackageRelationshipNamespace + "\">"
        + "<Relationship Id=\"rId1\""
        + " Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\""
        + " Target=\"xl/workbook.xml\"/>"
        + "</Relationships>";

    private const string WorkbookRelsXml = XmlHeader
        + "<Relationships xmlns=\"" + PackageRelationshipNamespace + "\">"
        + "<Relationship Id=\"rId1\""
        + " Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\""
        + " Target=\"worksheets/sheet1.xml\"/>"
        + "<Relationship Id=\"rId2\""
        + " Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\""
        + " Target=\"styles.xml\"/>"
        + "</Relationships>";

    /// <summary>
    ///     把表格写进 <paramref name="output" />。流由调用方管，这里只往里写（会留下一个完整的 zip）。
    /// </summary>
    public void Write(ExportTable table, Stream output)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(output);

        var styles = new StyleSheet();
        var sheetXml = BuildSheetXml(table, styles);

        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        AddEntry(archive, "[Content_Types].xml", ContentTypesXml);
        AddEntry(archive, "_rels/.rels", PackageRelsXml);
        AddEntry(archive, "xl/workbook.xml", BuildWorkbookXml(table.SheetName));
        AddEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelsXml);
        AddEntry(archive, "xl/styles.xml", styles.BuildXml());
        AddEntry(archive, "xl/worksheets/sheet1.xml", sheetXml);
    }

    #region 各部件

    private static string BuildWorkbookXml(string sheetName)
    {
        var escaped = Escape(sheetName);

        return XmlHeader
               + "<workbook xmlns=\"" + MainNamespace + "\" xmlns:r=\"" + RelationshipNamespace + "\">"
               + "<sheets><sheet name=\"" + escaped + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets>"
               + "</workbook>";
    }

    private static string BuildSheetXml(ExportTable table, StyleSheet styles)
    {
        var columnCount = table.ColumnCount;
        var lastColumn = ColumnName(columnCount);
        var lastRow = FrozenRows - 1 + table.Rows.Count;
        var builder = new StringBuilder(4096);

        builder.Append(XmlHeader);
        builder.Append("<worksheet xmlns=\"").Append(MainNamespace).Append("\">");

        // 用到的区域：A1 到最后一行的最后一列。
        builder.Append("<dimension ref=\"A1:").Append(lastColumn).Append(lastRow).Append("\"/>");

        // 冻结：前 4 行（板名 / 副标题 / 空行 / 表头）钉在顶上，数据从第 5 行开始。
        builder.Append("<sheetViews><sheetView tabSelected=\"1\" workbookViewId=\"0\">")
            .Append("<pane ySplit=\"").Append(FrozenRows).Append("\" topLeftCell=\"A")
            .Append(FrozenRows + 1)
            .Append("\" activePane=\"bottomLeft\" state=\"frozen\"/>")
            .Append("<selection pane=\"bottomLeft\" activeCell=\"A").Append(FrozenRows + 1)
            .Append("\" sqref=\"A").Append(FrozenRows + 1).Append("\"/>")
            .Append("</sheetView></sheetViews>");

        builder.Append("<sheetFormatPr defaultRowHeight=\"15\"/>");

        AppendColumns(builder, table, columnCount);

        builder.Append("<sheetData>");

        // 第 1 行：板名（合并 A1:H1，16pt 加粗，行高 28，垂直居中）。
        var titleStyle = styles.Resolve(FontIds.Title, FillIds.None, BorderIds.None,
            NumberFormats.General, wrap: false, horizontal: "left", vertical: "center");
        AppendTextRow(builder, 1, table.Title, titleStyle, columnCount, height: 28);

        // 第 2 行：副标题（合并 A2:H2，10pt 灰字）。
        var subtitleStyle = styles.Resolve(FontIds.Muted, FillIds.None, BorderIds.None,
            NumberFormats.General, wrap: false, horizontal: "left", vertical: "center");
        AppendTextRow(builder, 2, table.Subtitle, subtitleStyle, columnCount);

        // 第 3 行：空行，只为了把标题与表头分开一点（行高 6）。
        builder.Append("<row r=\"3\" ht=\"6\" customHeight=\"1\"/>");

        var merges = new List<string> { "A1:" + lastColumn + "1", "A2:" + lastColumn + "2" };

        // 前 3 行（板名 / 副标题 / 空行）已经写好了，表头正好落在第 4 行、数据从第 5 行起。
        var rowNumber = FrozenRows - 1;
        var noteRowIndex = 0;

        foreach (var row in table.Rows)
        {
            rowNumber++;
            switch (row.Kind)
            {
                case ExportTableRowKind.Header:
                {
                    // 表头：深蓝底白字，水平垂直居中，行高 22。
                    builder.Append("<row r=\"").Append(rowNumber).Append("\" ht=\"22\" customHeight=\"1\"")
                        .Append(SpanAttribute(columnCount)).Append('>');
                    for (var column = 0; column < columnCount; column++)
                    {
                        var text = column < row.Cells.Count ? row.Cells[column].Text : string.Empty;
                        var style = styles.Resolve(FontIds.Header, FillIds.Header, BorderIds.Thin,
                            NumberFormats.General, wrap: false, horizontal: "center", vertical: "center");
                        AppendCell(builder, ColumnName(column + 1), rowNumber, text, style);
                    }

                    builder.Append("</row>");
                    break;
                }

                case ExportTableRowKind.Group:
                {
                    // 组标题：整行合并成一格，浅灰底、加粗、左对齐，行高 20。
                    var text = row.Cells.Count > 0 ? row.Cells[0].Text : string.Empty;
                    var style = styles.Resolve(FontIds.GroupTitle, FillIds.Group, BorderIds.Thin,
                        NumberFormats.General, wrap: false, horizontal: "left", vertical: "center");
                    builder.Append("<row r=\"").Append(rowNumber).Append("\" ht=\"20\" customHeight=\"1\"")
                        .Append(SpanAttribute(columnCount)).Append('>');
                    AppendCell(builder, "A", rowNumber, text, style);
                    builder.Append("</row>");
                    merges.Add($"A{rowNumber}:{lastColumn}{rowNumber}");
                    break;
                }

                default:
                {
                    // 作业行：交替底色，内容列自动换行，其余列不换行。
                    var alternate = noteRowIndex++ % 2 == 1;
                    builder.Append("<row r=\"").Append(rowNumber).Append('"')
                        .Append(SpanAttribute(columnCount)).Append('>');

                    for (var column = 0; column < columnCount; column++)
                    {
                        var cell = column < row.Cells.Count ? row.Cells[column] : null;
                        var style = ResolveNoteCellStyle(styles, cell, alternate, wrap: column == 5);
                        AppendCell(builder, ColumnName(column + 1), rowNumber, cell, style);
                    }

                    builder.Append("</row>");
                    break;
                }
            }
        }

        builder.Append("</sheetData>");

        builder.Append("<mergeCells count=\"").Append(merges.Count).Append("\">");
        foreach (var merge in merges)
            builder.Append("<mergeCell ref=\"").Append(merge).Append("\"/>");
        builder.Append("</mergeCells>");

        builder.Append("</worksheet>");
        return builder.ToString();
    }

    /// <summary>作业行里一格该用哪号样式：字体看语义，底色看交替，数字格式看是不是日期。</summary>
    private static int ResolveNoteCellStyle(
        StyleSheet styles,
        ExportTableCell? cell,
        bool alternate,
        bool wrap)
    {
        var style = cell?.Style ?? ExportTableCellStyle.Normal;
        var fill = alternate ? FillIds.Alternate : FillIds.None;

        var font = style switch
        {
            ExportTableCellStyle.Muted => FontIds.Muted,
            ExportTableCellStyle.Emphasis => FontIds.Emphasis,
            ExportTableCellStyle.Overdue => FontIds.Overdue,
            _ => FontIds.Body
        };

        var numberFormat = cell?.Number is null
            ? NumberFormats.General
            : cell.NumberFormat == HomeworkTableBuilder.DateTimeNumberFormat
                ? NumberFormats.DateTime
                : NumberFormats.Date;

        return styles.Resolve(font, fill, BorderIds.Thin, numberFormat, wrap, horizontal: null, vertical: "top");
    }

    private static void AppendColumns(StringBuilder builder, ExportTable table, int columnCount)
    {
        builder.Append("<cols>");
        for (var column = 0; column < columnCount; column++)
        {
            var width = column < table.ColumnWidths.Count
                ? table.ColumnWidths[column]
                : ExportTable.DefaultColumnWidths[Math.Min(column, ExportTable.DefaultColumnWidths.Count - 1)];

            if (!(width > 0) || double.IsNaN(width) || double.IsInfinity(width))
                width = 10;

            builder.Append("<col min=\"").Append(column + 1).Append("\" max=\"").Append(column + 1)
                .Append("\" width=\"").Append(FormatNumber(width))
                .Append("\" customWidth=\"1\"/>");
        }

        builder.Append("</cols>");
    }

    /// <summary>标题 / 副标题那种「一整行合并的一格」。</summary>
    private static void AppendTextRow(
        StringBuilder builder,
        int rowNumber,
        string text,
        int style,
        int columnCount,
        double? height = null)
    {
        builder.Append("<row r=\"").Append(rowNumber).Append('"');
        if (height is { } value)
            builder.Append(" ht=\"").Append(FormatNumber(value)).Append("\" customHeight=\"1\"");

        builder.Append(SpanAttribute(columnCount)).Append('>');
        AppendCell(builder, "A", rowNumber, text, style);
        builder.Append("</row>");
    }

    private static string SpanAttribute(int columnCount) =>
        columnCount > 1 ? $" spans=\"1:{columnCount}\"" : string.Empty;

    /// <summary>
    ///     写一格：有 <see cref="ExportTableCell.Number" /> 时按数值写，否则按 inline string 写；
    ///     两者都没有（空文本）就写一个只有样式的空格子，边框才不会断。
    /// </summary>
    private static void AppendCell(StringBuilder builder, string columnName, int rowNumber, ExportTableCell? cell, int style)
    {
        var reference = columnName + rowNumber.ToString(CultureInfo.InvariantCulture);

        if (cell?.Number is { } number && double.IsFinite(number))
        {
            builder.Append("<c r=\"").Append(reference).Append("\" s=\"").Append(style).Append("\">")
                .Append("<v>").Append(number.ToString("R", CultureInfo.InvariantCulture)).Append("</v>")
                .Append("</c>");
            return;
        }

        AppendCell(builder, columnName, rowNumber, cell?.Text, style);
    }

    private static void AppendCell(StringBuilder builder, string columnName, int rowNumber, string? text, int style)
    {
        var reference = columnName + rowNumber.ToString(CultureInfo.InvariantCulture);
        var escaped = Escape(text);

        if (escaped.Length == 0)
        {
            builder.Append("<c r=\"").Append(reference).Append("\" s=\"").Append(style).Append("\"/>");
            return;
        }

        builder.Append("<c r=\"").Append(reference).Append("\" s=\"").Append(style)
            .Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
            .Append(escaped)
            .Append("</t></is></c>");
    }

    #endregion

    #region 小工具

    /// <summary>1 → A、27 → AA。</summary>
    private static string ColumnName(int index)
    {
        if (index < 1)
            index = 1;

        var builder = new StringBuilder(3);
        while (index > 0)
        {
            var remainder = (index - 1) % 26;
            builder.Insert(0, (char)('A' + remainder));
            index = (index - 1) / 26;
        }

        return builder.ToString();
    }

    private static string FormatNumber(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    ///     XML 转义 + 剔除非法字符。Excel 遇到非法控制字符会判定文件损坏（打开时要求「修复」），
    ///     所以这里宁可把字吞掉也不能原样写进去。
    /// </summary>
    internal static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            switch (character)
            {
                case '&':
                    builder.Append("&amp;");
                    continue;
                case '<':
                    builder.Append("&lt;");
                    continue;
                case '>':
                    builder.Append("&gt;");
                    continue;
                case '"':
                    builder.Append("&quot;");
                    continue;
                case '\'':
                    builder.Append("&apos;");
                    continue;
                case '\r':
                    // XML 解析器会把裸 CR 归一成 LF；写 &#13; 才能原样保留。
                    builder.Append("&#13;");
                    continue;
                case '\n':
                case '\t':
                    builder.Append(character);
                    continue;
            }

            if (character < 0x20)
                continue;

            if (character == '\uFFFE' || character == '\uFFFF')
                continue;

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    #endregion

    #region 样式表

    /// <summary>字体下标，对应 <see cref="StyleSheet" /> 里 fonts 的顺序。</summary>
    private static class FontIds
    {
        public const int Body = 0;
        public const int Title = 1;
        public const int Muted = 2;
        public const int Header = 3;
        public const int GroupTitle = 4;
        public const int Overdue = 5;
        public const int Emphasis = 6;
    }

    /// <summary>填充下标。0 / 1 是 Excel 规定的两个保留填充，真正的底色从 2 起。</summary>
    private static class FillIds
    {
        public const int None = 0;
        public const int Header = 2;
        public const int Group = 3;
        public const int Alternate = 4;
    }

    private static class BorderIds
    {
        public const int None = 0;
        public const int Thin = 1;
    }

    private static class NumberFormats
    {
        public const int General = 0;
        public const int Date = 164;
        public const int DateTime = 165;
    }

    /// <summary>
    ///     cellXfs 注册表：每遇到一种「字体 + 底色 + 边框 + 数字格式 + 对齐」的组合就登记一号，
    ///     同一组合复用同一号，最后按登记顺序写进 styles.xml。
    /// </summary>
    private sealed class StyleSheet
    {
        private readonly List<string> _xfs = [];
        private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

        public int Resolve(
            int font,
            int fill,
            int border,
            int numberFormat,
            bool wrap,
            string? horizontal,
            string? vertical)
        {
            var key = $"{font}|{fill}|{border}|{numberFormat}|{wrap}|{horizontal}|{vertical}";
            if (_index.TryGetValue(key, out var existing))
                return existing;

            var builder = new StringBuilder(160);
            builder.Append("<xf numFmtId=\"").Append(numberFormat)
                .Append("\" fontId=\"").Append(font)
                .Append("\" fillId=\"").Append(fill)
                .Append("\" borderId=\"").Append(border)
                .Append("\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"");

            if (numberFormat != NumberFormats.General)
                builder.Append(" applyNumberFormat=\"1\"");

            if (wrap || horizontal is not null || vertical is not null)
            {
                builder.Append(" applyAlignment=\"1\"><alignment");
                if (horizontal is not null)
                    builder.Append(" horizontal=\"").Append(horizontal).Append('"');
                if (vertical is not null)
                    builder.Append(" vertical=\"").Append(vertical).Append('"');
                if (wrap)
                    builder.Append(" wrapText=\"1\"");
                builder.Append("/></xf>");
            }
            else
            {
                builder.Append("/>");
            }

            _xfs.Add(builder.ToString());
            var index = _xfs.Count - 1;
            _index[key] = index;
            return index;
        }

        public string BuildXml()
        {
            var builder = new StringBuilder(2048);

            builder.Append(XmlHeader);
            builder.Append("<styleSheet xmlns=\"").Append(MainNamespace).Append("\">");

            builder.Append("<numFmts count=\"2\">")
                .Append("<numFmt numFmtId=\"").Append(NumberFormats.Date)
                .Append("\" formatCode=\"").Append(HomeworkTableBuilder.DateNumberFormat).Append("\"/>")
                .Append("<numFmt numFmtId=\"").Append(NumberFormats.DateTime)
                .Append("\" formatCode=\"").Append(HomeworkTableBuilder.DateTimeNumberFormat).Append("\"/>")
                .Append("</numFmts>");

            // 字体：正文 / 标题 / 灰字 / 表头 / 组标题 / 过期 / 强调。
            builder.Append("<fonts count=\"7\">")
                .Append(Font(11, bold: false, color: "FF1F2328"))
                .Append(Font(16, bold: true, color: "FF1F2328"))
                .Append(Font(10, bold: false, color: "FF6B7280"))
                .Append(Font(11, bold: true, color: "FFFFFFFF"))
                .Append(Font(11, bold: true, color: "FF1F2328"))
                .Append(Font(11, bold: true, color: "FFD92D20"))
                .Append(Font(11, bold: true, color: "FF2F6FEB"))
                .Append("</fonts>");

            // 填充：前两个是 Excel 的保留项（none / gray125），之后才是真正的底色。
            builder.Append("<fills count=\"5\">")
                .Append("<fill><patternFill patternType=\"none\"/></fill>")
                .Append("<fill><patternFill patternType=\"gray125\"/></fill>")
                .Append(SolidFill("FF2F6FEB"))
                .Append(SolidFill("FFF2F3F5"))
                .Append(SolidFill("FFFAFBFC"))
                .Append("</fills>");

            builder.Append("<borders count=\"2\">")
                .Append("<border><left/><right/><top/><bottom/><diagonal/></border>")
                .Append("<border>")
                .Append("<left style=\"thin\"><color rgb=\"FFE1E3E8\"/></left>")
                .Append("<right style=\"thin\"><color rgb=\"FFE1E3E8\"/></right>")
                .Append("<top style=\"thin\"><color rgb=\"FFE1E3E8\"/></top>")
                .Append("<bottom style=\"thin\"><color rgb=\"FFE1E3E8\"/></bottom>")
                .Append("<diagonal/></border>")
                .Append("</borders>");

            builder.Append("<cellStyleXfs count=\"1\">")
                .Append("<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/>")
                .Append("</cellStyleXfs>");

            builder.Append("<cellXfs count=\"").Append(_xfs.Count).Append("\">");
            foreach (var xf in _xfs)
                builder.Append(xf);
            builder.Append("</cellXfs>");

            builder.Append("<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>");
            builder.Append("</styleSheet>");

            return builder.ToString();
        }

        private static string Font(int size, bool bold, string color)
        {
            var builder = new StringBuilder(96);
            builder.Append("<font>");
            if (bold)
                builder.Append("<b/>");
            builder.Append("<sz val=\"").Append(size).Append("\"/>")
                .Append("<color rgb=\"").Append(color).Append("\"/>")
                .Append("<name val=\"Calibri\"/><family val=\"2\"/>")
                .Append("</font>");
            return builder.ToString();
        }

        private static string SolidFill(string color) =>
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"" + color
            + "\"/><bgColor indexed=\"64\"/></patternFill></fill>";
    }

    #endregion
}
