using Avalonia.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Corkboard.Core.Services.Config;

namespace Corkboard.Services.Updates;

/// <summary>
///     自动更新调度：启动 8 秒后看一眼，之后每小时一次。是否真的检查由配置里的
///     <c>AutoUpdateMode</c>（0 关闭 / 1 仅通知 / 2 下载 / 3 下载并安装）与「距上次检查满一天」共同决定。
///     <para>
///         整段更新逻辑都切回 UI 线程执行：<see cref="UpdateCenterService" /> 是实现 <c>INotifyPropertyChanged</c>
///         的绑定源，状态从后台线程改会让设置页的绑定在不该刷新的线程上取值。
///     </para>
/// </summary>
public sealed class UpdateScheduler(
    MainConfigHandler configHandler,
    UpdateCenterService updateCenter,
    IUpdateNotificationService updateNotificationService,
    ILogger<UpdateScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = configHandler.Data.UpdateSettings;
                if (settings.AutoUpdateMode > 0
                    && (!settings.LastCheckTime.HasValue || DateTime.Now - settings.LastCheckTime.Value >= TimeSpan.FromDays(1)))
                {
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await updateCenter.CheckAsync();
                        if (!updateCenter.CanDownloadAndInstall)
                            return;
                        if (settings.AutoUpdateMode == 1)
                            await updateNotificationService.ShowUpdateAvailableAsync(updateCenter.AvailableVersion);
                        else if (settings.AutoUpdateMode == 2)
                            await updateCenter.DownloadAsync(installAfterDownload: false);
                        else
                        {
                            await updateCenter.DownloadAsync(installAfterDownload: false);
                            await updateCenter.ApplyDownloadedUpdateAsync();
                        }
                    }, DispatcherPriority.Normal).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "自动检查更新失败。");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(false);
        }
    }
}
