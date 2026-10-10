using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Corkboard.Core.Models.Board;

/// <summary>
///     一份作业。便签本身仍然不画背景色，但内容可以带局部的颜色与字号——样式就写在
///     <see cref="Content" /> 的富文本文档里。
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
    ///     作业内容：这份作业具体要做什么。存的是**富文本文档的 HTML 片段**
    ///     （由 AvaloniaRichEditor 解析与画出），局部的颜色、字号直接写在片段里的行内样式上；
    ///     什么样式都没设时只有段落标签。和科目一样是所有作业都有的通用属性。
    /// </summary>
    [ObservableProperty] private string _content = string.Empty;

    /// <summary>
    ///     <see cref="Content" /> 该按什么格式解释。
    ///     <para>
    ///         默认值是 <see cref="BoardContentKind.Markdown" />：旧数据的 JSON 里没有这一项，
    ///         读出来就是它，于是「旧数据」不需要额外的版本字段或搬迁标记；
    ///         新内容一律用 <see cref="SetHtmlContent" /> 写，别只改 <see cref="Content" />。
    ///     </para>
    /// </summary>
    [ObservableProperty] private BoardContentKind _contentKind = BoardContentKind.Markdown;

    /// <summary>
    ///     遗留字段：旧数据的「整篇字号」。迁移时会被写进富文本文档，之后不再写、不再读
    ///     （为空时序列化会整项省略）。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ContentFontSize { get; set; }

    /// <summary>
    ///     遗留字段：旧数据的颜色/字号标注，按 Markdown 原文的字符偏移定位
    ///     （见 <see cref="BoardTextFormatRange" />）。迁移时会被写进富文本文档的行内样式，
    ///     之后不再写、不再读（为空时序列化会整项省略）。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BoardTextFormatRange>? Formats { get; set; }

    /// <summary>把内容换成一份富文本文档（HTML 片段），顺带清掉两个遗留字段。</summary>
    public void SetHtmlContent(string html)
    {
        Content = html;
        ContentKind = BoardContentKind.Html;
        ContentFontSize = null;
        Formats = null;
    }

    /// <summary>科目。空表示未分类。</summary>
    [ObservableProperty] private string _subject = string.Empty;

    private DateOnly? _dueDate;

    /// <summary>
    ///     截止日期，空表示这份作业没有期限。
    ///     <para>
    ///         只到「天」：作业不卡具体几点交，也免得用户为了填个日期还要选时间。
    ///         自动清理对它的用法见 <see cref="Services.Board.BoardExpiryPolicy" />：
    ///         <b>过了截止日期才会被清掉，没填截止日期的作业永远不会被自动清掉。</b>
    ///     </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateOnly? DueDate
    {
        get => _dueDate;
        set => SetProperty(ref _dueDate, value);
    }

    private DateTimeOffset? _cleanedAt;

    /// <summary>
    ///     被自动清理收走的时刻；为空表示还留在板子上。
    ///     <para>
    ///         清理<b>不动数据</b>：作业仍然在原来那个 <c>&lt;年&gt;/&lt;月&gt;/&lt;日&gt;/notes.json</c> 里，
    ///         只是主页面不再显示它。所以这是一个标记，不是归档——没有第二个目录，也不会把文件搬走甚至删掉。
    ///         想让它回到板子上，把这一项清空再落盘即可。
    ///     </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CleanedAt
    {
        get => _cleanedAt;
        set => SetProperty(ref _cleanedAt, value);
    }

    /// <summary>是否还在板子上（没被自动清理收走）。主页面按它过滤，见 <c>BoardPageViewModel</c>。</summary>
    [JsonIgnore]
    public bool IsOnBoard => CleanedAt is null;

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
