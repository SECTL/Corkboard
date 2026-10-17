using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Shared;
using Corkboard.Shared.Abstraction;

namespace Corkboard.Core.Homework;

/// <summary>
///     作业板的**定义**数据：<c>data/board/board.json</c>，只放科目与手动排序顺序。
///     <para>
///         这些是业务数据而不是应用偏好，所以既不挂到 <c>settings.json</c>，也不和设置文件同处
///         <c>data/config/</c>。**作业本身不在这里**：作业按创建日期分文件存放在
///         <c>data/board/&lt;年&gt;/&lt;月&gt;/&lt;日&gt;/notes.json</c>（见 <c>NoteStore</c>）。
///     </para>
/// </summary>
public partial class HomeworkConfig : ConfigBase
{
    /// <summary>作业数据根目录名，位于 <c>data/</c> 之下、与 <c>config/</c> 平级。</summary>
    public const string StorageDirectoryName = "board";

    public const string StorageFileName = "board.json";

    /// <summary>
    ///     科目清单。默认空：首次运行不再替用户预置「语文 / 数学 / 英语」，
    ///     科目表由用户在「设置 → 作业板 → 科目与分类」里自己建（迁移旧数据时旧文件里的科目仍会接管，
    ///     见 <see cref="HomeworkSectionCatalog.DefinitionExists" />）。
    /// </summary>
    [ObservableProperty]
    private List<string> _subjects = [];

    /// <summary>
    ///     手动排序下的科目区块先后，由主界面长按手柄拖动后落盘。只记科目名，**不记作业**：
    ///     区块内的作业顺序仍由 <see cref="Note.Order" /> 决定。
    ///     <para>
    ///         不在表里的科目（新建的、或表里删过又加回来的）排在最后，
    ///     所以这个表缺项不会让任何作业消失。
    ///     </para>
    /// </summary>
    [ObservableProperty] private List<string> _subjectOrder = [];

    /// <summary>
    ///     科目下的二级分类标题（大标题下面那一层小标题）。顺序即板子上的分组先后。
    ///     <para>
    ///         存的是**定义**，作业自己记着 <c>Note.Section</c>——删掉一条定义不会动任何作业，
    ///         那些作业照旧显示在自己那个分类组里；只是新建作业时挑不到了。
    ///     </para>
    /// </summary>
    [ObservableProperty] private List<HomeworkSection> _sections = [];

    [JsonIgnore]
    public override string ConfigFilePath => Utils.GetFilePath(StorageDirectoryName, StorageFileName);
}
