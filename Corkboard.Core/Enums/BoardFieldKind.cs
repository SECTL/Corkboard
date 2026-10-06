namespace Corkboard.Core.Enums;

/// <summary>
///     作业字段的取值类型：决定新建作业时用什么控件输入、便签上怎么显示。
///     <para>
///         枚举按数字落盘（没有字符串枚举转换器），只能在<b>末尾</b>增删成员。
///     </para>
/// </summary>
public enum BoardFieldKind
{
    /// <summary>单行文本。</summary>
    Text = 0,

    /// <summary>多行文本。</summary>
    Paragraph = 1,

    /// <summary>单个数字，输入用步进器。</summary>
    Number = 2,

    /// <summary>页数区间（起–止），输入是两个步进器，显示成一个整体如 P12–15。</summary>
    PageRange = 3
}
