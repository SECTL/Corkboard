using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Exam;
using Corkboard.Core.Exam.Layout;

namespace Corkboard.Core.Models.SubConfigs.Exam;

/// <summary>
///     考试看板的组件布局（<c>exam_board_layout</c>）：四档状态各存一套，全部默认走系统版式。
/// </summary>
public partial class ExamBoardLayoutConfig : ObservableObject
{
    [ObservableProperty] private ExamBoardStatusLayout _notStarted =
        ExamBoardLayoutDefaults.CreateStatusLayout(ExamStatus.NotStarted);
    [ObservableProperty] private ExamBoardStatusLayout _upcoming =
        ExamBoardLayoutDefaults.CreateStatusLayout(ExamStatus.Upcoming);
    [ObservableProperty] private ExamBoardStatusLayout _inProgress =
        ExamBoardLayoutDefaults.CreateStatusLayout(ExamStatus.InProgress);
    [ObservableProperty] private ExamBoardStatusLayout _finished =
        ExamBoardLayoutDefaults.CreateStatusLayout(ExamStatus.Finished);

    public ExamBoardStatusLayout For(ExamStatus status) => status switch
    {
        ExamStatus.NotStarted => NotStarted,
        ExamStatus.Upcoming => Upcoming,
        ExamStatus.InProgress => InProgress,
        _ => Finished
    };

    public void Normalize()
    {
        NotStarted ??= ExamBoardLayoutDefaults.CreateStatusLayout(ExamStatus.NotStarted);
        Upcoming ??= ExamBoardLayoutDefaults.CreateStatusLayout(ExamStatus.Upcoming);
        InProgress ??= ExamBoardLayoutDefaults.CreateStatusLayout(ExamStatus.InProgress);
        Finished ??= ExamBoardLayoutDefaults.CreateStatusLayout(ExamStatus.Finished);

        NotStarted.Normalize();
        Upcoming.Normalize();
        InProgress.Normalize();
        Finished.Normalize();
    }

    public ExamBoardLayoutConfig Clone() => new()
    {
        NotStarted = NotStarted.Clone(),
        Upcoming = Upcoming.Clone(),
        InProgress = InProgress.Clone(),
        Finished = Finished.Clone()
    };
}