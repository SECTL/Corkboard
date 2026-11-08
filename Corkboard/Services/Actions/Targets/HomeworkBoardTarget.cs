using Corkboard.Core.Actions;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Services.Config;

namespace Corkboard.Services.Actions.Targets;

/// <summary>
///     作业板目标：桌面上那扇承载作业板的窗口（含收纳 / 停在桌面上的形态）。
///     <para>
///         <see cref="IsAvailable" /> 必须看 <see cref="BoardSettingsConfig.BoardEnabled" />：
///         「启用作业板」关掉时板子等于不存在，行动不能把它叫出来 —— 用户明确关过的东西，
///         被一条自动化重新弹出来会像是设置没生效。<see cref="Show" /> 自己再判一次，
///         因为执行器判完到这个调用之间用户可能刚好拨了开关。
///     </para>
///     <para>
///         <c>App.ShowMainWindow</c> 本身不查这个开关（托盘与启动路径要能用），所以判断放在这里。
///     </para>
/// </summary>
public sealed class HomeworkBoardTarget(MainConfigHandler configHandler) : IBoardTarget
{
    /// <inheritdoc />
    public string Id => BoardTargetIds.Homework;

    /// <inheritdoc />
    public string DisplayName => ActionResources.Get("Action_Target_Homework", "作业板");

    /// <inheritdoc />
    public bool IsAvailable => configHandler.Data.BoardSettings.BoardEnabled;

    /// <inheritdoc />
    public string? UnavailableReason =>
        IsAvailable ? null : ActionResources.Get("Action_Message_Unavailable", "作业板已在设置里关闭");

    /// <inheritdoc />
    public bool IsVisible => App.IsMainWindowShown;

    /// <inheritdoc />
    public bool IsFullScreen => false;

    /// <inheritdoc />
    public void Show()
    {
        if (!IsAvailable)
            return;

        App.ShowMainWindow();
    }

    /// <inheritdoc />
    public void Hide()
    {
        // 幂等：已经不在屏幕上就什么都不做（老服务的让位与执行器的全屏互斥可能各调一次）。
        if (!IsVisible)
            return;

        App.HideMainWindow();
    }
}
