using System.Text.Json.Serialization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Shared;
using Corkboard.Shared.Abstraction;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Core.Exam;

/// <summary>
///     一份考试看板 = 一个文件（<c>data/exam/boards/&lt;Id&gt;.json</c>）。
///     <para>
///         标题、副标题、副标题颜色与考试安排**都在这份文件里**：用户可能同时维护好几块看板
///         （期中 / 期末 / 某个班的模拟考），换看板就该整套内容一起换，而不是只换考试、标题还留着上一块的。
///         全局只管「怎么看」（阈值、时钟位置、两个收起开关，见
///         <see cref="Models.SubConfigs.Exam.ExamBoardSettingsConfig" />）。
///     </para>
///     <para>
///         ⚠️ 文件名就是 <see cref="Id" />，所以 <b>Id 一旦定下就不能改</b>——改了等于换文件。
///         读盘时服务会把索引里的 Id 回填进来，手改过 JSON、丢了 <c>id</c> 字段也不会长出第二个文件。
///     </para>
/// </summary>
public partial class ExamBoardDocument : ConfigBase
{
    /// <summary>考试数据根目录名，位于 <c>data/</c> 之下、与 <c>config/</c> 平级。</summary>
    public const string StorageDirectoryName = "exam";

    /// <summary>看板文件所在子目录（<c>data/exam/boards/</c>）。</summary>
    public const string BoardsDirectoryName = "boards";

    /// <summary>看板标识。空值时服务会补一个，之后不再变动。</summary>
    [ObservableProperty] private Guid _id = Guid.NewGuid();

    /// <summary>看板主标题。留空表示用本地化资源里的默认名，升级界面语言后跟着变。</summary>
    [ObservableProperty] private string _title = string.Empty;

    /// <summary>副标题（比如「距离高考还有 200 天」）。留空则不显示这一行。</summary>
    [ObservableProperty] private string _subtitle = string.Empty;

    /// <summary>
    ///     副标题颜色。为空表示跟随主题的次要文字色（比标题淡一档，深浅色主题都看得清）；
    ///     用户挑了颜色就用他的。
    /// </summary>
    [ObservableProperty] private Color? _subtitleColor;

    /// <summary>
    ///     这块看板上的全部考试。顺序不代表显示顺序：看板与设置页都按「日期 + 开考时刻」自己排
    ///     （见 <see cref="ExamStatusPolicy.ListUpcoming" />），这样用户加考试时不必操心先后。
    /// </summary>
    [ObservableProperty] private List<ExamEntry> _exams = [];

    [JsonIgnore]
    public override string ConfigFilePath =>
        Utils.GetFilePath(StorageDirectoryName, BoardsDirectoryName, $"{Id:N}.json");

    /// <summary>看板主标题实际显示的文字：用户填了就用用户的，没填回退到资源里的默认名。</summary>
    public string ResolveTitle() =>
        string.IsNullOrWhiteSpace(Title) ? CR.Exam_Board_DefaultTitle : Title.Trim();

    /// <summary>副标题实际显示的文字（空白表示不显示）。</summary>
    public string ResolveSubtitle() => Subtitle?.Trim() ?? string.Empty;

    /// <summary>
    ///     复制一份（考试列表是浅拷贝：元素还是同一批 <see cref="ExamEntry" />）。
    ///     给「新建看板时以某块为模板」这类场景用。
    /// </summary>
    public ExamBoardDocument Clone() => new()
    {
        Id = Id,
        Title = Title,
        Subtitle = Subtitle,
        SubtitleColor = SubtitleColor,
        Exams = [.. Exams]
    };

    /// <summary>读盘后收拾一遍：标题与副标题去首尾空白（手改的脏数据不进界面）。</summary>
    public void Normalize()
    {
        Title = Title?.Trim() ?? string.Empty;
        Subtitle = Subtitle?.Trim() ?? string.Empty;
    }
}
