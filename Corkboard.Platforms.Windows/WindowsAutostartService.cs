using System.Runtime.Versioning;
using Corkboard.Platforms.Abstractions;
using Microsoft.Win32;

namespace Corkboard.Platforms.Windows;

/// <summary>
///     Windows 开机自启：在 <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>
///     下写一条本应用的启动项。用户级，不需要管理员权限。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAutostartService : IAutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Corkboard";

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool TrySetEnabled(bool enabled, out string? error)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                            ?? throw new InvalidOperationException("无法访问当前用户的启动项注册表键");

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                error = null;
                return true;
            }

            var command = PlatformLaunchCommand.Resolve();
            if (command is null)
                throw new InvalidOperationException("无法解析当前进程的可执行文件路径");

            key.SetValue(ValueName,
                PlatformLaunchCommand.ToWindowsCommandLine(command.Value.FileName, command.Value.Arguments),
                RegistryValueKind.String);
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            // 开启失败时清掉半成品，免得下次登录拿到一条坏命令。
            if (enabled)
                TryRemove();
            error = exception.Message;
            return false;
        }
    }

    private static void TryRemove()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // 原始失败原因对调用方更有价值，清理保持尽力而为。
        }
    }
}
