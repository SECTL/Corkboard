namespace Corkboard.Core.Homework;

/// <summary>
///     科目下面的**二级分类标题**（用户在设置里建，比如「语文 → 第一单元」）。
///     <para>
///         只是一条定义：作业自己记着自己属于哪个分类（<c>Note.Section</c>），
///         所以删掉一条定义不会连带删掉或改写任何作业——那些作业仍旧显示在自己那个分类组里。
///         整个列表的顺序就是板子上分组的先后。
///     </para>
/// </summary>
public sealed class HomeworkSection
{
    /// <summary>所属科目（与科目清单里的名字一致，比较时先 Trim）。</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>分类标题。</summary>
    public string Name { get; set; } = string.Empty;
}
