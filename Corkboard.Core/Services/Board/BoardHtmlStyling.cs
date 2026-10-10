using System.Globalization;
using System.Text;
using Avalonia.Media;
using Corkboard.Core.Models.Board;
using HtmlAgilityPack;

namespace Corkboard.Core.Services.Board;

/// <summary>
///     给内容 HTML 补 / 摘「默认样式」。
///     <para>
///         富文本编辑器（AvaloniaRichEditor）只在<b>行内 span</b> 上认样式：块级元素（p / div / body）的
///         style 会被它丢掉，而且它没有任何「默认文字颜色」属性，默认字号也只是渲染期的兜底。
///         所以「设置 → 作业板 → 内容默认样式」里的字号与颜色，只能在加载内容时逐段补成行内样式
///         （<see cref="Apply" />）。
///     </para>
///     <para>
///         编辑时补进去的样式会在保存时再摘掉（<see cref="Strip" />）：不然默认颜色/字号会被烘进文档，
///         换主题或改设置都不再跟着变。摘的时候是<b>按声明比对</b>的（忽略大小写与空白，逐条 style
///         声明看是不是我们补的那两条），所以既不需要依赖编辑器是否保留我们打的标记属性，
///         也不会碰用户自己加的颜色/字号，更不会把用户自己的 span 一起摘掉。
///     </para>
///     <para>
///         祖先里已经声明过 color / font-size 的文本不再补：用户自己设过的样式优先。
///     </para>
///     <para>
///         数值单位：界面上的字号是 DIP（像素），编辑器内部存的是 CSS pt，两者按 96 dpi 换算（1px = 0.75pt）。
///     </para>
/// </summary>
public static class BoardHtmlStyling
{
    /// <summary>补默认样式时打在 span 上的标记；只是给排查用的，<see cref="Strip" /> 不依赖它。</summary>
    public const string MarkerAttribute = "data-cb-def";

    /// <summary>
    ///     零宽空格（U+200B）：空块里的「颜色种子」。
    ///     <para>
    ///         库渲染输入法预编辑串（preedit）时，颜色取的是「光标处那个 run 的前景」，
    ///         取不到就退回写死的黑色（RichEditor.cs:4094 的 fallback、:4153-4158 的 preeditProps、
    ///         :4261 的 PreeditSourceProps）。空段落里一个 run 都没有，于是中文输入法里
    ///         「打出来还没按回车」的那些字在暗色主题下就是黑的。
    ///         塞一个带默认色的零宽 run 进去，预编辑串就继承到默认色了。
    ///     </para>
    ///     <para>
    ///         它是零宽的、看不见，回写前由 <see cref="StripSeeds" /> 摘掉，永远不会落盘。
    ///     </para>
    /// </summary>
    public const string SeedChar = "\u200B";

    private const string FontSizeProperty = "font-size";
    private const string ColorProperty = "color";

    /// <summary>可以装正文、但当前没有字的块级元素；只认叶子块，免得给容器也长出一个空段落。</summary>
    private static readonly HashSet<string> EmptyTextSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "td", "th", "li", "h1", "h2", "h3", "h4", "h5", "h6",
    };

    /// <summary>不带文字、但本身就是内容的东西：只数文字的话，一张图片会被当成「空文档」清掉。</summary>
    private static readonly HashSet<string> NonTextContentElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "img", "table", "hr", "video", "audio", "svg", "object", "iframe",
    };

    /// <summary>
    ///     摘掉种子零宽空格，还原成可以落盘的 HTML。
    ///     <para>
    ///         ⚠️ 编辑器的 <c>ToHtml()</c> 会把它序列化成实体（实测出来的是 <c>&amp;#8203;</c>），
    ///         所以不能只做一次字符串替换，得按文本节点解实体后再摘。
    ///     </para>
    /// </summary>
    public static string StripSeeds(string? html)
    {
        if (string.IsNullOrEmpty(html) || !MentionsSeed(html))
            return html ?? string.Empty;

        var document = new HtmlDocument();
        document.LoadHtml(html);

        foreach (var text in document.DocumentNode.Descendants().OfType<HtmlTextNode>().ToList())
        {
            // 文本节点里可能还是实体形态，先解实体再判断，避免把普通内容改花。
            var decoded = HtmlEntity.DeEntitize(text.Text);
            if (!decoded.Contains(SeedChar, StringComparison.Ordinal))
                continue;

            var cleaned = decoded.Replace(SeedChar, string.Empty, StringComparison.Ordinal);
            if (cleaned.Length == 0)
                text.Remove();
            else
                text.Text = cleaned;
        }

        return ExtractFragment(document, html);
    }

    /// <summary>HTML 里有没有种子（字面量或实体形态）。</summary>
    private static bool MentionsSeed(string html)
    {
        return html.Contains(SeedChar, StringComparison.Ordinal)
               || html.Contains("&#8203;", StringComparison.OrdinalIgnoreCase)
               || html.Contains("&#x200b;", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>HTML 里除了标签、空白与种子之外还有没有真内容（用来判断「这篇是空的」）。</summary>
    public static bool HasVisibleText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return false;

        var document = new HtmlDocument();
        document.LoadHtml(html);
        return HasVisibleContent(HtmlEntity.DeEntitize(document.DocumentNode.InnerText));
    }

    /// <summary>
    ///     HTML 里有没有「真内容」：有可见文字，或者有图片 / 表格 / 分隔线这类不带文字的内容。
    ///     <para>
    ///         ⚠️ 判断「这篇是不是空的」一律用它，不要只用 <see cref="HasVisibleText" />：
    ///         只数文字的话，一张图片会被当成空文档 —— 装填时被种子段落顶掉、回写时被清成空串，
    ///         表现就是「单独放一张图片上去存不住」。
    ///     </para>
    /// </summary>
    public static bool HasMeaningfulContent(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return false;

        var document = new HtmlDocument();
        document.LoadHtml(html);

        foreach (var node in document.DocumentNode.Descendants())
        {
            if (node.NodeType == HtmlNodeType.Element && NonTextContentElements.Contains(node.Name))
                return true;

            if (node.NodeType == HtmlNodeType.Text && HasVisibleContent(HtmlEntity.DeEntitize(node.InnerText)))
                return true;
        }

        return false;
    }

    /// <summary>纯文本里除了空白与种子之外还有没有真内容。</summary>
    public static bool HasVisibleContent(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (var ch in text)
        {
            if (ch == SeedChar[0] || char.IsWhiteSpace(ch))
                continue;

            return true;
        }

        return false;
    }

    /// <summary>DIP（界面上的字号）换算成 CSS pt（文档里存的字号）。</summary>
    public static double ToPoints(double dip) => dip * 0.75;

    /// <summary>CSS pt 换算回 DIP。</summary>
    public static double ToDip(double points) => points / 0.75;

    /// <summary>默认字号对应的 style 声明。</summary>
    public static string BuildFontSizeCss(double fontSizeDip)
    {
        var size = BoardContentStyle.ClampFontSize(fontSizeDip);
        return string.Create(CultureInfo.InvariantCulture, $"font-size:{ToPoints(size):0.##}pt");
    }

    /// <summary>默认颜色对应的 style 声明。</summary>
    public static string BuildColorCss(Color color)
    {
        return $"color:#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    /// <summary>
    ///     把默认样式补到每个有字的文本节点上；祖先已经声明过同名声明的部分不补。
    ///     补出来的 span 带 <see cref="MarkerAttribute" /> 标记，方便与用户自己的 span 区分。
    /// </summary>
    public static string Apply(string? html, string? fontSizeCss, string? colorCss)
    {
        if (string.IsNullOrWhiteSpace(html))
            return html ?? string.Empty;

        if (string.IsNullOrEmpty(fontSizeCss) && string.IsNullOrEmpty(colorCss))
            return html;

        var document = new HtmlDocument();
        document.LoadHtml(html);

        var texts = document.DocumentNode
            .Descendants()
            .OfType<HtmlTextNode>()
            .Where(node => !string.IsNullOrWhiteSpace(HtmlEntity.DeEntitize(node.Text)))
            .ToList();

        foreach (var text in texts)
        {
            var declared = CollectDeclaredProperties(text);

            var parts = new List<string>(2);
            if (!string.IsNullOrEmpty(fontSizeCss) && !declared.Contains(FontSizeProperty))
                parts.Add(fontSizeCss);

            // 链接文字的颜色由编辑器自己那层 data-are-fg 的 span 决定（盖过我们补的），补了等于白补。
            if (!string.IsNullOrEmpty(colorCss) && !declared.Contains(ColorProperty) && !IsLinkText(text))
                parts.Add(colorCss);

            if (parts.Count == 0)
                continue;

            var span = document.CreateElement("span");
            span.SetAttributeValue(MarkerAttribute, "1");
            span.SetAttributeValue("style", string.Join(";", parts));
            span.AppendChild(document.CreateTextNode(HtmlEntity.Entitize(text.Text)));
            text.ParentNode.ReplaceChild(span, text);
        }

        SeedEmptySlots(document, fontSizeCss, colorCss);

        return ExtractFragment(document, html);
    }

    /// <summary>
    ///     给「能装文字但当前没有字」的叶子块塞一个带默认色的零宽空格 run（见 <see cref="SeedChar" />）。
    /// </summary>
    private static void SeedEmptySlots(HtmlDocument document, string? fontSizeCss, string? colorCss)
    {
        var css = JoinCss(fontSizeCss, colorCss);
        if (css.Length == 0)
            return;

        foreach (var slot in document.DocumentNode.Descendants().Where(IsEmptyTextSlot).ToList())
        {
            var span = document.CreateElement("span");
            span.SetAttributeValue(MarkerAttribute, "1");
            span.SetAttributeValue("style", css);
            span.AppendChild(document.CreateTextNode(SeedChar));
            slot.AppendChild(span);
        }
    }

    private static string JoinCss(string? fontSizeCss, string? colorCss)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrEmpty(fontSizeCss))
            parts.Add(fontSizeCss);
        if (!string.IsNullOrEmpty(colorCss))
            parts.Add(colorCss);

        return string.Join(";", parts);
    }

    /// <summary>可以装正文、但当前是空的块级元素；里面还有别的元素（br 除外）时交给更里层那个去长种子。</summary>
    private static bool IsEmptyTextSlot(HtmlNode node)
    {
        if (node.NodeType != HtmlNodeType.Element || !EmptyTextSlots.Contains(node.Name))
            return false;

        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType != HtmlNodeType.Element)
                continue;

            if (!string.Equals(child.Name, "br", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return !HasVisibleContent(HtmlEntity.DeEntitize(node.InnerText));
    }

    /// <summary>把这次补进去的两条声明摘掉，还原成可以落盘的 HTML。</summary>
    public static string Strip(string? html, string? fontSizeCss, string? colorCss)
    {
        if (string.IsNullOrWhiteSpace(html))
            return html ?? string.Empty;

        var targets = new List<string>(2);
        AddTarget(targets, fontSizeCss);
        AddTarget(targets, colorCss);

        if (targets.Count == 0)
            return html;

        var document = new HtmlDocument();
        document.LoadHtml(html);

        var spans = document.DocumentNode.SelectNodes("//span[@style]")?.ToList();
        if (spans is not { Count: > 0 })
            return html;

        foreach (var span in spans)
        {
            var kept = new List<string>();
            var style = span.GetAttributeValue("style", string.Empty);
            foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = declaration.Trim();
                if (trimmed.Length == 0)
                    continue;

                // 是我们补的那条就丢掉，其余（用户自己设的）原样留着。
                if (!targets.Contains(Normalize(trimmed)))
                    kept.Add(trimmed);
            }

            if (kept.Count == 0)
            {
                // 这一层只剩我们补的样式：整层摘掉，内容留下。
                span.Attributes.Remove("style");
                span.ParentNode?.RemoveChild(span, keepGrandChildren: true);
            }
            else
            {
                span.SetAttributeValue("style", string.Join(";", kept));
                span.Attributes.Remove(MarkerAttribute);
            }
        }

        return ExtractFragment(document, html);

        static void AddTarget(List<string> list, string? css)
        {
            if (string.IsNullOrEmpty(css))
                return;

            foreach (var declaration in css.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = declaration.Trim();
                if (trimmed.Length > 0)
                    list.Add(Normalize(trimmed));
            }
        }
    }

    /// <summary>收集某个文本节点的祖先链里已经声明过的样式属性名。</summary>
    private static HashSet<string> CollectDeclaredProperties(HtmlTextNode text)
    {
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var node = text.ParentNode; node is not null; node = node.ParentNode)
        {
            if (node.NodeType != HtmlNodeType.Element)
                continue;

            var style = node.GetAttributeValue("style", string.Empty);
            foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = declaration.IndexOf(':');
                if (separator <= 0)
                    continue;

                declared.Add(declaration[..separator].Trim());
            }
        }

        return declared;
    }

    /// <summary>文本是不是链接里的：编辑器给链接补的那层颜色 span 会盖过我们补的默认色。</summary>
    private static bool IsLinkText(HtmlTextNode text)
    {
        for (var node = text.ParentNode; node is not null; node = node.ParentNode)
        {
            if (node.NodeType != HtmlNodeType.Element)
                continue;

            if (string.Equals(node.Name, "a", StringComparison.OrdinalIgnoreCase)
                || node.Attributes["data-are-fg"] is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>取出 body 里的片段；解析器没造出 body 时退回整棵树。</summary>
    private static string ExtractFragment(HtmlDocument document, string fallback)
    {
        var body = document.DocumentNode.SelectSingleNode("//body");
        var html = body?.InnerHtml ?? document.DocumentNode.InnerHtml;
        html = html.Trim();

        return html.Length > 0 ? html : fallback;
    }

    /// <summary>样式声明比较：忽略空白与大小写。</summary>
    private static string Normalize(string declaration)
    {
        var builder = new StringBuilder(declaration.Length);
        foreach (var ch in declaration)
        {
            if (!char.IsWhiteSpace(ch))
                builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }
}
