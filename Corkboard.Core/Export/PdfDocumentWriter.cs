using System.Globalization;
using System.Text;

namespace Corkboard.Core.Export;

/// <summary>一页 PDF：整页画一张 JPEG（尺寸单位是 PDF point，1pt = 1/72 英寸），可选书签标题。</summary>
public sealed record PdfPageImage(byte[] JpegData, double WidthPoints, double HeightPoints, string? OutlineTitle = null);

/// <summary>
///     极小的 PDF 1.4 组装器：只支持「整页一张 JPEG」的页面与顶层书签，够导出作业板用。
///     <para>
///         之所以自己拼：SkiaSharp 的 <c>SKDocument</c> 不支持 outline（书签目录），而作业板的科目
///         正好要当书签用。这里不引任何第三方 PDF 库，只按规范写字节。
///     </para>
///     <para>
///         只写不读、无 UI / IO 依赖：调用方给一个可写且可定位的流，这里只负责往里写，不关闭它。
///         写出的字节里没有 <see cref="Environment.NewLine" />，换行一律是 <c>\n</c>。
///     </para>
/// </summary>
public static class PdfDocumentWriter
{
    /// <summary>PDF 文件头第二行的二进制注释（高字节说明这是二进制文件），必须原样写字节。</summary>
    private static readonly byte[] BinaryComment = [0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A];

    /// <summary>把页写进 <paramref name="output" />（不负责关闭流）。<paramref name="documentTitle" /> 可空。</summary>
    /// <param name="output">目标流；需要可定位（要读 <see cref="Stream.Position" /> 记 xref 偏移）。</param>
    /// <param name="pages">页面列表，可以是空的（输出一个合法的 0 页 PDF）。</param>
    /// <param name="documentTitle">写进 Info 字典的文档标题，可空。</param>
    /// <exception cref="ArgumentException">某一页的 JPEG 数据为空时抛出，消息里带页号。</exception>
    public static void Write(Stream output, IReadOnlyList<PdfPageImage> pages, string? documentTitle = null)
    {
        ArgumentNullException.ThrowIfNull(output);

        var pageList = pages ?? [];
        for (var index = 0; index < pageList.Count; index++)
        {
            var page = pageList[index];
            if (page is null || page.JpegData is not { Length: > 0 })
                throw new ArgumentException($"第 {index + 1} 页的 JPEG 数据为空，无法写进 PDF。", nameof(pages));
        }

        // 有标题的页按顺序进书签目录（页码用的是 0 基下标）。
        var bookmarks = new List<(int PageIndex, string Title)>();
        for (var index = 0; index < pageList.Count; index++)
        {
            var title = pageList[index].OutlineTitle;
            if (!string.IsNullOrWhiteSpace(title))
                bookmarks.Add((index, title));
        }

        var hasOutlines = bookmarks.Count > 0;

        // 先把对象号排好，正文里才能互相引用（PDF 允许对象乱序，但 xref 要按号登记偏移）。
        var objectNumber = 0;
        var catalogNumber = ++objectNumber;

        var outlinesNumber = 0;
        if (hasOutlines)
            outlinesNumber = ++objectNumber;

        var outlineNumbers = new int[bookmarks.Count];
        for (var i = 0; i < outlineNumbers.Length; i++)
            outlineNumbers[i] = ++objectNumber;

        var pagesNumber = ++objectNumber;

        var pageNumbers = new int[pageList.Count];
        var contentNumbers = new int[pageList.Count];
        var imageNumbers = new int[pageList.Count];
        for (var i = 0; i < pageList.Count; i++)
        {
            pageNumbers[i] = ++objectNumber;
            contentNumbers[i] = ++objectNumber;
            imageNumbers[i] = ++objectNumber;
        }

        var infoNumber = ++objectNumber;
        var bodies = new byte[]?[objectNumber + 1];

        // ---- Catalog：页面树；有书签时挂上 /Outlines 并让阅读器默认显示书签栏 ----
        var catalog = new StringBuilder("<< /Type /Catalog /Pages ").Append(Ref(pagesNumber));
        if (hasOutlines)
            catalog.Append(" /Outlines ").Append(Ref(outlinesNumber)).Append(" /PageMode /UseOutlines");
        catalog.Append(" >>");
        bodies[catalogNumber] = Ascii(catalog.ToString());

        // ---- 书签：顶层 Outlines + 每项一个 OutlineItem ----
        if (hasOutlines)
        {
            var outlines = new StringBuilder("<< /Type /Outlines /First ")
                .Append(Ref(outlineNumbers[0]))
                .Append(" /Last ")
                .Append(Ref(outlineNumbers[^1]))
                .Append(" /Count ")
                .Append(Int(bookmarks.Count))
                .Append(" >>");
            bodies[outlinesNumber] = Ascii(outlines.ToString());

            for (var i = 0; i < bookmarks.Count; i++)
            {
                var (pageIndex, title) = bookmarks[i];
                var pageHeight = NormalizePoints(pageList[pageIndex].HeightPoints);

                var item = new StringBuilder("<< /Title <")
                    .Append(ToUtf16BeHex(title))
                    .Append("> /Parent ")
                    .Append(Ref(outlinesNumber))
                    .Append(" /Dest [")
                    .Append(Ref(pageNumbers[pageIndex]))
                    .Append(" /XYZ 0 ")
                    .Append(Number(pageHeight))
                    .Append(" 0]");

                // 第一项没有前驱、最后一项没有后继，其余两头都连上，阅读器才能上下跳。
                if (i > 0)
                    item.Append(" /Prev ").Append(Ref(outlineNumbers[i - 1]));
                if (i + 1 < bookmarks.Count)
                    item.Append(" /Next ").Append(Ref(outlineNumbers[i + 1]));

                item.Append(" >>");
                bodies[outlineNumbers[i]] = Ascii(item.ToString());
            }
        }

        // ---- Pages：页面树 ----
        var pagesBody = new StringBuilder("<< /Type /Pages /Kids [");
        for (var i = 0; i < pageList.Count; i++)
        {
            if (i > 0)
                pagesBody.Append(' ');
            pagesBody.Append(Ref(pageNumbers[i]));
        }

        pagesBody.Append("] /Count ").Append(Int(pageList.Count)).Append(" >>");
        bodies[pagesNumber] = Ascii(pagesBody.ToString());

        // ---- 每页：Page + 内容 stream + JPEG 图像 XObject ----
        for (var i = 0; i < pageList.Count; i++)
        {
            var page = pageList[i];
            var width = NormalizePoints(page.WidthPoints);
            var height = NormalizePoints(page.HeightPoints);

            var pageBody = new StringBuilder("<< /Type /Page /Parent ")
                .Append(Ref(pagesNumber))
                .Append(" /MediaBox [0 0 ")
                .Append(Number(width))
                .Append(' ')
                .Append(Number(height))
                .Append("] /Resources << /XObject << /Im0 ")
                .Append(Ref(imageNumbers[i]))
                .Append(" >> >> /Contents ")
                .Append(Ref(contentNumbers[i]))
                .Append(" >>");
            bodies[pageNumbers[i]] = Ascii(pageBody.ToString());

            // 整页铺一张图：cm 把单位正方形缩放到页面大小，Do 画出来。
            var content = Ascii($"q {Number(width)} 0 0 {Number(height)} 0 0 cm /Im0 Do Q");
            bodies[contentNumbers[i]] = BuildStreamBody($"<< /Length {Int(content.Length)} >>", content);

            var jpeg = page.JpegData;
            var pixelWidth = 1;
            var pixelHeight = 1;
            if (TryReadJpegSize(jpeg, out var w, out var h))
            {
                pixelWidth = w;
                pixelHeight = h;
            }

            var image = new StringBuilder("<< /Type /XObject /Subtype /Image /Width ")
                .Append(Int(pixelWidth))
                .Append(" /Height ")
                .Append(Int(pixelHeight))
                .Append(" /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length ")
                .Append(Int(jpeg.Length))
                .Append(" >>");
            bodies[imageNumbers[i]] = BuildStreamBody(image.ToString(), jpeg);
        }

        // ---- Info：标题可空，Producer 固定写自己 ----
        var info = new StringBuilder("<< ");
        if (!string.IsNullOrWhiteSpace(documentTitle))
            info.Append("/Title <").Append(ToUtf16BeHex(documentTitle)).Append("> ");
        info.Append("/Producer (Corkboard) >>");
        bodies[infoNumber] = Ascii(info.ToString());

        // ---- 落盘：文件头 + 顺序写对象并记偏移 + xref + trailer ----
        var startOffset = output.Position;

        WriteAscii(output, "%PDF-1.4\n");
        output.Write(BinaryComment, 0, BinaryComment.Length);

        var offsets = new long[objectNumber + 1];
        for (var number = 1; number <= objectNumber; number++)
        {
            offsets[number] = output.Position - startOffset;
            WriteAscii(output, $"{Int(number)} 0 obj\n");
            var body = bodies[number] ?? throw new InvalidOperationException($"PDF 对象 {number} 没有正文。");
            output.Write(body, 0, body.Length);
            WriteAscii(output, "\nendobj\n");
        }

        var xrefOffset = output.Position - startOffset;
        var xref = new StringBuilder();
        xref.Append("xref\n0 ").Append(Int(objectNumber + 1)).Append('\n');
        // 第 0 项是链表头，固定空闲；每项必须恰好 20 字节（「空格 + 换行」结尾）。
        xref.Append("0000000000 65535 f \n");
        for (var number = 1; number <= objectNumber; number++)
            xref.Append(Offset(offsets[number])).Append(" 00000 n \n");

        xref.Append("trailer\n<< /Size ").Append(Int(objectNumber + 1))
            .Append(" /Root ").Append(Ref(catalogNumber))
            .Append(" /Info ").Append(Ref(infoNumber))
            .Append(" >>\nstartxref\n")
            .Append(xrefOffset.ToString(CultureInfo.InvariantCulture))
            .Append("\n%%EOF");
        WriteAscii(output, xref.ToString());
    }

    /// <summary>把对象字典 + 裸数据拼成一个 stream 对象正文（不含 <c>N 0 obj</c> 外壳）。</summary>
    private static byte[] BuildStreamBody(string dictionary, byte[] data)
    {
        var header = Ascii(dictionary + "\nstream\n");
        var footer = Ascii("\nendstream");

        var result = new byte[header.Length + data.Length + footer.Length];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        Buffer.BlockCopy(data, 0, result, header.Length, data.Length);
        Buffer.BlockCopy(footer, 0, result, header.Length + data.Length, footer.Length);
        return result;
    }

    /// <summary>
    ///     从 JPEG 里读像素宽高（SOF 段）。图像字典里的 /Width /Height 必须是解码后的像素尺寸，
    ///     不能拿页面 point 尺寸充数；读不到时退回 1×1（DCTDecode 的数据本身仍带真实尺寸）。
    /// </summary>
    private static bool TryReadJpegSize(byte[] data, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            return false;

        var index = 2;
        while (index + 1 < data.Length)
        {
            if (data[index] != 0xFF)
            {
                index++;
                continue;
            }

            var marker = data[index + 1];
            index += 2;

            // 无长度的标记：SOI / EOI / RSTn / TEM。
            if (marker == 0xD8 || marker == 0xD9 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                continue;

            // SOS 之后是熵编码数据，里面可能混着 0xFF，再扫下去会误判，而且 SOF 一定在它之前。
            if (marker == 0xDA)
                return false;

            if (index + 1 >= data.Length)
                return false;

            var length = (data[index] << 8) | data[index + 1];
            if (length < 2 || index + length > data.Length)
                return false;

            // SOF0..SOF15 里，C4（DHT）/ C8（JPG）/ CC（DAC）不是帧头。
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (length < 7)
                    return false;

                height = (data[index + 3] << 8) | data[index + 4];
                width = (data[index + 5] << 8) | data[index + 6];
                return width > 0 && height > 0;
            }

            index += length;
        }

        return false;
    }

    /// <summary>PDF 里引用的对象：<c>3 0 R</c>。</summary>
    private static string Ref(int objectNumber)
    {
        return $"{Int(objectNumber)} 0 R";
    }

    /// <summary>整数一律走 InvariantCulture，不跟着系统区域变。</summary>
    private static string Int(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>实数保留最多 3 位小数，用点做小数点。</summary>
    private static string Number(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>xref 里的 10 位偏移。</summary>
    private static string Offset(long value)
    {
        return value.ToString("D10", CultureInfo.InvariantCulture);
    }

    /// <summary>页面尺寸坏了（非正 / NaN）时退回 1pt，保证 MediaBox 始终合法。</summary>
    private static double NormalizePoints(double value)
    {
        return double.IsFinite(value) && value > 0 ? value : 1;
    }

    /// <summary>把字符串的 UTF-16BE 字节写成 PDF 十六进制串（带 FEFF BOM），中文标题必须这么写。</summary>
    private static string ToUtf16BeHex(string text)
    {
        var builder = new StringBuilder(4 + text.Length * 4);
        builder.Append("FEFF");
        foreach (var ch in text)
            builder.Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    private static byte[] Ascii(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }

    private static void WriteAscii(Stream output, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        output.Write(bytes, 0, bytes.Length);
    }
}
