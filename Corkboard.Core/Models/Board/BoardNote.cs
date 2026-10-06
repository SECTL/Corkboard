using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Corkboard.Core.Models.Board;

/// <summary>
///     一份作业。便签本身仍然不画背景色，但内容可以带局部的颜色与字号（见 <see cref="Formats" />）。
///     <para>
///         作业没有名字：光靠「科目 + 类型 + 字段值 + 内容」就能说清要做什么，
///         例如「数学 / 练习册 / P12–15」。旧数据里的 <c>title</c> 会被静默忽略。
///     </para>
/// </summary>
public partial class BoardNote : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();

    /// <summary>作业类型的 Id。为空表示「直接写」的临时作业，没有专属字段。</summary>
    [ObservableProperty] private Guid? _typeId;

    /// <summary>
    ///     作业内容：这份作业具体要做什么。多行自由文本，按 Markdown 渲染，
    ///     和科目一样是所有作业都有的通用属性。
    /// </summary>
    [ObservableProperty] private string _content = string.Empty;

    /// <summary>
    ///     整篇字号，对应布置作业表单里的「整篇字号」。
    ///     为空表示跟随「设置 → 作业板 → 内容默认样式」里的默认字号。
    /// </summary>
    [ObservableProperty] private double? _contentFontSize;

    /// <summary>
    ///     内容里的颜色/字号标注，按 <see cref="Content" /> 的字符偏移定位（见 <see cref="BoardTextFormatRange" />）。
    ///     <para>
    ///         只有局部改过颜色或字号才会有内容；旧数据里没有这一项，读出来就是空表，
    ///         所以加这个字段不需要迁移，也不需要改 <c>BoardNoteStore</c>。
    ///     </para>
    /// </summary>
    [ObservableProperty] private List<BoardTextFormatRange> _formats = [];

    /// <summary>科目。空表示未分类。</summary>
    [ObservableProperty] private string _subject = string.Empty;

    /// <summary>手动排序用的序号，后续「手动」排序模式会用到。</summary>
    [ObservableProperty] private int _order;

    [ObservableProperty] private DateTimeOffset _createdAt = DateTimeOffset.Now;
    [ObservableProperty] private DateTimeOffset _updatedAt = DateTimeOffset.Now;

    /// <summary>
    ///     字段值：键是 <see cref="BoardTypeField.Id" /> 的字符串形式，值是**原始文本**。
    ///     页数区间存成 <c>12-15</c> 这种形式，显示时才按字段类型格式化成 <c>P12–15</c>，
    ///     这样数据层留得住结构，格式化只有一处。
    /// </summary>
    [ObservableProperty] private Dictionary<string, string> _values = [];
}
