namespace Corkboard.Platforms.Abstractions;

/// <summary>
///     屏幕坐标（<b>物理像素</b>，与 <c>Avalonia.PixelPoint</c> / <c>Window.Position</c> 同一套坐标系）。
/// </summary>
/// <remarks>
///     刻意用物理像素而不是设备无关像素：调用方（窗口定位）最终要写回 <c>Window.Position</c>，
///     那边就是物理像素，中间再折算一次只会引入舍入误差。
/// </remarks>
public readonly record struct ScreenPoint(int X, int Y);
