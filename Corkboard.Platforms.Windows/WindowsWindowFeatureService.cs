using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Corkboard.Platforms.Abstractions;

namespace Corkboard.Platforms.Windows;

public sealed class WindowsWindowFeatureService : IWindowFeatureService
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExAppWindow = 0x00040000L;
    private const long WsExLayered = 0x00080000L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint WdaNone = 0x00000000;
    private const uint WdaExcludeFromCapture = 0x00000011;
    private const uint LwaAlpha = 0x00000002;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    /// <summary>
    ///     不透明度的下限：再低窗口就基本看不见了，用户会以为窗口没了。
    ///     设置页的滑杆比这个下限更高（见 <c>BasicSettingsPageViewModel</c>），
    ///     这里兜的是手改配置文件的值。
    /// </summary>
    private const double MinimumWindowOpacity = 0.05;

    /// <summary>每个标题栏按钮的标准宽度（Windows 11 为 46 设备无关像素）。</summary>
    private const int CaptionButtonFallbackWidth = 3 * 46;

    /// <summary><c>DWMWA_CAPTION_BUTTON_BOUNDS</c>：系统按钮矩形，相对窗口左上角的物理像素。</summary>
    private const int DwmwaCaptionButtonBounds = 5;
    private const WindowFeatures StyleFeatures = WindowFeatures.ToolWindow |
                                                WindowFeatures.SkipTaskSwitcher |
                                                WindowFeatures.NoActivate |
                                                WindowFeatures.ClickThrough;
    private static readonly nint HwndTopmost = new(-1);
    private static readonly nint HwndNotopmost = new(-2);

    /// <summary>每个窗口当前打开的能力位（只用于把 <c>desired</c> 算出来）。</summary>
    private readonly ConcurrentDictionary<nint, WindowFeatures> _enabledFeatures = [];

    /// <summary>
    ///     每个窗口当前的不透明度。<b>点击穿透与不透明度共用 <c>WS_EX_LAYERED</c></b>：
    ///     两条路径谁后动都要用同一份 alpha 重设分层属性，否则会互相把对方的设置抹掉。
    /// </summary>
    private readonly ConcurrentDictionary<nint, double> _windowOpacity = [];

    public WindowFeatures SupportedFeatures => WindowFeatures.Topmost |
                                                WindowFeatures.ToolWindow |
                                                WindowFeatures.SkipTaskSwitcher |
                                                WindowFeatures.NoActivate |
                                                WindowFeatures.ClickThrough |
                                                WindowFeatures.WindowOpacity |
                                                WindowFeatures.DesktopBottom |
                                                (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
                                                    ? WindowFeatures.ExcludeFromCapture
                                                    : WindowFeatures.None);

    public WindowFeatureApplyResult Apply(PlatformWindowHandle window, WindowFeatureRequest request)
    {
        if (!window.IsValid)
            return WindowFeatureApplyResult.Failed(request.Features, "The native window handle is not available.");

        if (!string.IsNullOrWhiteSpace(window.Descriptor)
            && !string.Equals(window.Descriptor, "HWND", StringComparison.OrdinalIgnoreCase))
        {
            return WindowFeatureApplyResult.Unsupported(request.Features,
                $"The '{window.Descriptor}' native handle is not a Windows HWND.");
        }

        var requested = request.Features & SupportedFeatures;
        var unsupported = request.Features & ~SupportedFeatures;
        if (requested == WindowFeatures.None)
            return WindowFeatureApplyResult.Partial(WindowFeatures.None, unsupported, WindowFeatures.None);

        _enabledFeatures.TryGetValue(window.Value, out var current);
        var desired = request.Enabled ? current | requested : current & ~requested;
        var applied = WindowFeatures.None;
        var failed = WindowFeatures.None;
        string? detail = null;

        if ((requested & WindowFeatures.Topmost) != 0)
        {
            if (TrySetTopmost(window.Value, (desired & WindowFeatures.Topmost) != 0, out var failure))
                applied |= WindowFeatures.Topmost;
            else
            {
                failed |= WindowFeatures.Topmost;
                detail ??= failure;
            }
        }

        if ((requested & WindowFeatures.ExcludeFromCapture) != 0)
        {
            if (TrySetCaptureExclusion(window.Value, (desired & WindowFeatures.ExcludeFromCapture) != 0, out var failure))
                applied |= WindowFeatures.ExcludeFromCapture;
            else
            {
                failed |= WindowFeatures.ExcludeFromCapture;
                detail ??= failure;
            }
        }

        if ((requested & WindowFeatures.DesktopBottom) != 0)
        {
            var enable = (desired & WindowFeatures.DesktopBottom) != 0;
            string? failure;
            string? note;
            var attached = enable
                ? WindowsDesktopBottom.TryAttach(window.Value, out failure, out note)
                : WindowsDesktopBottom.TryDetach(window.Value, out failure, out note);

            if (attached)
            {
                applied |= WindowFeatures.DesktopBottom;
                // 退化（例如拿不到桌面宿主窗口）时 note 会带上原因，调用方据此记录告警。
                detail ??= note;
            }
            else
            {
                failed |= WindowFeatures.DesktopBottom;
                detail ??= failure;
            }
        }

        if ((requested & WindowFeatures.WindowOpacity) != 0)
        {
            // 关闭该能力（Enabled = false）等价于恢复完全不透明：配置里没有「关掉不透明度」这种状态，
            // 1.0 就是不透明，平台这里统一成同一个值，免得两处各判一次。
            var opacity = request.Enabled ? request.Opacity : 1.0;
            var clickThrough = (desired & WindowFeatures.ClickThrough) != 0;
            if (TrySetWindowOpacity(window.Value, opacity, clickThrough, out var failure))
            {
                applied |= WindowFeatures.WindowOpacity;
                TrackWindowOpacity(window.Value, opacity);
            }
            else
            {
                failed |= WindowFeatures.WindowOpacity;
                detail ??= failure;
            }
        }

        var requestedStyles = requested & StyleFeatures;
        if (requestedStyles != WindowFeatures.None)
        {
            // 点击穿透那一路也要重设分层属性：它可能跟不透明度共用 WS_EX_LAYERED，
            // 只改样式不补 alpha 会把不透明度抹掉（反过来，只设样式不设属性窗口会整体不显示）。
            if (TrySetExtendedStyles(window.Value, desired, GetTrackedOpacity(window.Value), out var failure))
                applied |= requestedStyles;
            else
            {
                failed |= requestedStyles;
                detail ??= failure;
            }
        }

        // 不透明度不进能力位表：它是标量，当前值单独记（见 _windowOpacity）。
        UpdateTrackedFeatures(window.Value, current, applied & ~WindowFeatures.WindowOpacity, request.Enabled);
        return WindowFeatureApplyResult.Partial(applied, unsupported, failed, detail);
    }

    public double GetSystemCaptionButtonWidth(PlatformWindowHandle window)
    {
        if (!window.IsValid)
            return 0;

        if (!string.IsNullOrWhiteSpace(window.Descriptor)
            && !string.Equals(window.Descriptor, "HWND", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        try
        {
            // DWM 给的是「相对窗口左上角」的物理像素矩形，除上窗口 DPI 缩放换算成设备无关像素。
            if (DwmGetWindowAttribute(window.Value, DwmwaCaptionButtonBounds, out var bounds,
                    Marshal.SizeOf<NativeRect>()) == 0)
            {
                var width = bounds.Right - bounds.Left;
                var dpi = GetDpiForWindow(window.Value);
                if (width > 0 && dpi > 0)
                    return width * 96.0 / dpi;
            }
        }
        catch (DllNotFoundException)
        {
            // 没有 dwmapi 时退回固定值。
        }
        catch (EntryPointNotFoundException)
        {
            // 老系统上没有这个导出时退回固定值。
        }

        return CaptionButtonFallbackWidth;
    }

    private void UpdateTrackedFeatures(nint window, WindowFeatures current, WindowFeatures applied, bool enabled)
    {
        var updated = enabled ? current | applied : current & ~applied;
        if (updated == WindowFeatures.None)
            _enabledFeatures.TryRemove(window, out _);
        else
            _enabledFeatures[window] = updated;
    }

    private double GetTrackedOpacity(nint window) =>
        _windowOpacity.TryGetValue(window, out var opacity) ? opacity : 1.0;

    /// <summary>
    ///     记下窗口当前的不透明度。等于 1.0 时把记录清掉（1.0 就是默认值，不需要记），
    ///     免得窗口销毁后残留在表里（句柄会被系统复用）。点击穿透要继续分层不受影响：
    ///     那条路自己就会把 <c>WS_EX_LAYERED</c> 加上。
    /// </summary>
    private void TrackWindowOpacity(nint window, double opacity)
    {
        var clamped = ClampOpacity(opacity);
        if (clamped >= 1.0)
            _windowOpacity.TryRemove(window, out _);
        else
            _windowOpacity[window] = clamped;
    }

    private static double ClampOpacity(double opacity) =>
        double.IsFinite(opacity) ? Math.Clamp(opacity, MinimumWindowOpacity, 1.0) : 1.0;

    /// <summary>
    ///     按 <c>SetLayeredWindowAttributes</c> 写窗口的均匀 alpha。
    ///     <b>只在窗口已经有 <c>WS_EX_LAYERED</c> 时调用</b>，否则这个调用会失败。
    /// </summary>
    private static bool TryApplyLayeredAttributes(nint window, double opacity, out string? failure)
    {
        var alpha = (byte)Math.Round(ClampOpacity(opacity) * 255);
        if (SetLayeredWindowAttributes(window, 0, alpha, LwaAlpha))
        {
            failure = null;
            return true;
        }

        failure = $"SetLayeredWindowAttributes failed with Win32 error {Marshal.GetLastWin32Error()}.";
        return false;
    }

    /// <summary>
    ///     设置窗口整体不透明度。走分层窗口的均匀 alpha：
    ///     <list type="bullet">
    ///         <item>先把 <c>WS_EX_LAYERED</c> 加上（Avalonia 自己不会加，所以不会被它覆盖）；</item>
    ///         <item>再写 alpha；<b>两步缺一不可</b>——只设样式不写分层属性，窗口在 Windows 上整体不显示；</item>
    ///         <item><paramref name="keepLayered" /> 为真表示点击穿透正开着，那 <c>WS_EX_LAYERED</c> 必须留着，
    ///         所以这一路只改 alpha、不摘样式。</item>
    ///     </list>
    /// </summary>
    private static bool TrySetWindowOpacity(nint window, double opacity, bool keepLayered, out string? failure)
    {
        try
        {
            var clamped = ClampOpacity(opacity);
            Marshal.SetLastPInvokeError(0);
            var style = GetWindowLongPtr(window, GwlExStyle).ToInt64();
            if (style == 0 && Marshal.GetLastWin32Error() != 0)
            {
                failure = $"GetWindowLongPtr failed with Win32 error {Marshal.GetLastWin32Error()}.";
                return false;
            }

            // 不透明且不需要穿透时就把分层样式摘掉，让窗口回到默认形态；否则留着并重设 alpha。
            var needsLayered = keepLayered || clamped < 1.0;
            var updated = SetFlag(style, WsExLayered, needsLayered);
            if (updated != style)
            {
                Marshal.SetLastPInvokeError(0);
                var previous = SetWindowLongPtr(window, GwlExStyle, new IntPtr(updated));
                if (previous == nint.Zero && Marshal.GetLastWin32Error() != 0)
                {
                    failure = $"SetWindowLongPtr failed with Win32 error {Marshal.GetLastWin32Error()}.";
                    return false;
                }
            }

            if (needsLayered && !TryApplyLayeredAttributes(window, clamped, out failure))
                return false;

            return TryRefreshFrame(window, out failure);
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }
    }

    /// <summary>
    ///     改完扩展样式后让窗口重新算一遍非工作区。样式与分层属性都要经这一步才生效。
    /// </summary>
    private static bool TryRefreshFrame(nint window, out string? failure)
    {
        if (SetWindowPos(window, nint.Zero, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged))
        {
            failure = null;
            return true;
        }

        failure = $"SetWindowPos failed with Win32 error {Marshal.GetLastWin32Error()}.";
        return false;
    }

    private static bool TrySetTopmost(nint window, bool enabled, out string? failure)
    {
        try
        {
            var insertAfter = enabled ? HwndTopmost : HwndNotopmost;
            if (SetWindowPos(window, insertAfter, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged))
            {
                failure = null;
                return true;
            }

            failure = $"SetWindowPos failed with Win32 error {Marshal.GetLastWin32Error()}.";
            return false;
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }
    }

    private static bool TrySetCaptureExclusion(nint window, bool enabled, out string? failure)
    {
        try
        {
            if (SetWindowDisplayAffinity(window, enabled ? WdaExcludeFromCapture : WdaNone))
            {
                failure = null;
                return true;
            }

            failure = $"SetWindowDisplayAffinity failed with Win32 error {Marshal.GetLastWin32Error()}.";
            return false;
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }
    }

    private static bool TrySetExtendedStyles(nint window, WindowFeatures desired, double opacity, out string? failure)
    {
        try
        {
            Marshal.SetLastPInvokeError(0);
            var style = GetWindowLongPtr(window, GwlExStyle).ToInt64();
            if (style == 0 && Marshal.GetLastWin32Error() != 0)
            {
                failure = $"GetWindowLongPtr failed with Win32 error {Marshal.GetLastWin32Error()}.";
                return false;
            }

            var updated = style;
            var isToolWindow = (desired & (WindowFeatures.ToolWindow | WindowFeatures.SkipTaskSwitcher)) != 0;
            updated = SetFlag(updated, WsExToolWindow, isToolWindow);
            if (isToolWindow)
                updated &= ~WsExAppWindow;
            updated = SetFlag(updated, WsExNoActivate, (desired & WindowFeatures.NoActivate) != 0);

            // 点击穿透要 WS_EX_LAYERED + WS_EX_TRANSPARENT；半透明窗口也要 WS_EX_LAYERED。
            // 两者共用这个样式，所以这里按「谁需要」一起算，再补一次分层属性（见 TrySetWindowOpacity）。
            var clickThrough = (desired & WindowFeatures.ClickThrough) != 0;
            var needsLayered = clickThrough || ClampOpacity(opacity) < 1.0;
            updated = SetFlag(updated, WsExLayered, needsLayered);
            updated = SetFlag(updated, WsExTransparent, clickThrough);

            if (updated != style)
            {
                Marshal.SetLastPInvokeError(0);
                var previous = SetWindowLongPtr(window, GwlExStyle, new IntPtr(updated));
                if (previous == nint.Zero && Marshal.GetLastWin32Error() != 0)
                {
                    failure = $"SetWindowLongPtr failed with Win32 error {Marshal.GetLastWin32Error()}.";
                    return false;
                }
            }

            // 开了穿透但没设过 alpha 的话，窗口会整体不显示（分层窗口必须有分层属性）。
            if (needsLayered && !TryApplyLayeredAttributes(window, opacity, out failure))
                return false;

            return TryRefreshFrame(window, out failure);
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }
    }

    private static long SetFlag(long value, long flag, bool enabled) => enabled ? value | flag : value & ~flag;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hWnd, int attribute, out NativeRect value, int size);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint hWnd, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint hWnd, uint affinity);
}
