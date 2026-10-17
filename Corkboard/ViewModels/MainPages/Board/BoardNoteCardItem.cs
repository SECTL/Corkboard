using Avalonia.Media;
using Corkboard.Core.Notes;

namespace Corkboard.ViewModels.MainPages.Board;

/// <summary>
///     科目区块内的一条作业：内容是主体，另有截止日期一行小字。
///     <para>
///         <see cref="Html" /> 是内容的富文本片段，<see cref="ContentFontSize" /> 与
///         <see cref="ContentColor" /> 是画它时要补的默认样式（设置里的「内容默认样式」，
///         颜色为空表示跟随主题），模板直接交给 <c>RichTextBlock</c>。
///     </para>
///     <para>
///         手写作业（<see cref="IsHandwriting" />）不画富文本，改画只读的
///         <c>HandwritingPreview</c>：<see cref="Handwriting" /> 是落盘的笔迹 JSON，
///         <see cref="HandwritingAspect" /> 是画布高宽比（控件按它定高，笔迹不变形）。
///         两种内容都只读、都不接收指针，免得跟整窗长按拖动、区块拖动排序打架。
///     </para>
///     <para>
///         <see cref="CanModify" /> 在构建时就定下来（回放期间为 <c>false</c>），
///         编辑与删除两个按钮共用它。
///     </para>
///     <para>
///         <see cref="DueText" /> 是截止日期的显示文案（没设过就是空串，整行不显示），
///         <see cref="IsOverdue" /> 只在过期时为真，模板拿它换一个警示色。
///     </para>
///     <para>
///         <see cref="Tags" /> 是这条作业勾中的快捷标签，模板把它们画成一排胶囊；
///         标签是结构化数据（<c>Note.Tags</c>），不是正文里的文字。
///     </para>
///     <para>
///         <paramref name="MetaFontSize" /> 是截止日期那一行用的字号（设置里的「作业条小字字号」）：
///         它原来写死 12，后排看不见。标签胶囊也继承这个字号。
///     </para>
/// </summary>
public sealed record BoardNoteCardItem(
    Note Note,
    string Html,
    double ContentFontSize,
    Color? ContentColor,
    double MetaFontSize,
    bool CanModify,
    string DueText,
    bool IsOverdue,
    string? Handwriting,
    double HandwritingAspect,
    bool IsHandwriting,
    IReadOnlyList<string> Tags)
{
    /// <summary>这段内容是不是富文本（模板据此显示 <c>RichTextBlock</c>）。</summary>
    public bool HasContent => Html.Length > 0;

    /// <summary>富文本要不要画：手写作业即使旧字段里残留了 HTML 也不画。</summary>
    public bool ShowHtml => !IsHandwriting && HasContent;

    /// <summary>有没有可画的手写笔迹；为空时不画（免得留一块空白）。</summary>
    public bool HasHandwriting => IsHandwriting && !string.IsNullOrEmpty(Handwriting);

    public bool HasDueText => DueText.Length > 0;

    /// <summary>有没有快捷标签；没有就整行不画，别在卡片顶上留一排空白。</summary>
    public bool HasTags => Tags.Count > 0;
}
