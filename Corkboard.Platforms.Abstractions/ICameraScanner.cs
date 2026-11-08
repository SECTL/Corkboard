namespace Corkboard.Platforms.Abstractions;

/// <summary>相机帧的像素排列方式（与具体平台无关，只描述字节怎么排）。</summary>
public enum CameraPixelFormat
{
    /// <summary>BGR 24 位，每像素 3 字节（OpenCV <c>Mat</c> 的默认排列）。</summary>
    Bgr24 = 0,

    /// <summary>BGRA 32 位，每像素 4 字节（Avalonia <c>WriteableBitmap</c> 的默认排列）。</summary>
    Bgra32 = 1,

    /// <summary>8 位灰度，每像素 1 字节。</summary>
    Gray8 = 2
}

/// <summary>相机朝向。桌面端通常只有一个默认相机，移动端才分前后。</summary>
public enum PlatformCameraFacing
{
    /// <summary>平台默认（桌面端都落在这里）。</summary>
    Default = 0,

    /// <summary>前置摄像头。</summary>
    Front = 1,

    /// <summary>后置摄像头。</summary>
    Rear = 2
}

/// <summary>
///     一台相机。
/// </summary>
/// <param name="Id">平台内稳定的设备标识（Windows 上是 <c>dshow:&lt;序号&gt;</c>）。</param>
/// <param name="DisplayName">给用户看的名字（Windows 上取 DirectShow 的 FriendlyName）。</param>
/// <param name="CaptureIndex">打开设备时用的后端索引（DirectShow / V4L2 / AVFoundation 各自的序号）。</param>
/// <param name="Facing">相机朝向。</param>
public sealed record PlatformCameraDevice(
    string Id,
    string DisplayName,
    int CaptureIndex,
    PlatformCameraFacing Facing = PlatformCameraFacing.Default);

/// <summary>
///     一帧原始像素。这里刻意<b>不放</b>任何图片类型（<c>Bitmap</c> / 编码后的 JPEG / PNG），
///     调用方拿到字节数组后自己决定是送解码器还是画到预览控件上——平台层因此不依赖 UI 框架。
/// </summary>
/// <param name="Pixels">按 <paramref name="Format" /> 排列、逐行连续（无行填充）的像素字节。</param>
/// <param name="Width">像素宽。</param>
/// <param name="Height">像素高。</param>
/// <param name="Format">像素排列方式。</param>
public sealed record CameraFrame(byte[] Pixels, int Width, int Height, CameraPixelFormat Format);

/// <summary>
///     相机扫码能力。只有「抓帧」这一件事：枚举设备、开/关、按固定间隔把最新一帧交给调用方。
///     <para>
///         帧以原始像素数组事件的形式送出，事件在采集线程上触发（不是 UI 线程），
///         调用方需要自己 <c>Dispatcher.Post</c> 回界面线程。
///     </para>
///     <para>
///         没有相机实现的平台返回 <c>UnsupportedCameraScanner</c>（<see cref="IsSupported" /> 为假），
///         界面据此整块隐藏相机扫码入口。
///     </para>
/// </summary>
public interface ICameraScanner : IAsyncDisposable
{
    /// <summary>本平台是否真的能抓帧（不只是「有没有接口」）；为假时界面隐藏相机入口。</summary>
    bool IsSupported { get; }

    /// <summary>枚举可用相机；不支持或枚举失败时返回空列表，不抛异常。</summary>
    Task<IReadOnlyList<PlatformCameraDevice>> GetDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>新的一帧。事件在采集线程上同步触发，处理器要尽快返回（慢处理器只会拖慢帧率，不会积压）。</summary>
    event EventHandler<CameraFrame>? FrameReceived;

    /// <summary>相机出错（打不开 / 采集中断）。参数是给用户看的中文说明。</summary>
    event EventHandler<string>? CameraError;

    /// <summary>
    ///     打开相机并开始出帧。<paramref name="device" /> 为 <see langword="null" /> 时用第一台。
    ///     返回 <see langword="false" /> 表示打不开（此时已触发 <see cref="CameraError" />），不抛异常。
    /// </summary>
    Task<bool> StartAsync(PlatformCameraDevice? device, CancellationToken cancellationToken = default);

    /// <summary>停止采集并释放设备。可以重复调用。</summary>
    Task StopAsync();
}