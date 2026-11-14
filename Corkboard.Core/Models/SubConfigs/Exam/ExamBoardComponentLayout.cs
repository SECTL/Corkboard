using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Exam.Layout;

namespace Corkboard.Core.Models.SubConfigs.Exam;

/// <summary>
///     一个组件在自定义布局里的百分比矩形与样式。所有数值都由 <see cref="Normalize" /> 夹取。
/// </summary>
public partial class ExamBoardComponentLayout : ObservableObject
{
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _width = 100;
    [ObservableProperty] private double _height = 100;
    [ObservableProperty] private double _scale = 1;
    [ObservableProperty] private double _opacity = 1;
    [ObservableProperty] private ExamBoardContentAlignment _contentAlignment =
        ExamBoardContentAlignment.MiddleCenter;

    public void Normalize()
    {
        var rect = ExamBoardLayoutMath.ClampRect(new ExamBoardLayoutRect(X, Y, Width, Height));
        ExamBoardLayoutMath.Apply(rect, this);

        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, 0.5, 2) : 1;
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.2, 1) : 1;
        ContentAlignment = Enum.IsDefined(ContentAlignment)
            ? ContentAlignment
            : ExamBoardContentAlignment.MiddleCenter;
    }

    public ExamBoardComponentLayout Clone() => new()
    {
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        Scale = Scale,
        Opacity = Opacity,
        ContentAlignment = ContentAlignment
    };

    public ExamBoardLayoutRect ToRect() => new(X, Y, Width, Height);
}