using Corkboard.Platforms.Abstractions;

namespace Corkboard.Platforms;

/// <summary>
///     没有相机抓帧实现的兜底：报不支持、枚举空、永远打不开。
///     界面读 <see cref="IsSupported" /> 把「扫码导入」整块隐藏，而不是留一个点不动的按钮。
/// </summary>
public sealed class UnsupportedCameraScanner : ICameraScanner
{
    public static UnsupportedCameraScanner Instance { get; } = new();

    private UnsupportedCameraScanner()
    {
    }

    public bool IsSupported => false;

    // 空实现不该被订阅者惦记：显式给空访问器，免得编译器报「事件从未被使用」。
    public event EventHandler<CameraFrame>? FrameReceived
    {
        add { }
        remove { }
    }

    public event EventHandler<string>? CameraError
    {
        add { }
        remove { }
    }

    public Task<IReadOnlyList<PlatformCameraDevice>> GetDevicesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PlatformCameraDevice>>([]);

    public Task<bool> StartAsync(PlatformCameraDevice? device, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task StopAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}