using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.Auth;

/// <summary>
///     在系统浏览器里打开 SECTL 授权页。桌面流程随后等待 loopback 回调。
/// </summary>
public interface IAuthBrowser
{
    /// <summary>打开授权页。无法打开任何浏览器时返回 false。</summary>
    bool TryOpenAuthorization(string authorizeUrl);
}

/// <summary>
///     默认授权浏览器：交给操作系统自己的启动器（shell 打开默认浏览器）。
/// </summary>
/// <remarks>
///     自包含实现，避免为了打开一个 URL 再引入一层桌面启动器抽象；
///     需要自定义（例如内嵌 WebView 捕获回调）时替换 <see cref="IAuthBrowser" /> 的注册即可。
/// </remarks>
public sealed class SystemBrowserAuthBrowser(ILogger<SystemBrowserAuthBrowser> logger) : IAuthBrowser
{
    public bool TryOpenAuthorization(string authorizeUrl)
    {
        if (string.IsNullOrWhiteSpace(authorizeUrl))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo(authorizeUrl)
            {
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                              or Win32Exception
                                              or PlatformNotSupportedException
                                              or FileNotFoundException)
        {
            logger.LogWarning(exception, "打开 SECTL 授权页失败。");
            return false;
        }
    }
}
