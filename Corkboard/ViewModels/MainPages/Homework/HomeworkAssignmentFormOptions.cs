using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.MainPages.Homework;

/// <summary>
///     「布置作业」表单里内容的两种形态：打字（富文本 HTML）与手写（笔迹 JSON）。
///     <para>两种形态各自留一份草稿，切换时谁也不覆盖谁（见 <see cref="HomeworkAssignmentFormViewModel" />）。</para>
/// </summary>
public enum HomeworkComposeMode
{
    /// <summary>打字：富文本编辑区，内容落盘成 HTML 片段。</summary>
    Text = 0,

    /// <summary>手写：手写画布，内容落盘成归一化坐标的笔迹 JSON。</summary>
    Handwriting = 1
}

/// <summary>
///     「二级分类」下拉里的一个选项。
///     <para><see cref="Value" /> 为 <c>null</c> 的那一项是「不分类」，恒排在第一。</para>
/// </summary>
public sealed class HomeworkSectionOption(string? value)
{
    /// <summary>要写进 <c>Note.Section</c> 的分类名；<c>null</c> 表示这条作业不分类。</summary>
    public string? Value { get; } = Normalize(value);

    /// <summary>下拉里显示的文字：分类名，或者「不分类」。</summary>
    public string Display { get; } = Normalize(value) ?? CR.Board_SectionNone;

    private static string? Normalize(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

/// <summary>手写工具栏上的一颗常用色圆点；点一下把当前笔色换成它。</summary>
public sealed partial class HandwritingColorOption : ObservableObject
{
    public HandwritingColorOption(Color color, HomeworkAssignmentFormViewModel owner)
    {
        Color = color;
        Brush = new SolidColorBrush(color);
        Owner = owner;

        // 颜色本身没有文字，悬停提示给个色值，省得用户分不清墨绿和深灰。
        ToolTip = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    /// <summary>要写进画布的点。</summary>
    public Color Color { get; }

    /// <summary>圆点自己的填充色。</summary>
    public IBrush Brush { get; }

    /// <summary>表单 ViewModel：圆点的命令都挂在它上面，免得在模板里往上找 DataContext。</summary>
    public HomeworkAssignmentFormViewModel Owner { get; }

    /// <summary>悬停提示里的十六进制色值。</summary>
    public string ToolTip { get; }

    /// <summary>是不是当前笔色（决定圆点外面那圈高亮）。</summary>
    [ObservableProperty] private bool _isSelected;
}

/// <summary>
///     快捷标签的一颗 chip：点一下是「选中 / 取消选中」，不是往正文里贴字。
///     <para>
///         选中的几颗会随作业一起存进 <c>Note.Tags</c>，在作业板上以胶囊形式显示；
///         正文内容与标签互不干扰，标签可以事后单独取消。
///     </para>
/// </summary>
public sealed partial class HomeworkQuickTagOption(string text, HomeworkAssignmentFormViewModel owner) : ObservableObject
{
    /// <summary>标签文字（已去掉首尾空白，设置里已经去过重）。</summary>
    public string Text { get; } = text;

    /// <summary>表单 ViewModel：这颗 chip 属于哪份表单。</summary>
    public HomeworkAssignmentFormViewModel Owner { get; } = owner;

    /// <summary>是不是已经被选中（决定 chip 是否点亮，也决定保存时会不会写进作业）。</summary>
    [ObservableProperty] private bool _isSelected;
}
