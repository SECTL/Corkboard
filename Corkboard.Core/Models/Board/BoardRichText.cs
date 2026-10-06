using Avalonia.Media;

namespace Corkboard.Core.Models.Board;

/// <summary>
///     画一段作业内容需要的全部输入：Markdown 原文、颜色/字号标注、整篇字号、默认颜色。
///     <para>
///         做成不可变记录是为了给控件一个干净的「换了就重建」信号：渲染输入是一个值，
///         ViewModel 每改一次就换一个新实例，控件只比较引用，不用去订阅集合内部的增删改。
///     </para>
///     <para>
///         <paramref name="BaseFontSize" /> 是**整篇字号**（作业自己定的，没定就用设置里的默认字号）；
///         <paramref name="BaseColor" /> 是**默认颜色**（设置里的默认颜色，为空则跟随主题前景色）。
///         标注里的颜色/字号是这两者之上的局部覆盖。
///     </para>
/// </summary>
public sealed record BoardRichText(
    string Text,
    IReadOnlyList<BoardTextFormatRange> Formats,
    double BaseFontSize,
    Color? BaseColor)
{
    /// <summary>空内容：没有字、没有标注，字号取默认、颜色跟随主题。</summary>
    public static BoardRichText Empty { get; } =
        new(string.Empty, [], BoardContentStyle.DefaultFontSize, null);

    /// <summary>
    ///     按一份作业造渲染输入。内容两侧的空白先去掉，标注跟着同一次位移挪过去——
    ///     直接 <c>Trim()</c> 会让标注和字符错位。
    /// </summary>
    /// <param name="note">作业。</param>
    /// <param name="defaultFontSize">设置里的默认字号，作业自己没定整篇字号时用它。</param>
    /// <param name="defaultColor">设置里的默认颜色，为空表示跟随主题。</param>
    public static BoardRichText FromNote(BoardNote note, double defaultFontSize, Color? defaultColor)
    {
        ArgumentNullException.ThrowIfNull(note);

        var trimmed = note.Content.Trim();
        var formats = string.Equals(trimmed, note.Content, StringComparison.Ordinal)
            ? note.Formats
            : Services.Board.BoardTextFormatEditing.Shift(note.Formats, note.Content, trimmed);

        var baseSize = note.ContentFontSize is { } size
            ? BoardContentStyle.ClampFontSize(size)
            : BoardContentStyle.ClampFontSize(defaultFontSize);

        return new BoardRichText(trimmed, formats, baseSize, defaultColor);
    }
}
