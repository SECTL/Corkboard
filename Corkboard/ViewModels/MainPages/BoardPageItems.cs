using Corkboard.Core.Models.Board;

namespace Corkboard.ViewModels.MainPages;

/// <summary>
///     同一科目的作业共用一个区块。
///     <para>
///         <paramref name="Key" /> 是落盘用的原始科目（空串表示未分类），
///         <paramref name="Subject" /> 是显示名（未分类时是资源文案）——手动排序记的是前者。
///         <paramref name="CanReorder" /> 在构建时就定下来（回放期间为 <c>false</c>）：
///         列表项是重建出来的，回放状态一变整份列表就重建，所以不需要在模板里做相对源绑定。
///     </para>
/// </summary>
public sealed record BoardSubjectItem(
    string Key,
    string Subject,
    IReadOnlyList<BoardNoteItem> Notes,
    bool CanReorder);

/// <summary>
///     科目区块内的一条作业，只显示类型、字段值与内容。
///     <para>
///         <see cref="Document" /> 是内容渲染需要的全部输入（Markdown 原文 + 颜色/字号标注 + 整篇字号），
///         在重建列表时就按当时的设置算好，模板直接交给 <c>MarkdownTextBlock</c> 画。
///     </para>
///     <para>
///         <see cref="CanModify" /> 在构建时就定下来（回放期间为 <c>false</c>），
///         编辑与删除两个按钮共用它。
///     </para>
/// </summary>
public sealed record BoardNoteItem(BoardNote Note, string Detail, BoardRichText Document, bool CanModify)
{
    public bool HasDetail => Detail.Length > 0;
    public bool HasContent => Document.Text.Length > 0;
}

/// <summary>布置作业的类型选项，第一项固定为「直接写」（Type 为空）。</summary>
public sealed record BoardTypeOption(BoardTypeDef? Type, string Name);
