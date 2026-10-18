using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Corkboard.Platforms.Abstractions;

namespace Corkboard.Platforms.Windows;

/// <summary>
///     用 <c>GetCursorPos</c> 取指针的屏幕物理坐标。
/// </summary>
/// <remarks>
///     <para>
///         <c>GetCursorPos</c> 给的是<b>虚拟屏幕</b>坐标系里的物理像素，与 Avalonia 的
///         <c>Window.Position</c> 同一套——进程是 per-monitor DPI aware，不需要再折算 DIP。
///         （别用 <c>GetCursorPos</c> 配 <c>ScreenToClient</c> 那套绕路：我们只要屏幕坐标。）
///     </para>
///     <para>
///         取不到（桌面没就绪、会话被锁等）时返回 <see langword="false" />，调用方退回兜底位置。
///     </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsPointerLocator : IPointerLocator
{
    public bool TryGetCursorPosition(out ScreenPoint position)
    {
        if (GetCursorPos(out var point))
        {
            position = new ScreenPoint(point.X, point.Y);
            return true;
        }

        position = default;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
