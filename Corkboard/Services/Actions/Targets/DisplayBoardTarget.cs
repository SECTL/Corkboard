using Corkboard.Core.Actions;

namespace Corkboard.Services.Actions.Targets;

/// <summary>
///     看板模式目标：全屏大时钟 / 一言 / 天气那扇窗口。
///     <para>
///         与考试看板同理，它没有「能不能用」的开关；「自动显示」是时段配置的事，
///         用户手动打开永远允许。
///     </para>
/// </summary>
public sealed class DisplayBoardTarget : IBoardTarget
{
    /// <inheritdoc />
    public string Id => BoardTargetIds.DisplayBoard;

    /// <inheritdoc />
    public string DisplayName => ActionResources.Get("Action_Target_Display", "看板模式");

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public string? UnavailableReason => null;

    /// <inheritdoc />
    public bool IsVisible => App.IsDisplayBoardWindowOpen;

    /// <inheritdoc />
    public bool IsFullScreen => true;

    /// <inheritdoc />
    public void Show() => App.ShowDisplayBoardWindow();

    /// <inheritdoc />
    public void Hide()
    {
        // 幂等：已经关了就不再碰 App 的静态字段与放映标志。
        if (!App.IsDisplayBoardWindowOpen)
            return;

        App.CloseDisplayBoardWindow();
    }
}
