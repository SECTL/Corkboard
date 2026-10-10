using System.Text.Json.Serialization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Corkboard.Core.Models.Board;

/// <summary>
///     一段文字的颜色与字号覆盖：从 <see cref="Start" /> 起的 <see cref="Length" /> 个字符。
///     <para>
///         ⚠️ **只用于读旧数据**：现在作业内容直接是富文本文档，样式写在文档的行内样式里
///         （见 <see cref="BoardNote.Content" />）。旧数据里才有这套「Markdown 原文 + 偏移标注」，
///         由 <c>LegacyBoardContentConverter</c> 在加载时一次性转成文档，之后不再落盘、不再使用。
///     </para>
///     <para>
///         两个可空属性互相独立：只想改字号就别动颜色，反之亦然。
///     </para>
/// </summary>
public partial class BoardTextFormatRange : ObservableObject
{
    /// <summary>起始字符下标。按 UTF-16 码元算，和 <see cref="string" /> 的索引一致。</summary>
    [ObservableProperty] private int _start;

    /// <summary>覆盖的字符数。</summary>
    [ObservableProperty] private int _length;

    /// <summary>文字颜色。为空表示这段跟随整篇默认色（设置里的默认颜色，没设就跟随主题前景色）。</summary>
    [ObservableProperty] private Color? _color;

    /// <summary>字号。为空表示这段不单独定字号，继续用整篇字号（Markdown 标题会按级别放大）。</summary>
    [ObservableProperty] private double? _fontSize;

    /// <summary>结束下标（不含）。只读，落盘时由 <see cref="Start" /> 与 <see cref="Length" /> 推出。</summary>
    [JsonIgnore]
    public int End => Start + Length;

    /// <summary>复制一份。编辑表单拿的是草稿，落盘前一律克隆，免得草稿对象被领域服务共享出去。</summary>
    public BoardTextFormatRange Clone()
    {
        return new BoardTextFormatRange
        {
            Start = Start,
            Length = Length,
            Color = Color,
            FontSize = FontSize
        };
    }
}
