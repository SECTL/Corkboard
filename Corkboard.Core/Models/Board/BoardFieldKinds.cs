using Corkboard.Core.Enums;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Core.Models.Board;

/// <summary>字段值类型在界面上的显示名，以及可选值清单（设置页的下拉用）。</summary>
public static class BoardFieldKinds
{
    /// <summary>设置页下拉的取值顺序，与枚举数字一致。</summary>
    public static readonly IReadOnlyList<BoardFieldKind> All =
    [
        BoardFieldKind.Text,
        BoardFieldKind.Paragraph,
        BoardFieldKind.Number,
        BoardFieldKind.PageRange
    ];

    /// <summary>
    ///     下拉用的显示名，顺序与 <see cref="All" /> 一致，所以设置页可以直接用
    ///     <c>SelectedIndex</c> 绑定 <c>BoardTypeField.KindIndex</c>，不用写转换器。
    ///     <para>刻意不缓存：界面语言切换后要能取到新语言的名字。</para>
    /// </summary>
    public static IReadOnlyList<string> DisplayNames => [.. All.Select(kind => kind.ToDisplayName())];

    public static string ToDisplayName(this BoardFieldKind kind)
    {
        return kind switch
        {
            BoardFieldKind.Paragraph => CR.Board_FieldKind_Paragraph,
            BoardFieldKind.Number => CR.Board_FieldKind_Number,
            BoardFieldKind.PageRange => CR.Board_FieldKind_PageRange,
            _ => CR.Board_FieldKind_Text
        };
    }
}
