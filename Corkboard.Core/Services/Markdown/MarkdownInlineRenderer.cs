using System.Globalization;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Corkboard.Core.Models.Board;
using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using AvaloniaInline = Avalonia.Controls.Documents.Inline;
using MdInline = Markdig.Syntax.Inlines.Inline;

namespace Corkboard.Core.Services.Markdown;

/// <summary>
///     把作业内容渲染成 Avalonia 富文本 <see cref="AvaloniaInline" /> 序列：
///     Markdig 负责解析 Markdown，这里负责把它翻成 <c>Run</c> / <c>LineBreak</c>，
///     再把「颜色/字号标注」按原文偏移套到对应的字符上。
///     <para>
///         为什么不用现成的 Markdown 控件库：那些库绑死 Avalonia 11、而且只能整份定样式，
///         没法回答「第 12 到第 15 个字换色改字号」这个问题。渲染自己写还能保证一条底线——
///         <b>原文里的字一个都不能丢</b>，用户的作业内容首先是文本，其次才是样式。
///     </para>
///     <para>
///         换行按原文保留（Markdown 的软换行也当真换行）：作业多半是多行清单，
///         用户敲的回车必须看得见，不能被 CommonMark 的「软换行折成空格」吃掉。
///     </para>
///     <para>纯渲染、不碰其它状态；有单测（见 <c>MarkdownInlineRendererTests</c>）。</para>
/// </summary>
public static class MarkdownInlineRenderer
{
    /// <summary>
    ///     Markdown 解析管线。只开作业板上用得上的扩展，不接 HTML：
    ///     任务列表（<c>- [x]</c>）与裸链接自动识别，其余保持 CommonMark 默认。
    /// </summary>
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseTaskLists()
        .UseAutoLinks()
        .Build();

    /// <summary>行内代码与代码块用的等宽字体。系统里没有第一个就往后回退。</summary>
    private static readonly FontFamily MonospaceFont =
        new("Consolas,Cascadia Mono,Courier New,Menlo,DejaVu Sans Mono,monospace");

    /// <summary>引用块的竖线、无序列表的圆点、分隔线的字形。都是渲染器自己画的，不来自原文。</summary>
    private const string QuotePrefix = "▎ ";

    private const string BulletPrefix = "• ";

    private const string RuleText = "────────────────";

    /// <summary>图片在纯文本便签里没法显示，退化成一段说明文字。</summary>
    private const string ImagePrefix = "[图片] ";

    private static readonly IReadOnlyList<double> HeadingScales = [1.6, 1.4, 1.25, 1.15, 1.08, 1.0];

    /// <summary>渲染一份内容。空内容返回空表（控件会画成什么都不显示）。</summary>
    public static IReadOnlyList<AvaloniaInline> Render(BoardRichText? document)
    {
        if (document is null || document.Text.Length == 0)
            return [];

        var builder = new InlineBuilder(document);
        builder.WriteDocument(Markdig.Markdown.Parse(document.Text, Pipeline));
        return builder.Build();
    }

    /// <summary>一段文字当前的 Markdown 样式。字号是**算好的绝对值**（已经乘过标题级别）。</summary>
    private readonly record struct MarkdownStyle(
        double FontSize, bool Bold, bool Italic, bool Strikethrough, bool Code, bool Link)
    {
        public static MarkdownStyle Plain(double fontSize) => new(fontSize, false, false, false, false, false);
    }

    /// <summary>渲染中间态：一段带源偏移的文字，或一个换行。</summary>
    private abstract record Piece;

    private sealed record TextPiece(string Text, int SourceStart, MarkdownStyle Style) : Piece;

    private sealed record BreakPiece : Piece;

    /// <summary>
    ///     解析结果 → Inline 序列的搬运工：一边走 Markdig 的树，一边带着当前样式与源偏移。
    /// </summary>
    private sealed class InlineBuilder(BoardRichText document)
    {
        private readonly BoardRichText _document = document;
        private readonly double _baseFontSize = BoardContentStyle.ClampFontSize(document.BaseFontSize);
        private readonly List<Piece> _pieces = [];
        private MarkdownStyle _style = MarkdownStyle.Plain(BoardContentStyle.ClampFontSize(document.BaseFontSize));

        public void WriteDocument(MarkdownDocument markdown)
        {
            foreach (var block in markdown)
            {
                // 链接定义（[x]: http://…）只是给链接用的元数据，不该画出来。
                if (block is LinkReferenceDefinitionGroup)
                    continue;

                WriteBlock(block, 0);
            }
        }

        /// <summary>最后收口：丢掉结尾多出来的换行，把中间态换成真的 Inline。</summary>
        public IReadOnlyList<AvaloniaInline> Build()
        {
            while (_pieces.Count > 0 && _pieces[^1] is BreakPiece)
                _pieces.RemoveAt(_pieces.Count - 1);

            var inlines = new List<AvaloniaInline>(_pieces.Count);
            foreach (var piece in _pieces)
            {
                switch (piece)
                {
                    case BreakPiece:
                        inlines.Add(new LineBreak());
                        break;
                    case TextPiece text:
                        AppendRuns(inlines, text);
                        break;
                }
            }

            return inlines;
        }

        private void WriteBlock(Block block, int depth)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    WriteInlines(paragraph.Inline);
                    AddBreak();
                    break;

                case HeadingBlock heading:
                    WriteHeading(heading);
                    break;

                case ListBlock list:
                    WriteList(list, depth);
                    break;

                case QuoteBlock quote:
                    WriteQuote(quote, depth);
                    break;

                case CodeBlock code:
                    WriteCode(code, depth);
                    AddBreak();
                    break;

                case ThematicBreakBlock:
                    AddGenerated(RuleText);
                    AddBreak();
                    break;

                case HtmlBlock html:
                    WriteRawLines(html, depth);
                    AddBreak();
                    break;

                case ContainerBlock container:
                    foreach (var child in container)
                        WriteBlock(child, depth);
                    break;

                case LeafBlock leaf:
                    WriteRawLines(leaf, depth);
                    AddBreak();
                    break;
            }
        }

        private void WriteHeading(HeadingBlock heading)
        {
            var previous = _style;
            var scale = heading.Level >= 1 && heading.Level <= HeadingScales.Count
                ? HeadingScales[heading.Level - 1]
                : 1d;

            _style = _style with { Bold = true, FontSize = _baseFontSize * scale };
            WriteInlines(heading.Inline);
            _style = previous;
            AddBreak();
        }

        private void WriteList(ListBlock list, int depth)
        {
            foreach (var item in list)
            {
                if (item is not ListItemBlock listItem)
                    continue;

                AddGenerated(new string(' ', depth * 2));

                // 任务列表项自带勾选框，再画一个圆点就是「• ☑ 背单词」这种叠字。
                if (!IsTaskListItem(listItem))
                {
                    AddGenerated(list.IsOrdered
                        ? (listItem.Order > 0 ? listItem.Order : 1).ToString(CultureInfo.InvariantCulture) + ". "
                        : BulletPrefix);
                }

                // 列表项里通常是段落；嵌套列表再往里缩一层。
                foreach (var child in listItem)
                {
                    if (child is ListBlock nested)
                    {
                        AddBreak();
                        WriteList(nested, depth + 1);
                    }
                    else
                    {
                        WriteBlock(child, depth + 1);
                    }
                }

                AddBreak();
            }
        }

        /// <summary>列表项开头是不是任务勾选框（<c>- [x]</c>）。是的话就不再画圆点。</summary>
        private static bool IsTaskListItem(ListItemBlock item)
        {
            return item.Count > 0
                   && item[0] is LeafBlock leaf
                   && leaf.Inline?.FirstChild is TaskList;
        }

        private void WriteQuote(QuoteBlock quote, int depth)
        {
            foreach (var child in quote)
            {
                AddGenerated(new string(' ', depth * 2) + QuotePrefix);
                WriteBlock(child, depth);
            }

            AddBreak();
        }

        private void WriteCode(CodeBlock code, int depth)
        {
            var previous = _style;
            _style = _style with { Code = true };

            var lines = code.Lines.Lines;
            for (var i = 0; i < code.Lines.Count; i++)
            {
                var slice = lines[i].Slice;
                AddGenerated(new string(' ', depth * 2));
                AddText(slice.ToString(), slice.Start);
                AddBreak();
            }

            _style = previous;
        }

        /// <summary>把一块的原始行原样画出来（HTML 块、以及没专门处理的叶子块）。</summary>
        private void WriteRawLines(LeafBlock leaf, int depth)
        {
            var lines = leaf.Lines.Lines;
            for (var i = 0; i < leaf.Lines.Count; i++)
            {
                var slice = lines[i].Slice;
                AddGenerated(new string(' ', depth * 2));
                AddText(slice.ToString(), slice.Start);

                if (i + 1 < leaf.Lines.Count)
                    AddBreak();
            }
        }

        private void WriteInlines(ContainerInline? container)
        {
            if (container is null)
                return;

            for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
                WriteInline(inline);
        }

        private void WriteInline(MdInline inline)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    AddText(literal.Content.ToString(), ResolveOffset(literal.Content.Text, literal.Content.Start));
                    break;

                case CodeInline code:
                {
                    var previous = _style;
                    _style = _style with { Code = true };

                    // CodeInline 不给内容切片，只能从整段的 Span 往前跳掉反引号。
                    AddText(code.Content, code.Span.Start < 0 ? -1 : code.Span.Start + code.DelimiterCount);
                    _style = previous;
                    break;
                }

                case LineBreakInline:
                    // 软换行也当真换行：作业是多行清单，用户敲的回车不能被折成空格。
                    AddBreak();
                    break;

                case EmphasisInline emphasis:
                    WriteEmphasis(emphasis);
                    break;

                case LinkInline link:
                    WriteLink(link);
                    break;

                case AutolinkInline autolink:
                {
                    var previous = _style;
                    _style = _style with { Link = true };

                    // Span 就是原文偏移；Markdig 认不出位置时它是 -1，格式标注自然就不往上套。
                    AddText(autolink.Url, autolink.Span.Start);
                    _style = previous;
                    break;
                }

                case HtmlInline html:
                    AddText(html.Tag, html.Span.Start);
                    break;

                case TaskList task:
                    // 只画勾选框本身：Markdig 把 "[x]" 后面的空格留在了下一个文本节点里，
                    // 这里再补一个空格就成了两个。
                    AddGenerated(task.Checked ? "☑" : "☐");
                    break;

                case ContainerInline container:
                    // 剩下没专门处理的容器（含没用上的强调定界符）照原样往下走，
                    // 宁可少一层样式，也不能把字弄丢。
                    WriteInlines(container);
                    break;
            }
        }

        private void WriteEmphasis(EmphasisInline emphasis)
        {
            var previous = _style;
            _style = emphasis.DelimiterChar switch
            {
                '~' => _style with { Strikethrough = true },
                '*' or '_' => _style with
                {
                    Bold = emphasis.DelimiterCount >= 2,
                    Italic = emphasis.DelimiterCount % 2 == 1
                },
                _ => _style
            };

            WriteInlines(emphasis);
            _style = previous;
        }

        private void WriteLink(LinkInline link)
        {
            var previous = _style;
            _style = _style with { Link = true };

            if (link.IsImage)
                AddGenerated(ImagePrefix);

            // 空链接文字（常见于图片与裸链接）退化成显示地址，别画成一段空白。
            if (link.FirstChild is null)
                AddGenerated(string.IsNullOrWhiteSpace(link.Url) ? link.Label ?? string.Empty : link.Url);
            else
                WriteInlines(link);

            _style = previous;
        }

        /// <summary>Markdig 给的是**整份原文**上的偏移；切片不是同一份字符串时宁可放弃对位，也不能错位。</summary>
        private int ResolveOffset(string? sourceText, int start)
        {
            return sourceText is not null && ReferenceEquals(sourceText, _document.Text) ? start : -1;
        }

        private void AddText(string? text, int sourceStart)
        {
            if (string.IsNullOrEmpty(text))
                return;

            _pieces.Add(new TextPiece(text, sourceStart, _style));
        }

        /// <summary>渲染器自己生成的文字（列表圆点、引用竖线…）：没有源偏移，格式标注管不到它。</summary>
        private void AddGenerated(string text) => AddText(text, -1);

        private void AddBreak()
        {
            if (_pieces.Count > 0 && _pieces[^1] is not BreakPiece)
                _pieces.Add(new BreakPiece());
        }

        /// <summary>
        ///     把一段文字按标注的边界切开，逐段决定最终颜色与字号。
        ///     标注只带偏移，所以这里做的就是把「第几个字到第几个字」翻译成「这几个 Run 长什么样」。
        /// </summary>
        private void AppendRuns(List<AvaloniaInline> inlines, TextPiece piece)
        {
            var text = piece.Text;
            if (text.Length == 0)
                return;

            var cuts = new SortedSet<int> { 0, text.Length };
            if (piece.SourceStart >= 0)
            {
                foreach (var range in _document.Formats)
                {
                    var start = range.Start - piece.SourceStart;
                    var end = range.End - piece.SourceStart;
                    if (end <= 0 || start >= text.Length)
                        continue;

                    cuts.Add(Math.Clamp(start, 0, text.Length));
                    cuts.Add(Math.Clamp(end, 0, text.Length));
                }
            }

            var points = cuts.ToArray();
            for (var i = 0; i + 1 < points.Length; i++)
            {
                var offset = points[i];
                var length = points[i + 1] - offset;
                if (length <= 0)
                    continue;

                var (color, fontSize) = piece.SourceStart >= 0
                    ? ResolveOverride(piece.SourceStart + offset)
                    : (null, null);

                var run = new Run { Text = text.Substring(offset, length) };

                // 字号始终显式写出来：作业内容的字号来自设置里的默认值/整篇字号，
                // 不该跟着壳的默认字号走（用户换界面字体大小时，作业板上的字不能跟着乱跳）。
                run.FontSize = fontSize is { } size
                    ? BoardContentStyle.ClampFontSize(size)
                    : piece.Style.FontSize;

                if (piece.Style.Bold)
                    run.FontWeight = FontWeight.Bold;

                if (piece.Style.Italic)
                    run.FontStyle = FontStyle.Italic;

                if (piece.Style.Strikethrough)
                    run.TextDecorations = TextDecorations.Strikethrough;

                if (piece.Style.Link)
                    run.TextDecorations = TextDecorations.Underline;

                if (piece.Style.Code)
                    run.FontFamily = MonospaceFont;

                // 没被标注指定颜色时用整篇默认色；默认色也是空就继承主题前景色（深色模式下才看得见）。
                var effectiveColor = color ?? _document.BaseColor;
                if (effectiveColor is { } foreground)
                    run.Foreground = new SolidColorBrush(foreground);

                inlines.Add(run);
            }
        }

        /// <summary>某个源下标处生效的覆盖：最后一条盖住它的标注说了算（重叠时靠后的赢）。</summary>
        private (Color? Color, double? FontSize) ResolveOverride(int sourceIndex)
        {
            Color? color = null;
            double? fontSize = null;

            foreach (var range in _document.Formats)
            {
                if (range.Start <= sourceIndex && sourceIndex < range.End)
                {
                    color = range.Color;
                    fontSize = range.FontSize;
                }
            }

            return (color, fontSize);
        }
    }
}
