using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Corkboard.Platforms.Abstractions;
using OpenCvSharp;

namespace Corkboard.Platforms.Windows;

/// <summary>
///     Windows 上的相机抓帧实现：DirectShow 打开设备、OpenCV <c>VideoCapture</c> 出帧，
///     每帧翻成紧凑的 BGR24 原始像素交给调用方（<c>QrFrameDecoder</c> 直接吃这一份）。
///     <para>
///         平台层<b>只出帧</b>：不编码 JPEG/PNG、不碰 Avalonia 位图、不做 QR 解码——
///         预览控件与解码器都在上层，所以这里可以完全不依赖 UI 框架。
///     </para>
///     <para>
///         为什么 <see cref="IsSupported" /> 还要按架构挡：<c>OpenCvSharp4.runtime.win</c> 只带
///         <b>win-x64</b> 的原生库（<c>OpenCvSharpExtern.dll</c> 等），x86 / arm64 进程加载不了，
///         真去 <c>new VideoCapture</c> 会抛 <c>DllNotFoundException</c>。这里提前探测一次并缓存结果，
///         不支持的架构直接报假、界面藏掉入口，而不是等用户点了才炸。
///     </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class OpenCvCameraScanner : ICameraScanner
{
    /// <summary>预览与扫码都不需要满帧率：约 10fps 足够，也让 CPU 与解码线程喘口气。</summary>
    private static readonly TimeSpan CaptureInterval = TimeSpan.FromMilliseconds(100);

    private const int TargetWidth = 1280;
    private const int TargetHeight = 720;

    /// <summary>探测一轮原生库能不能加载；结果只算一次（结果本身与设备无关）。</summary>
    private static readonly Lazy<bool> Supported = new(ProbeSupport, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly WindowsCameraDeviceCatalog _catalog;

    /// <summary>串行化开/停：两个调用同时按下时不至于把设备开两次、释放两次。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private VideoCapture? _capture;
    private CancellationTokenSource? _cancellation;
    private Task? _loop;
    private bool _disposed;

    public OpenCvCameraScanner()
        : this(new WindowsCameraDeviceCatalog())
    {
    }

    /// <summary>允许注入设备枚举（默认用 DirectShow 那份；测试/换实现时用）。</summary>
    public OpenCvCameraScanner(WindowsCameraDeviceCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    public bool IsSupported => Supported.Value;

    public event EventHandler<CameraFrame>? FrameReceived;

    public event EventHandler<string>? CameraError;

    public Task<IReadOnlyList<PlatformCameraDevice>> GetDevicesAsync(
        CancellationToken cancellationToken = default) => _catalog.GetAvailableAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> StartAsync(PlatformCameraDevice? device, CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            RaiseError("当前平台不支持相机扫码");
            return false;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
                return false;

            // 重复 Start 视为「换设备」：先停掉旧的，别让两台设备同时开着。
            await StopCoreAsync().ConfigureAwait(false);

            var captureIndex = device?.CaptureIndex ?? 0;
            VideoCapture? capture;
            try
            {
                // 打开与设分辨率都会进原生层（DirectShow 可能要枚举/协商几百毫秒），
                // 一律丢到线程池，别卡 UI 线程。
                capture = await Task.Run(() => OpenCapture(captureIndex), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                RaiseError($"打不开相机：{exception.Message}");
                return false;
            }

            if (capture is null)
            {
                RaiseError(device is null
                    ? "没有找到可用的相机"
                    : $"打不开相机「{device.DisplayName}」，可能被其他程序占用");
                return false;
            }

            _capture = capture;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _cancellation.Token;
            _loop = Task.Run(() => CaptureLoopAsync(capture, token), CancellationToken.None);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>真正打开设备（在线程池上调）。失败返回 <see langword="null" />，不抛（除取消外）。</summary>
    private static VideoCapture? OpenCapture(int captureIndex)
    {
        // 必须指定 DSHOW：不指定时 OpenCV 会依次试 MSMF/DSHOW，Windows 上经常先撞上 MSMF 的
        // 慢初始化，同一台相机被两种 API 打开还可能互相抢占。
        var capture = new VideoCapture(captureIndex, VideoCaptureAPIs.DSHOW);
        try
        {
            if (!capture.IsOpened())
                return null;

            // 分辨率只是「请求」，驱动给什么就采什么（下面按实际 Cols/Rows 出帧）。
            capture.Set(VideoCaptureProperties.FrameWidth, TargetWidth);
            capture.Set(VideoCaptureProperties.FrameHeight, TargetHeight);
            return capture;
        }
        catch (Exception)
        {
            capture.Dispose();
            return null;
        }
    }

    /// <summary>
    ///     采集循环：每轮读一帧、翻成 BGR24、触发事件，然后歇约 100ms。
    ///     <para>
    ///         <b>单槽、不积压</b>：这里没有帧队列，事件在采集线程上同步触发；处理器慢只会把这一轮拉长，
    ///         下一轮 <c>Read</c> 永远从驱动里取「此刻最新的一帧」，所以慢解码时中间帧自然被丢掉，
    ///         不存在越积越多、越放越旧的延迟。
    ///     </para>
    /// </summary>
    private async Task CaptureLoopAsync(VideoCapture capture, CancellationToken cancellationToken)
    {
        using var frame = new Mat();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (capture.Read(frame) && !frame.Empty())
                        PublishFrame(frame);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // 设备被拔掉 / 驱动失效：报一次错就退出，继续空转只会刷屏。
                    RaiseError($"读取相机画面失败：{exception.Message}");
                    break;
                }

                await Task.Delay(CaptureInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止。
        }
        catch (Exception exception)
        {
            if (!cancellationToken.IsCancellationRequested)
                RaiseError($"相机采集中断：{exception.Message}");
        }
    }

    /// <summary>把 <c>Mat</c> 翻成紧凑的无行填充 BGR24 字节并送出。</summary>
    private void PublishFrame(Mat frame)
    {
        var width = frame.Cols;
        var height = frame.Rows;
        if (width <= 0 || height <= 0)
            return;

        Mat working = frame;
        Mat? converted = null;
        try
        {
            // DirectShow 正常给 3 通道 BGR；个别驱动/虚拟相机会给 BGRA 或灰度，统一转成 BGR。
            if (frame.Channels() != 3)
            {
                converted = new Mat();
                Cv2.CvtColor(frame, converted,
                    frame.Channels() == 4 ? ColorConversionCodes.BGRA2BGR : ColorConversionCodes.GRAY2BGR);
                working = converted;
            }

            // ⚠️ Mat.Data 是按 Step() 对齐的，行尾可能带填充；直接 FromBytes 会错行。
            // 这里逐行拷到「宽 × 高 × 3」连续数组里，出去的数据才可以按 width*height*3 直接索引。
            var rowBytes = width * 3;
            var step = (int)working.Step();
            var pixels = new byte[rowBytes * height];
            var data = working.Data;
            for (var row = 0; row < height; row++)
                Marshal.Copy(IntPtr.Add(data, row * step), pixels, row * rowBytes, rowBytes);

            RaiseFrame(new CameraFrame(pixels, width, height, CameraPixelFormat.Bgr24));
        }
        finally
        {
            converted?.Dispose();
        }
    }

    private void RaiseFrame(CameraFrame frame)
    {
        try
        {
            FrameReceived?.Invoke(this, frame);
        }
        catch (Exception)
        {
            // 调用方的处理器抛异常不能把采集循环带崩；这一帧当没送到。
        }
    }

    private void RaiseError(string message)
    {
        try
        {
            CameraError?.Invoke(this, message);
        }
        catch (Exception)
        {
            // 错误处理器自己抛异常时同样吞掉，避免把错误上报变成二次崩溃。
        }
    }

    /// <summary>停采集 + 释放设备。调用方必须已持有 <see cref="_gate" />；可重复调用。</summary>
    private async Task StopCoreAsync()
    {
        var cancellation = _cancellation;
        var loop = _loop;
        var capture = _capture;
        _cancellation = null;
        _loop = null;
        _capture = null;

        cancellation?.Cancel();

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 正常停止。
            }
            catch (Exception)
            {
                // 循环内部已经报过错，这里只负责等它退干净。
            }
        }

        cancellation?.Dispose();

        if (capture is not null)
        {
            try
            {
                capture.Release();
            }
            catch (Exception)
            {
                // 释放失败也继续 Dispose，别把停止流程卡住。
            }

            capture.Dispose();
        }
    }

    /// <summary>探测原生库能否加载：Windows + x64 + <c>Cv2.GetVersionString()</c> 不抛。</summary>
    private static bool ProbeSupport()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        // runtime.win 只有 win-x64：x86 与 arm64 进程加载不了 OpenCvSharpExtern。
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            return false;

        try
        {
            return !string.IsNullOrWhiteSpace(Cv2.GetVersionString());
        }
        catch (Exception)
        {
            // DllNotFoundException / BadImageFormatException：原生库没随包分发或架构不对。
            return false;
        }
    }
}