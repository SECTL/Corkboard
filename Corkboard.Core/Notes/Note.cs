using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Notes.Legacy;
using Corkboard.Core.RichText;

namespace Corkboard.Core.Notes;

/// <summary>
///     一份作业。便签本身仍然不画背景色，但内容可以带局部的颜色与字号——样式就写在
///     <see cref="Content" /> 的富文本文档里。
///     <para>
///         作业没有名字：光靠「科目 + 截止日期 + 内容」就能说清要做什么，
///         例如「数学 / P12–15」。旧数据里的 <c>title</c> 会被静默忽略。
///     </para>
/// </summary>
public partial class Note : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();

    /// <summary>
    ///     作业内容：这份作业具体要做什么。存的是**富文本文档的 HTML 片段**
    ///     （由 AvaloniaRichEditor 解析与画出），局部的颜色、字号直接写在片段里的行内样式上；
    ///     什么样式都没设时只有段落标签。和科目一样是所有作业都有的通用属性。
    /// </summary>
    [ObservableProperty] private string _content = string.Empty;

    /// <summary>
    ///     <see cref="Content" /> 该按什么格式解释。
    ///     <para>
    ///         默认值是 <see cref="RichTextContentKind.Markdown" />：旧数据的 JSON 里没有这一项，
    ///         读出来就是它，于是「旧数据」不需要额外的版本字段或搬迁标记；
    ///         新内容一律用 <see cref="SetHtmlContent" /> 写，别只改 <see cref="Content" />。
    ///     </para>
    /// </summary>
    [ObservableProperty] private RichTextContentKind _contentKind = RichTextContentKind.Markdown;

    /// <summary>
    ///     遗留字段：旧数据的「整篇字号」。迁移时会被写进富文本文档，之后不再写、不再读
    ///     （为空时序列化会整项省略）。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ContentFontSize { get; set; }

    /// <summary>
    ///     遗留字段：旧数据的颜色/字号标注，按 Markdown 原文的字符偏移定位
    ///     （见 <see cref="LegacyTextFormatRange" />）。迁移时会被写进富文本文档的行内样式，
    ///     之后不再写、不再读（为空时序列化会整项省略）。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LegacyTextFormatRange>? Formats { get; set; }

    /// <summary>
    ///     手写笔迹：归一化坐标的笔迹 JSON（格式见 <c>Corkboard.Core.Handwriting.HandwritingDocument</c>）。
    ///     只有 <see cref="ContentKind" /> 为 <see cref="RichTextContentKind.Handwriting" /> 时才有值，
    ///     此时 <see cref="Content" /> 为空（为空时序列化会整项省略）。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Handwriting
    {
        get => _handwriting;
        set => SetProperty(ref _handwriting, value);
    }

    private string? _handwriting;

    /// <summary>把内容换成一份富文本文档（HTML 片段），顺带清掉手写笔迹与两个遗留字段。</summary>
    public void SetHtmlContent(string html)
    {
        Content = html;
        ContentKind = RichTextContentKind.Html;
        Handwriting = null;
        ContentFontSize = null;
        Formats = null;
    }

    /// <summary>
    ///     把内容换成一份手写笔迹：写 <see cref="Handwriting" /> 并清空 <see cref="Content" />
    ///     （两种内容互斥，免得回放/导出时一半画字一半画笔迹）。
    /// </summary>
    public void SetHandwriting(string? json)
    {
        Handwriting = string.IsNullOrWhiteSpace(json) ? null : json;
        Content = string.Empty;
        ContentKind = RichTextContentKind.Handwriting;
        ContentFontSize = null;
        Formats = null;
    }

    /// <summary>
    ///     快捷标签：布置作业时从「设置 → 作业板 → 快捷标签」里勾的几颗，卡片上以胶囊形式显示。
    ///     <para>
    ///         <b>不是正文的一部分</b>：这里只记标签文字，内容该写什么还是什么，标签可以被单独取消；
    ///         所以在卡片上它们画成一颗颗胶囊，而不是往富文本里贴一段字。
    ///         为空时序列化整项省略（旧数据读出来就是 <c>null</c>，不需要迁移）。
    ///     </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Tags
    {
        get => _tags;
        set => SetProperty(ref _tags, NormalizeTags(value));
    }

    private List<string>? _tags;

    /// <summary>
    ///     把标签清单收拾一遍：去掉首尾空白与空项、忽略大小写去重，顺序按第一次出现。
    ///     一项都不剩时返回 <c>null</c>（落盘时整项省略）。
    /// </summary>
    public static List<string>? NormalizeTags(IEnumerable<string?>? tags)
    {
        if (tags is null)
            return null;

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in tags)
        {
            var trimmed = tag?.Trim();
            if (string.IsNullOrEmpty(trimmed) || !seen.Add(trimmed))
                continue;

            result.Add(trimmed);
        }

        return result.Count == 0 ? null : result;
    }

    /// <summary>科目。空表示未分类。</summary>
    [ObservableProperty] private string _subject = string.Empty;

    /// <summary>
    ///     二级分类标题（科目下的小标题）。空表示这一条没分类，显示在科目的「未分类」分组里。
    ///     存在<see cref="Corkboard.Core.Homework.HomeworkConfig.Sections" />里的定义是可选清单；
    ///     这里存的是作业自己记住的那个名字，删掉定义之后作业仍旧显示在自己那一组。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Section
    {
        get => _section;
        set => SetProperty(ref _section, value);
    }

    private string? _section;

    private DateOnly? _dueDate;

    /// <summary>
    ///     截止日期，空表示这份作业没有期限。
    ///     <para>
    ///         只到「天」：作业不卡具体几点交，也免得用户为了填个日期还要选时间。
    ///         自动清理对它的用法见 <see cref="Corkboard.Core.Homework.HomeworkExpiryPolicy" />：
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
}
