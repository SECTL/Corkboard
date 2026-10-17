using System;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Corkboard.Services.Desktop;

/// <summary>
///     应用图标的唯一来源：窗口（标题栏 / 任务栏）与托盘都用随包分发的 PNG 母图，
///     **不要**再用 ICO。
/// </summary>
/// <remarks>
///     <para>
///         用 PNG 而不是 ICO 的原因：PNG 母图只有一份、改图不用重新生成多尺寸容器，
///         而 Avalonia 会按平台需要的尺寸重新缩放位图，高 DPI 下也不会糊成一格一格
///         （ICO 里没带的尺寸只能硬插值）。
///     </para>
///     <para>
///         exe 文件图标（<c>Corkboard.Desktop.csproj</c> 的 <c>ApplicationIcon</c>）仍然是 ICO——
///         那是 PE 资源，Windows 只认 ICO，和运行期窗口图标不是一回事。
///     </para>
///     <para>
///         <see cref="Bitmap" /> 不释放：部分平台的 <see cref="WindowIcon" /> 实现直接持有它，
///         提前释放会在画图标时炸掉，所以缓存到进程结束。
///     </para>
/// </remarks>
public static class AppIcon
{
    private const string PngAsset = "avares://Corkboard/Assets/Icons/Corkboard.png";

    private static Bitmap? _bitmap;
    private static WindowIcon? _windowIcon;

    /// <summary>
    ///     窗口图标用的位图。<c>FAAppWindow.Icon</c> 的声明类型是 <see cref="Avalonia.Media.IImage" />
    ///     （它把基类 <c>Window.Icon</c> 藏起来了），所以这里直接给位图，不要包 <see cref="WindowIcon" />。
    /// </summary>
    public static Bitmap LoadBitmap() =>
        _bitmap ??= new Bitmap(AssetLoader.Open(new Uri(PngAsset)));

    /// <summary>托盘图标用的 <see cref="WindowIcon" />（<c>TrayIcon.Icon</c> 就收这个类型）。</summary>
    public static WindowIcon LoadWindowIcon() => _windowIcon ??= new WindowIcon(LoadBitmap());
}
