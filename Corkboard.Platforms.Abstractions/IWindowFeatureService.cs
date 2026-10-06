namespace Corkboard.Platforms.Abstractions;

public interface IWindowFeatureService
{
    WindowFeatures SupportedFeatures { get; }

    WindowFeatureApplyResult Apply(PlatformWindowHandle window, WindowFeatureRequest request);

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
