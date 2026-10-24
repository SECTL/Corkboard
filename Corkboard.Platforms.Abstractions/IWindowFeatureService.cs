namespace Corkboard.Platforms.Abstractions;

public interface IWindowFeatureService
{
    WindowFeatures SupportedFeatures { get; }

    WindowFeatureApplyResult Apply(PlatformWindowHandle window, WindowFeatureRequest request);

    /// <summary>
    ///     把窗口摆到屏幕坐标（物理像素）。
    ///     <para>
    ///         看起来跟直接写 <c>Window.Position</c> 是一回事，只有一处差别，而那一处恰好很要命：
    ///         窗口<b>已经挂进桌面层</b>（<see cref="WindowFeatures.DesktopBottom" />）之后它成了桌面宿主的
    ///         子窗口，而子窗口的 <c>SetWindowPos</c> 坐标是相对宿主客户区的——照屏幕坐标写下去，
    ///         主显示器上因为宿主客户区原点正好是 (0,0) 而「碰巧是对的」，副显示器（原点为负）或
    ///         桌面宿主不铺满整屏时就会整块偏掉。所以挂载之后必须由平台把屏幕坐标换算成宿主客户区坐标。
    ///     </para>
    ///     <para>
    ///         没挂载的窗口按普通顶层窗口处理，与 <c>Window.Position</c> 等价。
    ///         平台不支持 <see cref="WindowFeatures.DesktopBottom" /> 时返回 <c>false</c>，调用方自己退回去写 <c>Position</c>。
    ///     </para>
    /// </summary>
    /// <returns>是否已经摆好。返回 <c>false</c> 时窗口位置没有被改动。</returns>
    bool TryPlaceAtScreenPoint(PlatformWindowHandle window, ScreenPoint position);

    /// <summary>
    ///     窗口右上角系统按钮（最小化 / 最大化 / 关闭）占用的宽度，单位与界面同一套设备无关像素。
    /// </summary>
    /// <remarks>
    ///     自绘标题栏（<c>FAAppWindow</c> 的 <c>TitleBar.ExtendsContentIntoTitleBar</c>）里，
    ///     页面内容一直铺到系统按钮底下，靠右的内容会被它们压住。要让某个控件紧挨着最小化按钮左边，
    ///     就得先按这个宽度把右侧让出来。平台没有系统按钮（按钮画在窗口外、或画在左侧）时返回 0，
    ///     调用方据此退化成「不留空位」。
    /// </remarks>
    double GetSystemCaptionButtonWidth(PlatformWindowHandle window);
}
