using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models.SubConfigs.Exam;

namespace Corkboard.Core.Exam.Layout;

/// <summary>
///     五个组件的默认矩形、盒内对齐与编辑时的 DIP 下限。
///     默认矩形只作「量不到时」的兜底；正常首次编辑以实测 Bounds 为准。
/// </summary>
public static class ExamBoardLayoutDefaults
{
    private static readonly IReadOnlyDictionary<ExamBoardLayoutComponentId, ExamBoardLayoutRect> FallbackRects =
        new Dictionary<ExamBoardLayoutComponentId, ExamBoardLayoutRect>
        {
            [ExamBoardLayoutComponentId.Title] = new(3.3, 4.4, 88, 10),
            [ExamBoardLayoutComponentId.Clock] = new(6, 22, 38, 42),
            [ExamBoardLayoutComponentId.ExamInfo] = new(10, 64, 30, 26),
            [ExamBoardLayoutComponentId.ExamTable] = new(54.6, 20, 37.5, 62),
            [ExamBoardLayoutComponentId.ExitCorner] = new(3.3, 87, 4, 6)
        };

    private static readonly IReadOnlyDictionary<ExamBoardLayoutComponentId, (double Width, double Height)> MinimumDip =
        new Dictionary<ExamBoardLayoutComponentId, (double Width, double Height)>
        {
            [ExamBoardLayoutComponentId.Title] = (200, 48),
            [ExamBoardLayoutComponentId.Clock] = (180, 120),
            [ExamBoardLayoutComponentId.ExamInfo] = (200, 120),
            [ExamBoardLayoutComponentId.ExamTable] = (690, 200),
            [ExamBoardLayoutComponentId.ExitCorner] = (64, 64)
        };

    public static ExamBoardLayoutRect GetFallbackRect(ExamBoardLayoutComponentId component) =>
        FallbackRects[component];

    public static (double Width, double Height) GetMinimumDip(ExamBoardLayoutComponentId component) =>
        MinimumDip[component];

    public static ExamBoardContentAlignment GetDefaultAlignment(ExamBoardLayoutComponentId component) =>
        component == ExamBoardLayoutComponentId.Title
            ? ExamBoardContentAlignment.TopLeft
            : ExamBoardContentAlignment.MiddleCenter;

    public static ExamBoardComponentLayout CreateComponent(ExamBoardLayoutComponentId component)
    {
        var rect = GetFallbackRect(component);
        return new ExamBoardComponentLayout
        {
            X = rect.X,
            Y = rect.Y,
            Width = rect.Width,
            Height = rect.Height,
            Scale = 1,
            Opacity = 1,
            ContentAlignment = GetDefaultAlignment(component)
        };
    }

    public static ExamBoardStatusLayout CreateStatusLayout(ExamStatus status) => new()
    {
        IsCustomized = false,
        Title = CreateComponent(ExamBoardLayoutComponentId.Title),
        Clock = CreateComponent(ExamBoardLayoutComponentId.Clock),
        ExamInfo = CreateComponent(ExamBoardLayoutComponentId.ExamInfo),
        ExamTable = CreateComponent(ExamBoardLayoutComponentId.ExamTable),
        ExitCorner = CreateComponent(ExamBoardLayoutComponentId.ExitCorner)
    };
}