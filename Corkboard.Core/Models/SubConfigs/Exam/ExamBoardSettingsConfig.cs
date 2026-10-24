using System.Text.Json.Serialization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Exam;

namespace Corkboard.Core.Models.SubConfigs.Exam;

/// <summary>
///     考试看板的**显示偏好**（<c>settings.json</c> 的 <c>exam_board</c> 段）。
///     <para>
///         这里只有「怎么看」：阈值、时钟摆哪边、两种形态收不收列表。
///         标题、副标题与考试安排都跟着**看板文件**走（<see cref="Exam.ExamBoardDocument" />），
///         因为用户可能同时维护好几块看板，换看板就该整套内容一起换。
///     </para>
/// </summary>
public partial class ExamBoardSettingsConfig : ObservableObject
{
    /// <summary>
    ///     ⚠️ 遗留字段：标题已经搬进看板文件（<see cref="Exam.ExamBoardDocument.Title" />）。
    ///     留着只为读老版本写在 <c>settings.json</c> 里的值，由 <see cref="Exam.ExamService" />
    ///     在建第一块看板时搬走并清空。界面不要再绑它。
    /// </summary>
    [ObservableProperty] private string _title = string.Empty;

    /// <summary>⚠️ 遗留字段，同 <see cref="Title" />。</summary>
    [ObservableProperty] private string _subtitle = string.Empty;

    /// <summary>⚠️ 遗留字段，同 <see cref="Title" />。</summary>
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

    /// <summary>
    ///     「剩余时间快到了」的门槛（分钟）：正在考剩多久、即将开始还有多久，少于这个数就转红。
    ///     默认 15 分钟；0 表示不提醒。读的时候一律过一遍
    ///     <see cref="ExamStatusPolicy.ClampRemainingWarningMinutes" />。
    /// </summary>
    [ObservableProperty] private int _remainingWarningMinutes = ExamStatusPolicy.DefaultRemainingWarningMinutes;

    /// <summary>
    ///     「剩余时间精确到秒」：开启后最后一小时里的倒计时带秒（「还剩 12 分 30 秒」），
    ///     一小时以上仍然只说小时/分钟（那时候秒没意义）。默认关。
    /// </summary>
    [ObservableProperty] private bool _remainingSecondsEnabled;

    /// <summary>
    ///     时钟下面那组考试信息（进行中 / 下一场是哪一科、时间范围、剩余时间、卷面）要不要显示。
    ///     默认开——它就是这个看板的信息主体；关掉之后只剩一个大时钟，适合只想看时间的场合。
    /// </summary>
    [ObservableProperty] private bool _showExamInfo = true;

    /// <summary>读盘后收拾一遍：阈值夹进合法区间、遗留的标题与副标题去首尾空白（手改的脏数据不进界面）。</summary>
    public void Normalize()
    {
        UpcomingThresholdMinutes = ExamStatusPolicy.ClampUpcomingMinutes(UpcomingThresholdMinutes);
        RemainingWarningMinutes = ExamStatusPolicy.ClampRemainingWarningMinutes(RemainingWarningMinutes);
        Title = Title?.Trim() ?? string.Empty;
        Subtitle = Subtitle?.Trim() ?? string.Empty;
    }

    /// <summary>副标题默认用几号字（主标题的六成左右；看板会按窗口宽度再放大）。</summary>
    [JsonIgnore] public const double DefaultSubtitleFontSize = 22;
}
