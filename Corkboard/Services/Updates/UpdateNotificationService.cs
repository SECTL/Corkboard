using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Corkboard.Core.Updates;

namespace Corkboard.Services.Updates;

/// <summary>系统通知出入口：后台调度发现新版本时用（自动更新模式 = 仅通知）。</summary>
public interface IUpdateNotificationService
{
    Task ShowUpdateAvailableAsync(string version);
}

/// <summary>
///     默认实现：走各平台自己的系统通知（Windows Toast / macOS osascript / Linux notify-send）。
///     通知只是尽力而为——弹不出来也只记日志，不能把更新流程带崩。
/// </summary>
public sealed class UpdateNotificationService(ILogger<UpdateNotificationService> logger) : IUpdateNotificationService
{
    private const string AppName = "Corkboard";

    public Task ShowUpdateAvailableAsync(string version)
    {
        try
        {
            var title = UpdateText.Get("Notification_Title");
            var message = string.Format(CultureInfo.CurrentUICulture, UpdateText.Get("Notification_Message"), version);
            var startInfo = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true };
            if (OperatingSystem.IsWindows())
            {
                startInfo.FileName = "powershell";
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-Command");
                startInfo.ArgumentList.Add(
                    "[Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime] | Out-Null; " +
                    "$xml = New-Object Windows.Data.Xml.Dom.XmlDocument; " +
                    $"$xml.LoadXml('<toast><visual><binding template=\"ToastGeneric\"><text>{EscapeXml(title)}</text><text>{EscapeXml(message)}</text></binding></visual></toast>'); " +
                    $"[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{AppName}').Show((New-Object Windows.UI.Notifications.ToastNotification $xml))");
            }
            else if (OperatingSystem.IsMacOS())
            {
                startInfo.FileName = "osascript";
                startInfo.ArgumentList.Add("-e");
                startInfo.ArgumentList.Add(
                    $"display notification \"{EscapeAppleScript(message)}\" with title \"{EscapeAppleScript(title)}\"");
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
            {
                startInfo.FileName = "notify-send";
                startInfo.ArgumentList.Add($"--app-name={AppName}");
                startInfo.ArgumentList.Add(title);
                startInfo.ArgumentList.Add(message);
            }
            else
            {
                return Task.CompletedTask;
            }

            Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            logger.LogInformation(exception, "无法显示系统更新通知。");
        }

        return Task.CompletedTask;
    }

    /// <summary>Toast 的 XML 是拼进 PowerShell 单引号字符串里的，先转义 XML 实体。</summary>
    private static string EscapeXml(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&apos;", StringComparison.Ordinal);

    private static string EscapeAppleScript(string text) => text
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);
}
