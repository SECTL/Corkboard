namespace Corkboard.Core.Exam;

/// <summary>
///     一场考试相对于「此刻」处于哪一档。枚举按数字落盘（历史日志、快照里可能出现），
///     所以只能在末尾追加成员。
/// </summary>
public enum ExamStatus
{
    /// <summary>离开始还早（超过设置的「即将开始」阈值）。</summary>
    NotStarted = 0,

    /// <summary>已经进入开考前的那段窗口（阈值之内），还没打铃。</summary>
    Upcoming = 1,

    /// <summary>正在考。</summary>
    InProgress = 2,

    /// <summary>已经考完（收卷时刻已过）。看板不显示这类考试。</summary>
    Finished = 3
}
