using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.Desktop;

/// <summary>
///     用系统默认程序打开外部地址（关于页的仓库 / 组织 / 作者链接都走它）。
/// </summary>
public interface IExternalLauncher
{
    /// <summary>在系统默认浏览器里打开网址。无法打开时返回 false。</summary>
    bool TryOpenUri(string uri);
}

/// <summary>
///     默认实现：交给操作系统自己的启动器（shell 打开默认浏览器）。
/// </summary>
/// <remarks>
///     与 <c>SystemBrowserAuthBrowser</c> 同一个路子：自包含实现，避免为了打开一个网址再引一层
///     桌面启动器抽象；需要自定义（例如内嵌 WebView）时替换 <see cref="IExternalLauncher" /> 的注册即可。
/// </remarks>
public sealed class SystemExternalLauncher(ILogger<SystemExternalLauncher> logger) : IExternalLauncher
{
    public bool TryOpenUri(string uri)
    {
        // 只放行绝对网址：这里是「打开链接」，不是「运行任意路径」。
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var target))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo(target.AbsoluteUri)
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
            logger.LogWarning(exception, "打开链接失败：{Uri}", target.AbsoluteUri);
            return false;
        }
    }
}
