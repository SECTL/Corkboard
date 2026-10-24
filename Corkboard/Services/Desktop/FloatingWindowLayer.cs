using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Corkboard.Core.Abstraction;
using Corkboard.Platforms.Abstractions;

namespace Corkboard.Services.Desktop;

/// <summary>
///     主窗口之外那几颗浮钮（收纳展开按钮、锁定徽标、右下角布置作业浮标）的层级归属。
///     <para>
///         它们各自是一扇独立的无边框小窗口，其中两颗 XAML 里默认 <c>Topmost="True"</c>——压在一切窗口之上，
///         这样主窗口被别的东西盖住时仍然点得到。但主窗口开了「置底到桌面」之后，板子已经沉到桌面层了，
///         这几颗按钮再浮在所有窗口之上就很突兀（用户反馈：
///         「开了置底时，收纳和锁定后的按钮是置顶的，这个按钮也要改成置底」）。
///     </para>
///     <para>
///         所以置底生效时它们<b>跟板子一样挂进桌面层</b>（<see cref="WindowFeatures.DesktopBottom" />，
///         Windows 上是 <c>SetParent</c> 到承载桌面图标的 <c>Progman</c> / <c>WorkerW</c>）：
///         和板子待在同一个窗口树里，被所有普通窗口盖住、也不随「显示桌面」一起消失。
///     </para>
///     <para>
///         ⚠️ <b>只退掉 <c>Topmost</c> 是不够的</b>（曾经就是这么做的）：那样窗口只是从「置顶」变回
///         「普通顶层窗口」，仍然浮在桌面层之上，用户看到的还是「板子沉下去了、按钮没沉」。
///         真置底必须改父窗口，而改完父窗口就带来下面这条坐标系问题。
///     </para>
///     <para>
///         ⚠️ <b>挂进桌面层之后不能再直接写 <see cref="Window.Position" /></b>：它成了桌面宿主的子窗口，
///         而子窗口的 <c>SetWindowPos</c> 坐标是相对宿主客户区的。主显示器上宿主客户区原点正好在 (0,0)，
///         写屏幕坐标「碰巧是对的」，副显示器（原点为负）或宿主不铺满整屏时就会整块偏掉。
///         所以浮钮的定位一律走 <see cref="Place" />，由平台换算一次。
///     </para>
///     <para>
///         ⚠️ 窗口创建之后要立刻 <see cref="Register" /> 一次（在任何 <see cref="Apply" /> 之前）：
///         三颗按钮的默认层级并不一致（右下角布置作业浮标 XAML 里<b>没有</b> <c>Topmost</c>），
///         不能拿「置顶」当所有人的兜底值，否则会把原本不置顶的窗口改成置顶。
///     </para>
/// </summary>
public static class FloatingWindowLayer
{
    /// <summary>
    ///     每个浮钮窗口自己的默认层级（XAML 里写的那个值）。
    ///     用弱引用表：窗口关掉后条目自己消失，不用手工清理。
    /// </summary>
    private static readonly ConditionalWeakTable<Window, StrongBox<bool>> Baselines = new();

    /// <summary>
    ///     记下这颗浮钮「本来」是不是置顶。窗口一创建就调一次，之后 <see cref="Apply" /> 才有还原的基准。
    /// </summary>
    public static void Register(Window window)
    {
        if (!Baselines.TryGetValue(window, out _))
            Baselines.Add(window, new StrongBox<bool>(window.Topmost));
    }

    /// <summary>
    ///     按主窗口是否置底，把这颗浮钮也放进桌面层（或从桌面层拿出来）。
    /// </summary>
    /// <param name="window">浮钮窗口。</param>
    /// <param name="desktopBottom">主窗口是不是真的置底了（取实际生效值，不是配置值）。</param>
    public static void Apply(Window window, bool desktopBottom)
    {
        // 没登记过的窗口按它此刻的层级当基准（幂等：之后不会再来回漂）。
        Register(window);

        if (desktopBottom)
        {
            // 先退掉置顶再挂载：置顶样式对子窗口没有意义，留着只会在摘挂载那一刻突然又冒出来。
            if (window.Topmost)
                window.Topmost = false;

            ApplyDesktopBottom(window, enabled: true);
            return;
        }

        // 顺序反过来：先摘挂载恢复成顶层窗口，再按它自己的默认层级还原置顶。
        ApplyDesktopBottom(window, enabled: false);

        var topmost = Baselines.TryGetValue(window, out var baseline) && baseline.Value;
        if (window.Topmost != topmost)
            window.Topmost = topmost;
    }

    /// <summary>
    ///     把浮钮摆到屏幕坐标（物理像素）。
    ///     <para>
    ///         没挂进桌面层时与直接写 <see cref="Window.Position" /> 等价；挂进去之后由平台把屏幕坐标
    ///         换算成桌面宿主的客户区坐标（见类注释里那条坐标系警告）。
    ///         平台不支持桌面层能力时返回 <c>false</c>，这里自己退回去写 <see cref="Window.Position" />。
    ///     </para>
    /// </summary>
    public static void Place(Window window, PixelPoint screenPoint)
    {
        if (IAppHost.TryGetService<IWindowFeatureService>() is { } features
            && window.TryGetPlatformHandle() is { } handle
            && features.TryPlaceAtScreenPoint(
                new PlatformWindowHandle(handle.Handle, handle.HandleDescriptor),
                new ScreenPoint(screenPoint.X, screenPoint.Y)))
        {
            return;
        }

        window.Position = screenPoint;
    }

    /// <summary>
    ///     请求平台把这颗浮钮挂进 / 拿出桌面层。窗口还没创建出原生句柄时静默跳过——
    ///     调用方本来就会在显示之后再同步一次（见 <c>MainWindow.SyncLockBadge</c>）。
    /// </summary>
    private static void ApplyDesktopBottom(Window window, bool enabled)
    {
        if (IAppHost.TryGetService<IWindowFeatureService>() is not { } features)
            return;

        if (window.TryGetPlatformHandle() is not { } handle)
            return;

        features.Apply(
            new PlatformWindowHandle(handle.Handle, handle.HandleDescriptor),
            new WindowFeatureRequest(WindowFeatures.DesktopBottom, enabled));
    }
}
