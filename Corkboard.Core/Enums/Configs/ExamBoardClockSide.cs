namespace Corkboard.Core.Enums.Configs;

/// <summary>
///     全屏考试看板里「时钟」与「考试列表」谁在左谁在右。枚举按数字落盘，只能在末尾追加成员。
/// </summary>
public enum ExamBoardClockSide
{
    /// <summary>时钟在左、考试列表在右（默认，也是大多数人习惯的读法）。</summary>
    Left = 0,

    /// <summary>考试列表在左、时钟在右。</summary>
    Right = 1
}
