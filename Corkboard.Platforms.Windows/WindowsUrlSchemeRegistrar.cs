using System.Runtime.Versioning;
using Corkboard.Platforms.Abstractions;
using Microsoft.Win32;

namespace Corkboard.Platforms.Windows;

/// <summary>
///     Windows 自定义 URL 协议：在 <c>HKCU\Software\Classes\&lt;scheme&gt;</c> 下写一套协议处理程序项。
///     用户级，不需要管理员权限，也不改动系统级配置。
/// </summary>
/// <remarks>
///     <para>
///         注册出来的键形如：
///         <code>
///         HKCU\Software\Classes\corkboard                    (默认值) "URL:corkboard Protocol"
///                                                           "URL Protocol" = ""
///         HKCU\Software\Classes\corkboard\DefaultIcon        (默认值) "C:\…\Corkboard.exe",0
///         HKCU\Software\Classes\corkboard\shell\open\command (默认值) "C:\…\Corkboard.exe" "%1"
///         </code>
///         <c>URL Protocol</c> 那个空值不是装饰：少了它资源管理器只把这个键当成普通文件类型，
///         根本不会把 <c>corkboard://</c> 交过来。
///     </para>
///     <para>
///         命令行的末尾必须是字面量 <c>"%1"</c>——系统唤起时把整个 URL 替换进这个位置，
///         应用侧才拿得到要打开的目标。引用与转义一律走 <see cref="PlatformLaunchCommand" />，
///         不要在这里另写一套。
///     </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsUrlSchemeRegistrar : IUrlSchemeRegistrar
{
    private const string ClassesKeyPath = @"Software\Classes";

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool TrySetEnabled(string scheme, bool enabled, out string? error)
    {
        if (!IsValidScheme(scheme, out error))
            return false;

        var keyPath = ClassesKeyPath + "\\" + scheme;
        try
        {
            if (!enabled)
            {
                DeleteKeyTree(keyPath);
                error = null;
                return true;
            }

            var command = PlatformLaunchCommand.Resolve();
            if (command is null)
                throw new InvalidOperationException("无法解析当前进程的可执行文件路径");

            using var root = Registry.CurrentUser.CreateSubKey(keyPath, writable: true)
                             ?? throw new InvalidOperationException("无法访问当前用户的协议注册表键");

            // 默认值给资源管理器显示用的友好描述，通用写成 "URL:<scheme> Protocol"。
            root.SetValue(null, "URL:" + scheme + " Protocol", RegistryValueKind.String);
            root.SetValue("URL Protocol", string.Empty, RegistryValueKind.String);

            using (var icon = root.CreateSubKey("DefaultIcon", writable: true)
                              ?? throw new InvalidOperationException("无法创建协议图标的注册表键"))
            {
                // 图标写成 "<exe>",0：引号仍走 PlatformLaunchCommand 那套，不在这里另写一份。
                var iconTarget = PlatformLaunchCommand.ToWindowsCommandLine(command.Value.FileName, []) + ",0";
                icon.SetValue(null, iconTarget, RegistryValueKind.String);
            }

            using (var commandKey = root.CreateSubKey(@"shell\open\command", writable: true)
                                    ?? throw new InvalidOperationException("无法创建协议命令的注册表键"))
            {
                // 末尾的 "%1" 是系统替换 URL 的占位符，必须保留。
                commandKey.SetValue(null,
                    PlatformLaunchCommand.ToWindowsCommandLine(command.Value.FileName, ["%1"]),
                    RegistryValueKind.String);
            }

            error = null;
            return true;
        }
        catch (Exception exception)
        {
            // 开启失败时清掉半成品，免得系统把 URL 交给一条坏命令。
            if (enabled)
                TryRemove(keyPath);
            error = exception.Message;
            return false;
        }
    }

    /// <summary>
    ///     协议名只允许字母、数字与 <c>+</c> <c>-</c> <c>.</c>（RFC 3986 的 scheme 文法）。
    ///     名字会拼进注册表路径，非法输入直接拒绝，不要让它抛到调用方。
    /// </summary>
    private static bool IsValidScheme(string scheme, out string? error)
    {
        if (string.IsNullOrWhiteSpace(scheme))
        {
            error = "协议名不能为空。";
            return false;
        }

        if (!scheme.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '+' or '-' or '.'))
        {
            error = $"协议名 “{scheme}” 只能包含字母、数字与 + - . 这四个符号。";
            return false;
        }

        error = null;
        return true;
    }

    private static void DeleteKeyTree(string keyPath)
    {
        // throwOnMissingSubKey: false —— 本来就没注册过时，注销也该算成功。
        Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
    }

    private static void TryRemove(string keyPath)
    {
        try
        {
            DeleteKeyTree(keyPath);
        }
        catch
        {
            // 原始失败原因对调用方更有价值，清理保持尽力而为。
        }
    }
}
