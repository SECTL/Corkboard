using Corkboard.Core.Exam.Layout;

namespace Corkboard.ViewModels.ExamBoard;

/// <summary>
///     布局编辑窗的 ViewModel 实现这个接口，供共享的 <c>ExamBoardView</c> 把「画布上点了哪个区块」回传。
///     放映窗的 ViewModel 不实现它，所以点画布没有任何额外行为。
/// </summary>
public interface IExamBoardLayoutEditingHost
{
    /// <summary>点击某个区块；点空白处为 <see langword="null" />（收起属性面板）。</summary>
    void SelectComponent(ExamBoardLayoutComponentId? component);
}