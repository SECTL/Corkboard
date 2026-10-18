using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Shared;
using Corkboard.Shared.Abstraction;

namespace Corkboard.Core.Exam;

/// <summary>
///     ⚠️ **遗留的单份考试安排**：老版本只有一块看板时，考试就存在 <c>data/exam/exams.json</c> 里。
///     现在考试安排跟着看板文件走（<see cref="ExamBoardDocument" />，每块一个文件），
///     这个类型只为读老数据保留：<see cref="ExamService" /> 在建第一块看板时把它整份搬进去，
///     之后不再写它。
///     <para>
///         与作业板同一个规矩：业务数据不进 <c>settings.json</c>（那是应用偏好）。
///         状态阈值、时钟位置这类**显示偏好**才在 <c>settings.json</c> 的 <c>exam_board</c> 段里。
///     </para>
/// </summary>
public partial class ExamScheduleConfig : ConfigBase
{
    /// <summary>考试数据根目录名，位于 <c>data/</c> 之下、与 <c>config/</c> 平级。</summary>
    public const string StorageDirectoryName = "exam";

    public const string StorageFileName = "exams.json";

    /// <summary>
    ///     全部考试安排。顺序不代表显示顺序：看板与设置页都按「日期 + 开考时刻」自己排
    ///     （见 <see cref="ExamStatusPolicy.ListUpcoming" />），这样用户加考试时不必操心先后。
    /// </summary>
    [ObservableProperty] private List<ExamEntry> _exams = [];

    [JsonIgnore]
    public override string ConfigFilePath => Utils.GetFilePath(StorageDirectoryName, StorageFileName);
}
