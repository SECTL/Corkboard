using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Media;
using Corkboard.Core.Models.Board;
using HtmlAgilityPack;
using Markdig;

namespace Corkboard.Core.Services.Board;

/// <summary>
///     旧数据的一次性内容迁移：把「Markdown 原文 + 按原文偏移的颜色/字号标注」转成富文本文档（HTML）。
///     <para>
///         旧作业的内容是 Markdown 原文，颜色/字号记在 <see cref="BoardNote.Formats" /> 的偏移上；
///         新作业的内容直接就是富文本文档，样式写在片段里的行内样式上（见 <see cref="BoardContentKind" />）。
///         两者不能长期并存——显示与编辑都必须知道内容该怎么解释——所以在加载时一次转干净：
///         Markdown 渲染成 HTML，标注按渲染后的文本找到对应片段，套上 <c>color</c> / <c>font-size</c> 行内样式。
///     </para>
///     <para>
///         标注是**旧原文的偏移**，而渲染会把 <c>**</c>、<c>`</c>、<c>#</c> 这些记号吃掉，位置对不上，
///         所以按「标过的原文片段」在渲染结果里顺序找一遍（<see cref="MapFormats" />）。
///         找不到的标注直接丢掉：宁可少一段颜色，也不要把样式套到别的字上。
///     </para>
///     <para>
///         转换是幂等的：内容已经是富文本的作业一律不动，所以重复启动、重复调用都不会二次转义。
///     </para>
/// </summary>
public static class LegacyBoardContentConverter
{
    /// <summary>旧渲染管线用的就是这一套（`.UseTaskLists().UseAutoLinks()`），迁移后观感保持一致。</summary>
    private static readonly Markdig.MarkdownPipeline Pipeline = new Markdig.MarkdownPipelineBuilder()
        .UseTaskLists()
        .UseAutoLinks()
        .Build();

    /// <summary>块级记号（引用、标题、列表项），只在行首出现。</summary>
    private static readonly Regex BlockMarker = new(
        @"^[ \t]*(?:[>#]+[ \t]*|[-+*][ \t]+|\d+[.][ \t]+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>链接与图片：渲染后只剩链接文字。</summary>
    private static readonly Regex LinkMarker = new(@"!?\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);

    /// <summary>加粗、斜体、删除线、行内代码的定界记号。</summary>
    private static readonly Regex InlineMarker = new(@"(\*\*|__|~~|`|\*|_)", RegexOptions.Compiled);

    /// <summary>
    ///     把所有还是 <see cref="BoardContentKind.Markdown" /> 的作业转成富文本，返回转了几条。
    ///     调用方负责落盘（见 <c>BoardService</c> 在加载后立刻保存一次）。
    /// </summary>
    public static int ConvertAll(IEnumerable<BoardNote> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);

        var converted = 0;
        foreach (var note in notes)
        {
            if (Convert(note))
                converted++;
        }

        return converted;
    }

    /// <summary>
    ///     转换一条作业，返回是否真的转了（内容本来就是富文本时什么也不做）。
    /// </summary>
    public static bool Convert(BoardNote note)
    {
        ArgumentNullException.ThrowIfNull(note);

        if (note.ContentKind == BoardContentKind.Html)
            return false;

        note.SetHtmlContent(BuildHtml(note.Content, note.Formats, note.ContentFontSize));
        return true;
    }

    private static string BuildHtml(string source, IReadOnlyList<BoardTextFormatRange>? formats, double? fontSize)
    {
        if (source.Length == 0)
            return string.Empty;

        var html = Markdig.Markdown.ToHtml(source, Pipeline).Trim();

        // 没有标注就不必过一遍 DOM：直接用 Markdig 的输出，少一层被改写的可能。
        if (formats is not { Count: > 0 })
            return WrapBaseFontSize(html, fontSize);

        var document = new HtmlDocument();
        document.LoadHtml(html);
        var body = document.DocumentNode.SelectSingleNode("//body") ?? document.DocumentNode;
        ApplyFormats(body, source, formats);
        return WrapBaseFontSize(body.InnerHtml.Trim(), fontSize);
    }

    /// <summary>
    ///     旧的「整篇字号」是整篇的默认值，等价于给每个有字的文本节点补一层行内字号
    ///     （块级 style 会被富文本编辑器丢掉，所以不能套 div）。
    ///     没设过（绝大多数旧数据）就不补，让内容继续跟随设置里的默认字号。
    /// </summary>
    private static string WrapBaseFontSize(string html, double? fontSize)
    {
        if (fontSize is not { } size || html.Length == 0)
            return html;

        return BoardHtmlStyling.Apply(html, BoardHtmlStyling.BuildFontSizeCss(size), null);
    }

    private static void ApplyFormats(HtmlNode body, string source, IReadOnlyList<BoardTextFormatRange> formats)
    {
        var (plainText, textNodes) = CollectText(body);
        if (plainText.Length == 0)
            return;

        var spans = MapFormats(source, plainText, formats);
        if (spans.Count == 0)
            return;

        // 逐文本节点套样式：切出来的片段都是同一个父节点的子节点，嵌套天然合法，
        // 也不会出现「跨了加粗边界、span 交叉」这种让解析器自作主张的写法。
        foreach (var (node, start, length) in textNodes)
        {
            var end = start + length;
            var pieces = new List<(int From, int To, BoardTextFormatRange Range)>();
            foreach (var span in spans)
            {
                if (span.Start >= end || span.Start + span.Length <= start)
                    continue;

                var from = Math.Max(span.Start, start) - start;
                var to = Math.Min(span.Start + span.Length, end) - start;
                if (to > from)
                    pieces.Add((from, to, span.Range));
            }

            if (pieces.Count > 0)
                ReplaceTextNode(node, pieces);
        }
    }

    /// <summary>渲染结果里的纯文本，以及每个文本节点对应的区间（按 <see cref="HtmlEntity.DeEntitize" /> 后的长度算）。</summary>
    private static (string PlainText, List<(HtmlTextNode Node, int Start, int Length)> Nodes) CollectText(HtmlNode root)
    {
        var builder = new StringBuilder();
        var nodes = new List<(HtmlTextNode Node, int Start, int Length)>();

        foreach (var node in root.Descendants())
        {
            if (node is not HtmlTextNode text)
                continue;

            var value = HtmlEntity.DeEntitize(text.Text);
            if (value.Length == 0)
                continue;

            nodes.Add((text, builder.Length, value.Length));
            builder.Append(value);
        }

        return (builder.ToString(), nodes);
    }

    private static List<(int Start, int Length, BoardTextFormatRange Range)> MapFormats(
        string source,
        string plainText,
        IReadOnlyList<BoardTextFormatRange> formats)
    {
        var mapped = new List<(int Start, int Length, BoardTextFormatRange Range)>();

        // 顺序找、不回退：标注按偏移排好序，映射结果也必须保持同样的先后，
        // 否则同一段文字会被后面的标注盖住。
        var cursor = 0;
        foreach (var range in formats.OrderBy(range => range.Start))
        {
            var from = Math.Clamp(range.Start, 0, source.Length);
            var to = Math.Clamp(range.Start + range.Length, from, source.Length);
            if (to == from)
                continue;

            var needle = ToPlainText(source[from..to]);
            if (needle.Length == 0)
                continue;

            var index = plainText.IndexOf(needle, cursor, StringComparison.Ordinal);
            if (index < 0)
                continue;

            mapped.Add((index, needle.Length, range));
            cursor = index + needle.Length;
        }

        return mapped;
    }

    /// <summary>把 Markdown 原文去掉记号，得到它在渲染结果里应该长成的样子。</summary>
    private static string ToPlainText(string markdown)
    {
        var text = BlockMarker.Replace(markdown, string.Empty);
        text = LinkMarker.Replace(text, "$1");
        return InlineMarker.Replace(text, string.Empty);
    }

    private static void ReplaceTextNode(HtmlTextNode node, List<(int From, int To, BoardTextFormatRange Range)> pieces)
    {
        var document = node.OwnerDocument;
        var parent = node.ParentNode;
        if (document is null || parent is null)
            return;

        var value = HtmlEntity.DeEntitize(node.Text);
        var position = 0;
        foreach (var (from, to, range) in pieces)
        {
            if (from > position)
                parent.InsertBefore(CreateText(document, value[position..from]), node);

            var span = document.CreateElement("span");
            span.SetAttributeValue("style", BuildStyle(range));
            span.AppendChild(CreateText(document, value[from..to]));
            parent.InsertBefore(span, node);
            position = to;
        }

        if (position < value.Length)
            parent.InsertBefore(CreateText(document, value[position..]), node);

        parent.RemoveChild(node);
    }

    /// <summary>序列化时文本节点不会自己转义，片段里的 <c>&amp;</c> / <c>&lt;</c> 要在这里补回去。</summary>
    private static HtmlTextNode CreateText(HtmlDocument document, string value)
    {
        return document.CreateTextNode(HtmlEntity.Entitize(value));
    }

    private static string BuildStyle(BoardTextFormatRange range)
    {
        var style = new StringBuilder();

        if (range.Color is { } color)
            style.Append("color:").Append(ToCssColor(color)).Append(';');

        if (range.FontSize is { } size)
        {
            style.Append("font-size:")
                .Append(BoardContentStyle.ClampFontSize(size).ToString("0.##", CultureInfo.InvariantCulture))
                .Append("px;");
        }

        return style.ToString();
    }

    /// <summary>
    ///     CSS 颜色：不透明用 <c>#RRGGBB</c>，带透明度用 <c>rgba(...)</c>。
    ///     注意本仓库落盘的十六进制是 <c>#RRGGBBAA</c>（alpha 在后），两套写法不要混。
    /// </summary>
    private static string ToCssColor(Color color)
    {
        if (color.A == byte.MaxValue)
            return string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");

        var alpha = (color.A / 255d).ToString("0.###", CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"rgba({color.R},{color.G},{color.B},{alpha})");
    }
}
