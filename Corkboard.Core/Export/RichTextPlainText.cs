using System.Net;
using System.Text;

namespace Corkboard.Core.Export;

/// <summary>
///     把富文本 HTML 片段转成纯文本，给表格的「内容」列用。
///     <para>
///         作业内容是所见即所得编辑器的 HTML（<c>&lt;p&gt;</c> / <c>&lt;br&gt;</c> / 行内
///         <c>&lt;span style&gt;</c>），而单元格里要的是能看、能复制、能搜的字。
///         规则：块级标签与 <c>&lt;br&gt;</c> 变行、其余标签去掉、实体解码、多余空白压掉。
///     </para>
///     <para>
///         刻意手写而不是拉 <c>HtmlAgilityPack</c>：这里只需要「摊平成文字」，
///         一个确定性的字符扫描就够，还能保证 <c>&lt;p&gt;甲&lt;/p&gt;&lt;p&gt;乙&lt;/p&gt;</c> 与
///         <c>甲&lt;br&gt;乙</c> 得到同一份结果。
///     </para>
/// </summary>
public static class RichTextPlainText
{
    /// <summary>出现这些标签的位置要断行（开始或结束都一样：段落就是一段一段的）。</summary>
    private static readonly string[] BlockTags =
    [
        "p", "div", "br", "li", "ul", "ol", "tr", "td", "th", "table", "blockquote",
        "h1", "h2", "h3", "h4", "h5", "h6", "pre", "hr", "section", "article"
    ];

    /// <summary>整块丢掉内容（不是断行，是里面的字都不要）的标签。</summary>
    private static readonly string[] DroppedTags = ["script", "style", "head", "title"];

    /// <summary>
    ///     转成纯文本。段落之间与 <c>&lt;br&gt;</c> 之间都变成一个换行；连续空行压成一个，
    ///     行首行尾空白去掉，整串再去掉首尾空白。输入为空 / 全空白时返回空串。
    /// </summary>
    public static string Convert(string? html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        // 纯文本（旧数据里没有标签的那种）也要走同一条归一，免得两套空白规则。
        var builder = new StringBuilder(html.Length);
        var index = 0;
        while (index < html.Length)
        {
            var open = html.IndexOf('<', index);
            if (open < 0)
            {
                builder.Append(html, index, html.Length - index);
                break;
            }

            builder.Append(html, index, open - index);

            var close = html.IndexOf('>', open + 1);
            if (close < 0)
            {
                // 残缺标签：剩下的部分当文字（去掉那个孤零零的 '<'）。
                builder.Append(html, open + 1, html.Length - open - 1);
                break;
            }

            var tag = html.AsSpan(open + 1, close - open - 1).Trim();
            var name = ReadTagName(tag);

            if (Array.IndexOf(DroppedTags, name) >= 0)
            {
                // 成对出现的丢内容标签：整段跳到它的结束标签之后。
                var end = html.IndexOf("</" + name, close + 1, StringComparison.OrdinalIgnoreCase);
                if (end < 0)
                {
                    index = close + 1;
                    continue;
                }

                var endClose = html.IndexOf('>', end);
                index = endClose < 0 ? html.Length : endClose + 1;
                continue;
            }

            if (Array.IndexOf(BlockTags, name) >= 0)
                builder.Append('\n');

            index = close + 1;
        }

        return Normalize(builder.ToString());
    }

    /// <summary>取标签名（小写）：<c>&lt;/p style="x"&gt;</c> 里的 <c>p</c>、<c>&lt;br/&gt;</c> 里的 <c>br</c>。</summary>
    private static string ReadTagName(ReadOnlySpan<char> tag)
    {
        var start = 0;
        if (tag.Length > 0 && tag[0] == '/')
            start = 1;

        var end = start;
        while (end < tag.Length && (char.IsLetterOrDigit(tag[end]) || tag[end] == '!'))
            end++;

        if (end == start)
            return string.Empty;

        var name = tag[start..end].ToString().ToLowerInvariant();

        // <!DOCTYPE ...> / 注释之类：当「不是标签」处理，直接丢掉。
        return name.StartsWith('!') ? string.Empty : name;
    }

    /// <summary>
    ///     解实体 + 压空白：先按行切（这样 <c>&amp;nbsp;</c> 解出来的空格不会被当成段落分隔），
    ///     再逐行把连续空白压成一个空格、去掉行首行尾空白，最后丢掉空行。
    /// </summary>
    private static string Normalize(string text)
    {
        var lines = new List<string>();
        var builder = new StringBuilder();

        void Flush()
        {
            var line = WebUtility.HtmlDecode(builder.ToString());
            builder.Clear();

            var squeezed = new StringBuilder(line.Length);
            var lastWasSpace = false;
            foreach (var character in line)
            {
                var isSpace = char.IsWhiteSpace(character);
                if (isSpace)
                {
                    if (!lastWasSpace)
                        squeezed.Append(' ');

                    lastWasSpace = true;
                    continue;
                }

                squeezed.Append(character);
                lastWasSpace = false;
            }

            var trimmed = squeezed.ToString().Trim();
            if (trimmed.Length > 0)
                lines.Add(trimmed);
        }

        foreach (var character in text)
        {
            if (character == '\n')
            {
                Flush();
                continue;
            }

            builder.Append(character);
        }

        Flush();

        return string.Join("\n", lines);
    }
}
