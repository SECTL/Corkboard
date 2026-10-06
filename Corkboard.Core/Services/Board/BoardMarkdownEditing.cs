namespace Corkboard.Core.Services.Board;

/// <summary>格式浮窗能给选段套的 Markdown 记号。</summary>
public enum BoardMarkdownFormat
{
    /// <summary>加粗：把选段夹进 <c>**</c>。</summary>
    Bold,

    /// <summary>斜体：把选段夹进 <c>*</c>。</summary>
    Italic
}

/// <summary>套完记号之后的新文本，以及应该重新选中的区间。</summary>
public readonly record struct BoardMarkdownEditResult(string Text, int SelectionStart, int SelectionLength);

/// <summary>
///     给选段套 Markdown 记号，再点一次同样的记号就去掉（Office 那种开关手感）。
///     <para>
///         只做「夹住选段」这一类（加粗 / 斜体）：标题、列表、行内代码这些手写也一样能写，
///         但按钮摆出来对普通用户是负担，浮窗上只留最常用的两个。
///     </para>
///     <para>
///         记号是**写进文本本身**的：原文始终是普通 Markdown，可读、可编辑、可粘贴，
///         与「样式标注另存一份」的路子（<see cref="BoardTextFormatEditing" /> 管的颜色/字号）互不干扰。
///     </para>
///     <para>
///         返回新选区是为了让界面把 <c>TextBox</c> 的选区摆回去——记号落在选区外面，
///         用户能接着点下一个格式，不会越点越偏。
///     </para>
///     <para>纯计算、不碰 UI，有单测（见 <c>BoardMarkdownEditingTests</c>）。</para>
/// </summary>
public static class BoardMarkdownEditing
{
    /// <summary>
    ///     给 <c>[selectionStart, selectionEnd)</c> 这段套一个记号。选区不分先后（会自动归一），
    /// 光标（长度为 0）时会在光标处插入一对空记号，并把光标放到中间。
    /// </summary>
    public static BoardMarkdownEditResult Toggle(
        string? text, int selectionStart, int selectionEnd, BoardMarkdownFormat format)
    {
        text ??= string.Empty;

        var length = text.Length;
        var start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, length);
        var end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, length);

        return format switch
        {
            BoardMarkdownFormat.Bold => ToggleWrap(text, start, end, "**"),
            BoardMarkdownFormat.Italic => ToggleWrap(text, start, end, "*"),
            _ => new BoardMarkdownEditResult(text, start, end - start)
        };
    }

    /// <summary>夹住选段；已经夹着（记号在选区外侧或选区把记号也框了进来）就把它们去掉。</summary>
    private static BoardMarkdownEditResult ToggleWrap(string text, int start, int end, string marker)
    {
        // 把记号连内容一起选中的情况：把选区里那对记号去掉，剩下的内容继续选中。
        if (end - start >= marker.Length * 2
            && text.AsSpan(start, end - start).StartsWith(marker, StringComparison.Ordinal)
            && text.AsSpan(start, end - start).EndsWith(marker, StringComparison.Ordinal))
        {
            var trimmed = text.Remove(end - marker.Length, marker.Length).Remove(start, marker.Length);
            return new BoardMarkdownEditResult(trimmed, start, end - start - marker.Length * 2);
        }

        // 记号紧贴选区两侧：去掉它们（再点一次就是取消）。
        if (start >= marker.Length && end + marker.Length <= text.Length
            && text.AsSpan(start - marker.Length, marker.Length).SequenceEqual(marker)
            && text.AsSpan(end, marker.Length).SequenceEqual(marker))
        {
            var unwrapped = text.Remove(end, marker.Length).Remove(start - marker.Length, marker.Length);
            return new BoardMarkdownEditResult(unwrapped, start - marker.Length, end - start);
        }

        // 否则套上：记号在选区外，选区仍落在内容上。光标（空选区）时就插入一对空记号。
        var wrapped = text.Insert(end, marker).Insert(start, marker);
        return new BoardMarkdownEditResult(wrapped, start + marker.Length, end - start);
    }
}
