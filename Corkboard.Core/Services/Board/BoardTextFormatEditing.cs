using Avalonia.Media;
using Corkboard.Core.Models.Board;

namespace Corkboard.Core.Services.Board;

/// <summary>
///     作业内容格式标注的编辑逻辑：文本一变就把标注挪到新位置，给一段文字套颜色/字号，
///     以及算「这次操作该落到哪一段」。
///     <para>
///         全是纯计算，不碰 UI，有单测（见 <c>BoardTextFormatEditingTests</c>）。
///         界面层只负责把 <c>TextBox</c> 的选区/光标位置递进来、把结果换成预览。
///     </para>
/// </summary>
public static class BoardTextFormatEditing
{
    /// <summary>
    ///     文本被编辑之后，把每个标注挪到新文本里的对应位置。
    ///     <para>
    ///         做法是最朴素的「掐掉公共前缀与公共后缀」：剩下的那一小截就是这次真正被替换掉的区间，
    ///         区间之前的标注原地不动、之后的整体平移，跨过区间的标注按新长度伸缩。
    ///         这样打字、退格、粘贴、整段替换都能对上，不需要给 <c>TextBox</c> 挂文本变更事件流。
    ///     </para>
    ///     <para>
    ///         两个边界约定：点在替换区间左端时，标注的**起点**留在原地（左边那段不跟着长）、
    ///         **终点**把新插入的内容包进来（接着往下打字仍然带着格式）；
    ///         纯插入时点上的起点则推到插入内容之后，免得左边那段的格式被新字抢走。
    ///     </para>
    /// </summary>
    /// <returns>新的标注列表（原来那些对象不会被改动）。旧的标注被删光时返回空表。</returns>
    public static List<BoardTextFormatRange> Shift(
        IReadOnlyList<BoardTextFormatRange> ranges, string oldText, string newText)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        oldText ??= string.Empty;
        newText ??= string.Empty;

        if (ranges.Count == 0)
            return [];

        if (string.Equals(oldText, newText, StringComparison.Ordinal))
            return [.. ranges.Select(range => range.Clone())];

        var oldLength = oldText.Length;
        var newLength = newText.Length;

        // 公共前缀：从头开始一样的那一段，它原封不动。
        var left = 0;
        while (left < oldLength && left < newLength && oldText[left] == newText[left])
            left++;

        // 公共后缀：从尾开始一样的那一段（不与前缀重叠）。
        var right = 0;
        while (right < oldLength - left
               && right < newLength - left
               && oldText[oldLength - 1 - right] == newText[newLength - 1 - right])
        {
            right++;
        }

        var replacedOldEnd = oldLength - right;   // 旧文本里被替换区间的结束（不含）
        var replacedNewEnd = newLength - right;   // 新文本里被替换区间的结束（不含）
        var delta = newLength - oldLength;
        var isPureInsertion = replacedOldEnd == left;

        var shifted = new List<BoardTextFormatRange>(ranges.Count);
        foreach (var range in ranges)
        {
            var start = Math.Clamp(MapLeft(range.Start), 0, newLength);
            var end = Math.Clamp(MapRight(range.End), start, newLength);

            // 整段被删掉的标注直接丢：留着也只是一条长度为零的空壳。
            if (end <= start)
                continue;

            shifted.Add(new BoardTextFormatRange
            {
                Start = start,
                Length = end - start,
                Color = range.Color,
                FontSize = range.FontSize
            });
        }

        return shifted;

        // 标注起点：落在被替换区间左端点上时原地不动（纯插入时推到插入内容之后），
        // 落在右端点上时跟到新区间末尾，落在区间内部时收到左端点。
        int MapLeft(int position)
        {
            if (position < left)
                return position;

            if (position > replacedOldEnd)
                return position + delta;

            return position == replacedOldEnd ? replacedNewEnd : left;
        }

        // 标注终点：左端点上原地不动（纯插入时把插入内容包进来，接着打字仍带格式），
        // 落在区间里就收到新区间末尾，区间之后整体平移。
        int MapRight(int position)
        {
            if (position < left)
                return position;

            if (position > replacedOldEnd)
                return position + delta;

            if (position == left)
                return isPureInsertion ? replacedNewEnd : position;

            return replacedNewEnd;
        }
    }

    /// <summary>给一段文字套颜色。<paramref name="color" /> 为 <c>null</c> 表示只把颜色去掉，字号不动。</summary>
    public static void ApplyColor(List<BoardTextFormatRange> ranges, int start, int length, Color? color)
    {
        Apply(ranges, start, length, setColor: true, color, setFontSize: false, null);
    }

    /// <summary>
    ///     给一段文字套字号。<paramref name="fontSize" /> 为 <c>null</c> 表示只把字号去掉、颜色不动；
    ///     给了值会先夹进允许区间。
    /// </summary>
    public static void ApplyFontSize(List<BoardTextFormatRange> ranges, int start, int length, double? fontSize)
    {
        Apply(ranges, start, length, setColor: false, null, setFontSize: true,
            fontSize is { } size ? BoardContentStyle.ClampFontSize(size) : null);
    }

    /// <summary>把一段文字的格式整个抹掉（颜色与字号都回到整篇默认）。</summary>
    public static void Clear(List<BoardTextFormatRange> ranges, int start, int length)
    {
        Apply(ranges, start, length, setColor: true, null, setFontSize: true, null);
    }

    /// <summary>
    ///     这次操作该落到哪一段：选中了就是选中那一段；没选中（只点了光标）就是光标所在的那一行，
    ///     也就是用户心里的「这一段」。空行返回长度为 0，调用方据此什么都不做。
    /// </summary>
    public static (int Start, int Length) ResolveTargetSpan(string? text, int selectionStart, int selectionEnd)
    {
        text ??= string.Empty;

        var length = text.Length;
        var from = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, length);
        var to = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, length);

        if (to > from)
            return (from, to - from);

        // 只点了光标：按换行切出所在的一行（行尾的 \r 不算进格式范围）。
        var lineStart = from;
        while (lineStart > 0 && text[lineStart - 1] != '\n')
            lineStart--;

        var lineEnd = from;
        while (lineEnd < length && text[lineEnd] != '\n')
            lineEnd++;

        while (lineEnd > lineStart && text[lineEnd - 1] == '\r')
            lineEnd--;

        return (lineStart, lineEnd - lineStart);
    }

    /// <summary>
    ///     归一化：丢掉空标注、按下标排序、把相邻且样式完全相同的合并成一条。
    ///     <para>
    ///         有重叠时**靠后的那条生效**（渲染时也是这么取的），所以这里不做裁剪——
    ///         裁剪会把「后写的覆盖先写的」这条语义改掉。重叠只会出现在文本增删的边界上，
    ///         下一次套格式时自然会被裁掉。
    ///     </para>
    /// </summary>
    public static void Normalize(List<BoardTextFormatRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        ranges.RemoveAll(range => range.Length <= 0 || (range.Color is null && range.FontSize is null));
        ranges.Sort(static (left, right) => left.Start != right.Start
            ? left.Start.CompareTo(right.Start)
            : left.End.CompareTo(right.End));

        for (var i = 1; i < ranges.Count; i++)
        {
            var previous = ranges[i - 1];
            var current = ranges[i];
            if (current.Start != previous.End
                || current.Color != previous.Color
                || current.FontSize != previous.FontSize)
            {
                continue;
            }

            previous.Length += current.Length;
            ranges.RemoveAt(i);
            i--;
        }
    }

    /// <summary>
    ///     套格式的收口：先把目标区间从现有标注里裁掉（左右残段保留原有属性），
    ///     再把「没被改动的那个属性沿用目标区间起点处正在生效的值」拼成一条新标注放回去。
    /// </summary>
    private static void Apply(
        List<BoardTextFormatRange> ranges, int start, int length,
        bool setColor, Color? color, bool setFontSize, double? fontSize)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        if (length <= 0)
            return;

        var end = start + length;
        var current = ResolveEffectiveStyle(ranges, start);
        var nextColor = setColor ? color : current.Color;
        var nextSize = setFontSize ? fontSize : current.FontSize;

        var kept = new List<BoardTextFormatRange>(ranges.Count + 1);
        foreach (var range in ranges)
        {
            // 完全在目标区间之外：原样留着。
            if (range.End <= start || range.Start >= end)
            {
                kept.Add(range.Clone());
                continue;
            }

            // 左边剩一截、右边剩一截，各自保留原来的属性。
            if (range.Start < start)
            {
                kept.Add(new BoardTextFormatRange
                {
                    Start = range.Start,
                    Length = start - range.Start,
                    Color = range.Color,
                    FontSize = range.FontSize
                });
            }

            if (range.End > end)
            {
                kept.Add(new BoardTextFormatRange
                {
                    Start = end,
                    Length = range.End - end,
                    Color = range.Color,
                    FontSize = range.FontSize
                });
            }
        }

        // 颜色与字号都为空就不用放回去了——这段回到整篇默认。
        if (nextColor is not null || nextSize is not null)
        {
            kept.Add(new BoardTextFormatRange
            {
                Start = start,
                Length = length,
                Color = nextColor,
                FontSize = nextSize
            });
        }

        ranges.Clear();
        ranges.AddRange(kept);
        Normalize(ranges);
    }

    /// <summary>
    ///     某个下标处正在生效的样式：最后一条盖住它的标注说了算；没标注就是 <c>(null, null)</c>
    ///     （颜色跟随主题、字号跟随整篇默认）。
    ///     <para>界面用它把浮窗上的「字号／颜色」显示成<b>选段当前的值</b>，而不是永远空着。</para>
    /// </summary>
    public static (Color? Color, double? FontSize) ResolveEffectiveStyle(
        IReadOnlyList<BoardTextFormatRange> ranges, int index)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        for (var i = ranges.Count - 1; i >= 0; i--)
        {
            if (ranges[i].Start <= index && index < ranges[i].End)
                return (ranges[i].Color, ranges[i].FontSize);
        }

        return (null, null);
    }
}
