using System.ComponentModel;
using Corkboard.Core.Board.History;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Services.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.Board;

/// <summary>
///     历史存储的「设置同步 + 定期清理」后台服务。
///     <para>
///         历史存储（<see cref="INoteHistoryStore" />）自己不知道设置在哪，三个容量参数要有人写给它：
///         启动时先按配置写一遍，之后配置一改（设置页提交、云端恢复设置）就再写一遍。
///         只认「保存粒度 / 保留天数 / 容量上限」这三项，其它配置变化一概不管。
///     </para>
///     <para>
///         清理每小时跑一次，启动时也先跑一次（补上应用没开着时该删的旧日志）。清理只碰
///         <c>data/history/board/</c>，不会动作业数据，失败只记日志、不打断应用。
///     </para>
/// </summary>
public sealed class BoardHistoryHostedService(
    MainConfigHandler configHandler,
    INoteHistoryStore historyStore,
    ILogger<BoardHistoryHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(1);

    private BoardSettingsConfig? _settings;

    /// <summary>
    ///     挪到线程池上跑：<c>Host.StartAsync</c> 会同步执行 <c>ExecuteAsync</c> 到第一个未完成的 await，
    ///     而启动时要清理一次历史（扫目录、删文件），不能把启动线程卡在那儿。
    /// </summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => RunLoopAsync(stoppingToken), CancellationToken.None);

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        logger.LogDebug("作业历史服务已启动");

        Attach();
        PruneOnce();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(PruneInterval, stoppingToken).ConfigureAwait(false);
                PruneOnce();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            Detach();
            logger.LogDebug("作业历史服务已停止");
        }
    }

    /// <summary>订阅配置：配置对象被整个换掉（<see cref="MainConfigHandler.Reloaded" />）时要重新挂。</summary>
    private void Attach()
    {
        configHandler.Reloaded += OnConfigReloaded;
        SubscribeSettings();
    }

    private void Detach()
    {
        configHandler.Reloaded -= OnConfigReloaded;

        if (_settings is not null)
            _settings.PropertyChanged -= OnSettingsChanged;
        _settings = null;
    }

    private void OnConfigReloaded(object? sender, EventArgs e)
    {
        // 从云端恢复设置会把 Data 整个换掉，旧的子配置对象已经不归这份配置管了。
        SubscribeSettings();
        Apply();
    }

    /// <summary>把订阅挂到当前的 <see cref="BoardSettingsConfig" /> 上，挂了就顺手同步一次。</summary>
    private void SubscribeSettings()
    {
        var settings = configHandler.Data.BoardSettings;
        if (ReferenceEquals(_settings, settings))
            return;

        if (_settings is not null)
            _settings.PropertyChanged -= OnSettingsChanged;

        _settings = settings;
        settings.PropertyChanged += OnSettingsChanged;

        Apply();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BoardSettingsConfig.HistoryCoalesceMinutes)
            or nameof(BoardSettingsConfig.HistoryRetentionDays)
            or nameof(BoardSettingsConfig.HistoryMaxSizeMb))
        {
            Apply();
        }
    }

    /// <summary>把三项设置夹一次再写进存储层；存储层的 setter 自己也会夹，这里只是让日志少一次惊讶。</summary>
    private void Apply()
    {
        try
        {
            var settings = configHandler.Data.BoardSettings;

            historyStore.CoalesceWindow = BoardHistoryPolicy.ClampCoalesceWindow(settings.HistoryCoalesceMinutes);
            historyStore.RetentionDays = BoardHistoryPolicy.ClampRetentionDays(settings.HistoryRetentionDays);
            historyStore.MaxBytes = BoardHistoryPolicy.ToBytes(settings.HistoryMaxSizeMb);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "同步历史存储的容量设置失败");
        }
    }

    private void PruneOnce()
    {
        try
        {
            var removed = historyStore.Prune();
            if (removed > 0)
                logger.LogInformation("清理了 {Count} 天的旧历史日志", removed);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "清理历史日志失败");
        }
    }
}
