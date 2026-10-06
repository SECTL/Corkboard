using System.Runtime.InteropServices;
using Corkboard.Platforms.Abstractions;

namespace Corkboard.Platforms.MacOs;

public sealed class MacOsWindowFeatureService : IWindowFeatureService
{
    private const nint NsNormalWindowLevel = 0;
    private const nint NsFloatingWindowLevel = 3;
    private const nint NsUtilityWindowStyleMask = 1 << 4;
    private const nint NsWindowCollectionBehaviorCanJoinAllSpaces = 1 << 0;

    public WindowFeatures SupportedFeatures => WindowFeatures.Topmost |
                                                WindowFeatures.ToolWindow |
                                                WindowFeatures.ClickThrough |
                                                WindowFeatures.WindowOpacity;

    public WindowFeatureApplyResult Apply(PlatformWindowHandle window, WindowFeatureRequest request)
    {
        if (!window.IsValid)
            return WindowFeatureApplyResult.Failed(request.Features, "The native window handle is not available.");

        if (!string.IsNullOrWhiteSpace(window.Descriptor)
            && !string.Equals(window.Descriptor, "NSWindow", StringComparison.OrdinalIgnoreCase))
        {
            return WindowFeatureApplyResult.Unsupported(request.Features,
                $"The '{window.Descriptor}' native handle is not an NSWindow.");
        }

        var requested = request.Features & SupportedFeatures;
        var unsupported = request.Features & ~SupportedFeatures;
        var applied = WindowFeatures.None;
        var failed = WindowFeatures.None;
        string? detail = null;

        if ((requested & WindowFeatures.Topmost) != 0)
        {
            if (TrySendBooleanOrInteger(window.Value, "setLevel:", request.Enabled ? NsFloatingWindowLevel : NsNormalWindowLevel,
                    out var failure))
            {
                applied |= WindowFeatures.Topmost;
            }
            else
            {
                failed |= WindowFeatures.Topmost;
                detail ??= failure;
            }
        }

        if ((requested & WindowFeatures.ClickThrough) != 0)
        {
            if (TrySendBooleanOrInteger(window.Value, "setIgnoresMouseEvents:", request.Enabled ? 1 : 0, out var failure))
            {
                applied |= WindowFeatures.ClickThrough;
            }
            else
            {
                failed |= WindowFeatures.ClickThrough;
                detail ??= failure;
            }
        }

        if ((requested & WindowFeatures.ToolWindow) != 0)
        {
            if (TrySetToolWindow(window.Value, request.Enabled, out var failure))
            {
                applied |= WindowFeatures.ToolWindow;
            }
            else
            {
                failed |= WindowFeatures.ToolWindow;
                detail ??= failure;
            }
        }

        if ((requested & WindowFeatures.WindowOpacity) != 0)
        {
            // 关闭该能力等价于恢复完全不透明（见 WindowFeatureRequest.Opacity）。
            var opacity = request.Enabled ? request.Opacity : 1.0;
            if (TrySetWindowOpacity(window.Value, opacity, out var failure))
            {
                applied |= WindowFeatures.WindowOpacity;
            }
            else
            {
                failed |= WindowFeatures.WindowOpacity;
                detail ??= failure;
            }
        }

        return WindowFeatureApplyResult.Partial(applied, unsupported, failed, detail);
    }

    /// <summary>
    ///     macOS 的红绿灯按钮在标题栏左侧（设置界面留了一块空白），右侧没有系统按钮。
    /// </summary>
    public double GetSystemCaptionButtonWidth(PlatformWindowHandle window) => 0;

    private static bool TrySetToolWindow(nint window, bool enabled, out string? failure)
    {
        try
        {
            var styleMaskSelector = SelRegisterName("styleMask");
            var collectionBehaviorSelector = SelRegisterName("collectionBehavior");
            if (styleMaskSelector == nint.Zero || collectionBehaviorSelector == nint.Zero)
            {
                failure = "Unable to resolve the Objective-C NSWindow style selectors.";
                return false;
            }

            var styleMask = ObjcMsgSendNint(window, styleMaskSelector);
            if (!TrySendBooleanOrInteger(
                    window,
                    "setStyleMask:",
                    enabled ? styleMask | NsUtilityWindowStyleMask : styleMask & ~NsUtilityWindowStyleMask,
                    out failure))
            {
                return false;
            }

            var collectionBehavior = ObjcMsgSendNint(window, collectionBehaviorSelector);
            return TrySendBooleanOrInteger(
                window,
                "setCollectionBehavior:",
                enabled
                    ? collectionBehavior | NsWindowCollectionBehaviorCanJoinAllSpaces
                    : collectionBehavior & ~NsWindowCollectionBehaviorCanJoinAllSpaces,
                out failure);
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }
    }

    /// <summary>
    ///     窗口整体不透明度：NSWindow 的 <c>alphaValue</c>（0–1，1 为不透明）。
    ///     下限与 Windows 那边取同一个量级，免得配置里手改成 0 之后窗口再也点不到。
    /// </summary>
    private static bool TrySetWindowOpacity(nint window, double opacity, out string? failure)
    {
        var clamped = double.IsFinite(opacity) ? Math.Clamp(opacity, 0.05, 1.0) : 1.0;
        try
        {
            var selector = SelRegisterName("setAlphaValue:");
            if (selector == nint.Zero)
            {
                failure = "Unable to resolve Objective-C selector 'setAlphaValue:'.";
                return false;
            }

            ObjcMsgSendDouble(window, selector, clamped);
            failure = null;
            return true;
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }
    }

    private static bool TrySendBooleanOrInteger(nint window, string selectorName, nint value, out string? failure)
    {
        try
        {
            var selector = SelRegisterName(selectorName);
            if (selector == nint.Zero)
            {
                failure = $"Unable to resolve Objective-C selector '{selectorName}'.";
                return false;
            }

            ObjcMsgSend(window, selector, value);
            failure = null;
            return true;
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint SelRegisterName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ObjcMsgSend(nint receiver, nint selector, nint argument);

    /// <summary><c>objc_msgSend</c> 的另一份签名：CGFloat 参数按 double 传（alphaValue 就是这种）。</summary>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ObjcMsgSendDouble(nint receiver, nint selector, double argument);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint ObjcMsgSendNint(nint receiver, nint selector);
}
