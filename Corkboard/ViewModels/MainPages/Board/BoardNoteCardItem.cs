using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
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
///         <c>HandwritingPreview</c>：<see cref="Handwriting" /> 是落盘的笔迹文本，
///         <see cref="HandwritingAspect" /> 是画布高宽比（控件按它定高，笔迹不变形）。
///         两种内容都只读、都不接收指针，免得跟整窗长按拖动、区块拖动排序打架。
///     </para>
///     <para>
///         <see cref="CanModify" /> 在构建时就定下来（回放期间为 <c>false</c>）；
///         编辑 / 删除两颗按钮**平时不显示**，点一下这条作业才浮出来、再点一下收回去
///         （<see cref="IsActionsVisible" /> 是这一次的开关，<see cref="ShowActions" /> 才是模板绑的
///         最终可见性）。按钮画在内容上面的一层里，**不占布局位置**——不能因为放两个按钮
///         把作业内容挤窄（用户明确要求）。
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
public sealed partial class BoardNoteCardItem : ObservableObject
{
    public BoardNoteCardItem(
        Note note,
        string html,
        double contentFontSize,
        Color? contentColor,
        double metaFontSize,
        double noteTagFontSize,
        bool canModify,
        string dueText,
        bool isOverdue,
        string? handwriting,
        double handwritingAspect,
        bool isHandwriting,
        IReadOnlyList<string> tags)
    {
        Note = note;
        Html = html;
        ContentFontSize = contentFontSize;
        ContentColor = contentColor;
        MetaFontSize = metaFontSize;
        NoteTagFontSize = noteTagFontSize;
        CanModify = canModify;
        DueText = dueText;
        IsOverdue = isOverdue;
        Handwriting = handwriting;
        HandwritingAspect = handwritingAspect;
        IsHandwriting = isHandwriting;
        Tags = tags;
    }

    public Note Note { get; }

    public string Html { get; }

    public double ContentFontSize { get; }

    public Color? ContentColor { get; }

    public double MetaFontSize { get; }

    public double NoteTagFontSize { get; }

    /// <summary>这条作业现在能不能改（回放中 / 点击穿透时为 false，两颗按钮永远不冒出来）。</summary>
    public bool CanModify { get; }

    public string DueText { get; }

    public bool IsOverdue { get; }

    public string? Handwriting { get; }

    public double HandwritingAspect { get; }

    public bool IsHandwriting { get; }

    public IReadOnlyList<string> Tags { get; }

    /// <summary>
    ///     这一条作业的编辑 / 删除按钮现在是不是被「点开」了。默认关着——
    ///     点一下卡片打开、再点一下收起（见 BoardPage 的 OnNoteTapped）。
    /// </summary>
    [ObservableProperty] private bool _isActionsVisible;

    /// <summary>
    ///     指针（或触摸按下的那根手指）是不是停在这条作业上。
    ///     鼠标悬停也要能看见按钮：只靠「点开」的话，用户根本不知道卡片上还有编辑 / 删除，
    ///     而且要两步操作才能编辑一条作业。
    /// </summary>
    [ObservableProperty] private bool _isPointerOver;

    /// <summary>模板绑的最终可见性：允许改 且（停着指针 或 被点开）。</summary>
    public bool ShowActions => CanModify && (IsActionsVisible || IsPointerOver);

    partial void OnIsActionsVisibleChanged(bool value) => OnPropertyChanged(nameof(ShowActions));

    partial void OnIsPointerOverChanged(bool value) => OnPropertyChanged(nameof(ShowActions));

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
