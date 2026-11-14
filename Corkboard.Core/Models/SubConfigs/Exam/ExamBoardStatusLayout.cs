using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Exam.Layout;

namespace Corkboard.Core.Models.SubConfigs.Exam;

/// <summary>
///     某一档考试状态下的五个组件摆法。默认 <see cref="IsCustomized" /> 为 false，走系统版式。
/// </summary>
public partial class ExamBoardStatusLayout : ObservableObject
{
    [ObservableProperty] private bool _isCustomized;
    [ObservableProperty] private ExamBoardComponentLayout _title =
        ExamBoardLayoutDefaults.CreateComponent(ExamBoardLayoutComponentId.Title);
    [ObservableProperty] private ExamBoardComponentLayout _clock =
        ExamBoardLayoutDefaults.CreateComponent(ExamBoardLayoutComponentId.Clock);
    [ObservableProperty] private ExamBoardComponentLayout _examInfo =
        ExamBoardLayoutDefaults.CreateComponent(ExamBoardLayoutComponentId.ExamInfo);
    [ObservableProperty] private ExamBoardComponentLayout _examTable =
        ExamBoardLayoutDefaults.CreateComponent(ExamBoardLayoutComponentId.ExamTable);
    [ObservableProperty] private ExamBoardComponentLayout _exitCorner =
        ExamBoardLayoutDefaults.CreateComponent(ExamBoardLayoutComponentId.ExitCorner);

    public ExamBoardComponentLayout For(ExamBoardLayoutComponentId component) => component switch
    {
        ExamBoardLayoutComponentId.Title => Title,
        ExamBoardLayoutComponentId.Clock => Clock,
        ExamBoardLayoutComponentId.ExamInfo => ExamInfo,
        ExamBoardLayoutComponentId.ExamTable => ExamTable,
        _ => ExitCorner
    };

    public void Normalize()
    {
        Title ??= ExamBoardLayoutDefaults.CreateComponent(ExamBoardLayoutComponentId.Title);
        Clock ??= ExamBoardLayoutDefaults.CreateComponent(ExamBoardLayoutComponentId.Clock);
        ExamInfo ??= ExamBoardLayoutDefaults.CreateComponent(ExamBoardLayoutComponentId.ExamInfo);
        ExamTable ??= ExamBoardLayoutDefaults.CreateComponent(ExamBoardLayoutComponentId.ExamTable);
        ExitCorner ??= ExamBoardLayoutDefaults.CreateComponent(ExamBoardLayoutComponentId.ExitCorner);

        Title.Normalize();
        Clock.Normalize();
        ExamInfo.Normalize();
        ExamTable.Normalize();
        ExitCorner.Normalize();
    }

    public ExamBoardStatusLayout Clone() => new()
    {
        IsCustomized = IsCustomized,
        Title = Title.Clone(),
        Clock = Clock.Clone(),
        ExamInfo = ExamInfo.Clone(),
        ExamTable = ExamTable.Clone(),
        ExitCorner = ExitCorner.Clone()
    };
}