using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Corkboard.Platforms.Windows;

/// <summary>
///     「置底到桌面」的 Win32 实现：把窗口挂进桌面窗口树。
///     <para>
///         流程沿用社区通用做法：先向 <c>Progman</c> 发 <c>0x052C</c> 让 Explorer 生成承载壁纸的
///         <c>WorkerW</c>，再找出持有 <c>SHELLDLL_DefView</c>（桌面图标）的窗口当父窗口，
///         最后把目标窗口 <c>SetParent</c> 进去。成为桌面子窗口后，它会被所有普通窗口盖住，
///         也不会随「显示桌面」一起被隐藏。
///     </para>
///     <para>
///         拿不到桌面宿主窗口时退化成把窗口压到 Z 序最底（<c>HWND_BOTTOM</c>），
///         退化原因通过 <c>note</c> 交给调用方记录，不假装成功。
///     </para>
///     <para>
///         已知取舍：子窗口的键盘焦点取决于桌面窗口是否前台，因此挂到桌面层后，
///         窗口内的文本输入在桌面被激活时可能拿不到焦点，这是该形态的固有限制。
///     </para>
/// </summary>
internal static class WindowsDesktopBottom
{
    private const int GwlStyle = -16;
    private const long WsChild = 0x40000000L;
    private const long WsPopup = unchecked((long)0x80000000L);
    private const uint WmSpawnWorkerW = 0x052C;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint HwndTop = nint.Zero;
    private static readonly nint HwndBottom = new(1);

    /// <summary>句柄 → 挂载前的现场，用于解除时还原。这个表是进程内的，窗口销毁时由系统回收句柄。</summary>
    private static readonly ConcurrentDictionary<nint, Attachment> Attachments = [];

    private sealed record Attachment(nint OriginalParent, long OriginalStyle, bool IsEmbedded, nint DesktopHost);

    /// <summary>把窗口挂到桌面层。已经挂过的窗口直接算成功，避免重复 SetParent 打乱 Z 序。</summary>
    public static bool TryAttach(nint window, out string? failure, out string? note)
    {
        note = null;

        // 表里记着「已挂」不代表现在还挂着：窗口最小化再还原时，系统会把 WS_CHILD 摘掉，
        // 窗口悄悄回到顶层——不核对一次实际父窗口，就会一直按「已经置底」跳过，
        // 用户看到的是配置写着置底、窗口却浮在桌面之上。
        if (Attachments.TryGetValue(window, out var existing))
        {
            var stillAttached = !existing.IsEmbedded
                                || (existing.DesktopHost != nint.Zero && GetParent(window) == existing.DesktopHost);
            if (stillAttached)
            {
                failure = null;
                return true;
            }

            Attachments.TryRemove(window, out _);
        }

        try
        {
            Marshal.SetLastPInvokeError(0);
            var style = GetWindowLongPtr(window, GwlStyle).ToInt64();
            if (style == 0 && Marshal.GetLastWin32Error() != 0)
            {
                failure = $"GetWindowLongPtr failed with Win32 error {Marshal.GetLastWin32Error()}.";
                return false;
            }

            var originalParent = GetParent(window);
            var desktopHost = FindDesktopHostWindow();
            if (desktopHost == nint.Zero)
            {
                if (!SetWindowPos(window, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate))
                {
                    failure = $"SetWindowPos failed with Win32 error {Marshal.GetLastWin32Error()}.";
                    return false;
                }

                Attachments[window] = new Attachment(originalParent, style, IsEmbedded: false, nint.Zero);
                note = "The desktop host window (Progman/WorkerW) is unavailable; "
                       + "the window was only pushed to the bottom of the Z order.";
                failure = null;
                return true;
            }

            if (!GetWindowRect(window, out var bounds))
            {
                failure = $"GetWindowRect failed with Win32 error {Marshal.GetLastWin32Error()}.";
                return false;
            }

            // 子窗口坐标相对宿主客户区，因此先把屏幕坐标存下来，挂载后再换算回去。
            var origin = new NativePoint { X = bounds.Left, Y = bounds.Top };

            Marshal.SetLastPInvokeError(0);
            if (SetParent(window, desktopHost) == nint.Zero && Marshal.GetLastWin32Error() != 0)
            {
                failure = $"SetParent failed with Win32 error {Marshal.GetLastWin32Error()}.";
                return false;
            }

            Attachments[window] = new Attachment(originalParent, style, IsEmbedded: true, desktopHost);

            // SetParent 刻意不动窗口样式：不补 WS_CHILD 的话窗口仍按顶层窗口参与 Z 序计算。
            var childStyle = (style & ~WsPopup) | WsChild;
            if (childStyle != style && !TrySetWindowStyle(window, childStyle, out failure))
            {
                RollBack(window, originalParent, style);
                return false;
            }

            ScreenToClient(desktopHost, ref origin);

            // HWND_TOP 在这里是「宿主子窗口的最上层」，也就是盖住桌面图标、但仍被普通窗口盖住。
            if (!SetWindowPos(window, HwndTop, origin.X, origin.Y, bounds.Width, bounds.Height,
                    SwpNoActivate | SwpFrameChanged | SwpShowWindow))
            {
                failure = $"SetWindowPos failed with Win32 error {Marshal.GetLastWin32Error()}.";
                RollBack(window, originalParent, style);
                return false;
            }

            failure = null;
            return true;
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }
    }

    /// <summary>解除桌面层挂载并把窗口放回最前；没有挂载过的窗口直接算成功。</summary>
    public static bool TryDetach(nint window, out string? failure, out string? note)
    {
        note = null;
        if (!Attachments.TryRemove(window, out var attachment))
        {
            failure = null;
            return true;
        }

        try
        {
            if (!GetWindowRect(window, out var bounds))
            {
                failure = $"GetWindowRect failed with Win32 error {Marshal.GetLastWin32Error()}.";
                return false;
            }

            if (attachment.IsEmbedded)
            {
                Marshal.SetLastPInvokeError(0);
                if (SetParent(window, attachment.OriginalParent) == nint.Zero && Marshal.GetLastWin32Error() != 0)
                {
                    failure = $"SetParent failed with Win32 error {Marshal.GetLastWin32Error()}.";
                    return false;
                }

                if (!TrySetWindowStyle(window, attachment.OriginalStyle, out failure))
                    return false;
            }

            // 还原后把窗口拉回最前，否则用户会以为窗口消失了（它上一刻还压在 Z 序最底）。
            if (!SetWindowPos(window, HwndTop, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                    SwpNoActivate | SwpFrameChanged | SwpShowWindow))
            {
                failure = $"SetWindowPos failed with Win32 error {Marshal.GetLastWin32Error()}.";
                return false;
            }

            failure = null;
            return true;
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }
    }

    private static void RollBack(nint window, nint originalParent, long originalStyle)
    {
        Attachments.TryRemove(window, out _);
        SetParent(window, originalParent);
        TrySetWindowStyle(window, originalStyle, out _);
    }

    /// <summary>
    ///     找出承载桌面图标的窗口。桌面图标是 <c>SHELLDLL_DefView</c> 的子窗口，
    ///     它的父窗口就是我们要挂进去的桌面宿主（Win10/11 上是 WorkerW，部分系统上是 Progman）。
    /// </summary>
    private static nint FindDesktopHostWindow()
    {
        var progman = FindWindow("Progman", null);
        if (progman != nint.Zero)
        {
            // 让 Explorer 把壁纸层拆成独立的 WorkerW；发不出去也不致命，下面还有 Progman 兜底。
            SendMessageTimeout(progman, WmSpawnWorkerW, nint.Zero, nint.Zero, SmtoAbortIfHung, 1000, out _);
        }

        var host = nint.Zero;
        EnumWindowsProc callback = (window, _) =>
        {
            if (FindWindowEx(window, nint.Zero, "SHELLDLL_DefView", null) == nint.Zero)
                return true;

            host = window;
            return false;
        };

        EnumWindows(callback, nint.Zero);
        GC.KeepAlive(callback);

        return host != nint.Zero ? host : progman;
    }

    private static bool TrySetWindowStyle(nint window, long style, out string? failure)
    {
        Marshal.SetLastPInvokeError(0);
        var previous = SetWindowLongPtr(window, GwlStyle, (nint)style);
        if (previous == nint.Zero && Marshal.GetLastWin32Error() != 0)
        {
            failure = $"SetWindowLongPtr failed with Win32 error {Marshal.GetLastWin32Error()}.";
            return false;
        }

        failure = null;
        return true;
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetParent(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint hWndChild, nint hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint hWnd, ref NativePoint point);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessageTimeout(nint hWnd, uint message, nint wParam, nint lParam, uint flags,
        uint timeout, out nint result);
}
