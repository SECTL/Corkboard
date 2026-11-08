using Corkboard.Core.Actions;

namespace Corkboard.Services.Actions.Targets;

/// <summary>
///     考试看板目标：全屏放映窗口（最小化状态也算「还在屏幕上」，
///     <see cref="App.IsExamBoardWindowOpen" /> 已经把最小化算进去了）。
///     <para>
///         它没有「可用性」开关——看板窗口什么时候都能开，参不参加自动放映由每块看板的
///         <c>AutoShowEnabled</c> 决定，那是日程投影那一层的事，不是目标本身的事。
///     </para>
/// </summary>
public sealed class ExamBoardTarget : IBoardTarget
{
    /// <inheritdoc />
    public string Id => BoardTargetIds.ExamBoard;

    /// <inheritdoc />
    public string DisplayName => ActionResources.Get("Action_Target_Exam", "考试看板");

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public string? UnavailableReason => null;

    /// <inheritdoc />
    public bool IsVisible => App.IsExamBoardWindowOpen;

    /// <inheritdoc />
    public bool IsFullScreen => true;

    /// <inheritdoc />
    public void Show() => App.ShowExamBoardWindow();

    /// <inheritdoc />
    public void Hide()
    {
        // 幂等：已经关了就不再碰 App 的静态字段（否则会把「已关」的现场又改一遍）。
        if (!App.IsExamBoardWindowOpen)
            return;

        App.CloseExamBoardWindow();
    }
}
