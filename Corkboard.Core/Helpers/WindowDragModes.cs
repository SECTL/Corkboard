using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models.SubConfigs.General;

namespace Corkboard.Core.Helpers;

/// <summary>
///     窗口拖动三档与两个落盘布尔值之间的折算。
///     <para>
///         设置页只有一个「拖动窗口」下拉框（<see cref="WindowDragMode" />），而配置里冻结着两个老键：
///         <c>HoldToDragWindow</c>（长按拖动）与 <c>WindowFixed</c>（固定窗口）。
///         三态用两个 bool 表示本来就有冗余，这里把「谁优先、怎么退」写成唯一一份，界面与主窗口都读它，
///         免得两边各判一套、出现「下拉框显示固定、窗口还能拖」。
///     </para>
///     <para>
///         优先级：固定 &gt; 长按拖动。固定开着时 <c>HoldToDragWindow</c> 的值不再有意义
///         （切到固定档不会把用户的「长按拖动」偏好清掉，切回来还是原样）。
///     </para>
/// </summary>
public static class WindowDragModes
{
    /// <summary>默认档：长按拖动（与加这个下拉框之前的出厂值一致）。</summary>
    public const WindowDragMode Default = WindowDragMode.HoldAnywhere;

    /// <summary>把两个落盘布尔值折算成界面上的三档。</summary>
    public static WindowDragMode FromConfig(bool holdToDragWindow, bool windowFixed)
    {
        if (windowFixed)
            return WindowDragMode.Fixed;

        return holdToDragWindow ? WindowDragMode.HoldAnywhere : WindowDragMode.TitleBarOnly;
    }

    /// <summary>
    ///     把三档写回配置。越界值（手改配置、未来新增档位）退回 <see cref="Default" />，
    ///     与其它枚举的兜底习惯一致。
    ///     <para>
    ///     ⚠️ 切到「固定」时<b>不动</b> <c>HoldToDragWindow</c>：固定只是压在长按拖动上面的一层，
    ///     用户的「长按拖动」偏好要留着，切回来还是原来那一档。所以这里不给出「全部写回」的纯函数形式——
    ///     三态折成两个 bool 本来就有冗余，谁被谁覆盖必须由这一处说了算（设置页与主窗口都读它）。
    ///     </para>
    /// </summary>
    /// <param name="basic">要写回的配置对象。</param>
    /// <param name="mode">用户选中的档位。</param>
    public static void ApplyTo(BasicSettingsConfig basic, WindowDragMode mode)
    {
        var normalized = Normalize(mode);

        if (normalized == WindowDragMode.Fixed)
        {
            // 只开固定：长按拖动的偏好留着。
            if (!basic.WindowFixed)
                basic.WindowFixed = true;

            return;
        }

        // 两个「能拖」的档位：先关固定，再按档位写长按拖动。
        if (basic.WindowFixed)
            basic.WindowFixed = false;

        var holdAnywhere = normalized == WindowDragMode.HoldAnywhere;
        if (basic.HoldToDragWindow != holdAnywhere)
            basic.HoldToDragWindow = holdAnywhere;
    }

    /// <summary>越界值兜底：只认三个已知档位，其余一律当默认档。</summary>
    public static WindowDragMode Normalize(WindowDragMode mode) => mode switch
    {
        WindowDragMode.HoldAnywhere => WindowDragMode.HoldAnywhere,
        WindowDragMode.TitleBarOnly => WindowDragMode.TitleBarOnly,
        WindowDragMode.Fixed => WindowDragMode.Fixed,
        _ => Default
    };

    /// <summary>当前档位下窗口能不能拖动（标题栏拖动与长按拖动都算）。</summary>
    public static bool AllowsDrag(WindowDragMode mode) => Normalize(mode) != WindowDragMode.Fixed;

    /// <summary>当前档位下窗口四边能不能拉着缩放。</summary>
    public static bool AllowsResize(WindowDragMode mode) => Normalize(mode) != WindowDragMode.Fixed;

    /// <summary>当前档位下「按住任意位置」能不能拖窗口（只有第一档可以）。</summary>
    public static bool AllowsHoldAnywhere(WindowDragMode mode) => Normalize(mode) == WindowDragMode.HoldAnywhere;
}
