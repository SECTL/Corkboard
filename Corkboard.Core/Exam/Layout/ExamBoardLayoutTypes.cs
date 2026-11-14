namespace Corkboard.Core.Exam.Layout;

/// <summary>看板里可独立摆放的区块。id 是配置字段的语义来源，不直接落盘。</summary>
public enum ExamBoardLayoutComponentId
{
    Title = 0,
    Clock = 1,
    ExamInfo = 2,
    ExamTable = 3,
    ExitCorner = 4
}

/// <summary>八个缩放手柄。</summary>
public enum ExamBoardResizeHandle
{
    TopLeft = 0,
    Top = 1,
    TopRight = 2,
    Right = 3,
    BottomRight = 4,
    Bottom = 5,
    BottomLeft = 6,
    Left = 7
}

/// <summary>不含控件的百分比矩形（0–100，相对看板客户区）。</summary>
public readonly record struct ExamBoardLayoutRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public static ExamBoardLayoutRect FromComponent(Models.SubConfigs.Exam.ExamBoardComponentLayout layout) =>
        new(layout.X, layout.Y, layout.Width, layout.Height);
}