using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;
using FluentAvalonia.UI.Navigation;

namespace Corkboard.Helpers;

/// <summary>
///     页面切换的动效策略。
///     <para>
///         内容宿主是 FluentAvalonia 的 <see cref="FAFrame" />，它的入场过渡来自框架自身的样式；
///         所以「关闭动画」这类设置必须在这里显式换成 <see cref="FASuppressNavigationTransitionInfo" />，
///         光改自己的样式是看不见效果的。
///     </para>
/// </summary>
internal static class UiMotion
{
    /// <summary>是否处于低动效模式；由设置页写入。</summary>
    public static bool IsReduced { get; set; }

    public static void NavigateFromObject<T>(FAFrame frame, T target)
    {
        if (!IsReduced)
        {
            frame.NavigateFromObject(target);
            return;
        }

        frame.NavigateFromObject(target, new FAFrameNavigationOptions
        {
            IsNavigationStackEnabled = frame.IsNavigationStackEnabled,
            TransitionInfoOverride = new FASuppressNavigationTransitionInfo()
        });
    }
}
