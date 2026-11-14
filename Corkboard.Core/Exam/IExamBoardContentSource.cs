using System.Collections.ObjectModel;

namespace Corkboard.Core.Exam;

/// <summary>
///     看板渲染需要的最小内容源：当前看板文件 + 它的考试列表。
///     真实实现是 <see cref="IExamService" />，布局编辑窗用示例内容源实现它——两边共用同一套看板视图。
/// </summary>
public interface IExamBoardContentSource
{
    ExamBoardDocument ActiveBoard { get; }

    ReadOnlyObservableCollection<ExamEntry> Exams { get; }

    event EventHandler? Changed;
}

/// <summary>看板渲染需要的最小时间源：当前本地时间 + 状态变化通知。真实实现是 NTP 校时服务。</summary>
public interface IExamBoardTimeSource
{
    DateTime NowLocal { get; }

    event EventHandler? StatusChanged;
}