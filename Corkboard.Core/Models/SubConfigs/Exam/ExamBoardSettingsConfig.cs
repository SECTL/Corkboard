using System.Text.Json.Serialization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Exam;

namespace Corkboard.Core.Models.SubConfigs.Exam;

/// <summary>
///     考试看板的**显示偏好**（<c>settings.json</c> 的 <c>exam_board</c> 段）。
///     <para>
///         考试安排本身不在这里：那是业务数据，落在 <c>data/exam/exams.json</c>
///         （见 <see cref="ExamScheduleConfig" />）。这里只有「怎么看」。
///     </para>
/// </summary>
public partial class ExamBoardSettingsConfig : ObservableObject
{
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
    ///     「即将开始」的窗口（分钟）：开考前这么多分钟之内算「即将开始」。
    ///     读的时候一律过一遍 <see cref="ExamStatusPolicy.ClampUpcomingMinutes" />。
    /// </summary>
    [ObservableProperty] private int _upcomingThresholdMinutes = ExamStatusPolicy.DefaultUpcomingMinutes;

    /// <summary>时钟与考试列表谁在左（默认时钟在左）。</summary>
    [ObservableProperty] private ExamBoardClockSide _clockSide = ExamBoardClockSide.Left;

    /// <summary>
    ///     进入「即将开始」之后是否把右侧的完整考试表收起来、只留下一场。
    ///     默认开：开考前那一刻，用户要看的是「马上考什么」，不是整张表。
    /// </summary>
    [ObservableProperty] private bool _hideExamListWhenUpcoming = true;

    /// <summary>
    ///     考试进行中是否仍然显示右侧的考试信息。默认关——考试中看板只留一个大时钟，
    ///     免得一直盯着科目和卷面信息分心。
    /// </summary>
    [ObservableProperty] private bool _showExamListInProgress;

    /// <summary>看板主标题实际显示的文字：用户填了就用用户的，没填回退到资源里的默认名。</summary>
    public string ResolveTitle() =>
        string.IsNullOrWhiteSpace(Title) ? Langs.Common.Resources.Exam_Board_DefaultTitle : Title.Trim();

    /// <summary>副标题实际显示的文字（空白表示不显示）。</summary>
    public string ResolveSubtitle() => Subtitle?.Trim() ?? string.Empty;

    /// <summary>读盘后收拾一遍：阈值夹进合法区间、标题与副标题去首尾空白（手改的脏数据不进界面）。</summary>
    public void Normalize()
    {
        UpcomingThresholdMinutes = ExamStatusPolicy.ClampUpcomingMinutes(UpcomingThresholdMinutes);
        Title = Title?.Trim() ?? string.Empty;
        Subtitle = Subtitle?.Trim() ?? string.Empty;
    }

    /// <summary>副标题默认用几号字（主标题的六成左右；看板会按窗口宽度再放大）。</summary>
    [JsonIgnore] public const double DefaultSubtitleFontSize = 22;
}
