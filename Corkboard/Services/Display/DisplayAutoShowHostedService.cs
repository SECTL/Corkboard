using Avalonia.Threading;
using Corkboard.Core.Display;
using Corkboard.Core.Models.SubConfigs.Display;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Time;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.Display;

/// <summary>
///     看板模式的看门狗：每隔十几秒看一眼「现在该不该把全屏看板窗口自己弹出来 / 该不该收」。
///     <para>
///         规则全在 <see cref="DisplaySchedulePolicy" /> 里（纯函数、有单测，支持跨零点、多段、星期几），
///         这里只做三件事：读配置、按规则算、调界面上的开窗 / 关窗。判定与开窗都跑在 UI 线程上——
///         开窗会建窗口、挂订阅，在线程池线程上走这一趟会跟渲染打架。
///     </para>
///     <para>
///         为什么是轮询而不是「算好下一个时刻睡过去」：用户随时可能改时刻、开关，
///         定时器一旦睡死就得等它醒。十几秒一次的比较是纯内存判断，代价可以忽略。
///     </para>
///     <para>
///         时间源一律走 <see cref="NtpTimeService.NowLocal" />：屏幕上的时钟是校过时的，
///         自动进出要是按本机时间算，就会出现「钟显示 20:59、窗口却已经退了」。
///     </para>
///     <para>
///         ⚠️ <b>自动退出只关「自动打开的那一次」</b>：用户自己点开的窗口（托盘 / 设置页 / 链接）
///         不受 <c>auto_hide</c> 管，否则展示到一半被系统收走，谁都莫名其妙。
///     </para>
///     <para>
///         ⚠️ <b>用户手动关掉之后不许立刻再弹回来</b>：以前是先清 <c>_autoShown</c>，
///         下一拍 <c>ShouldEnter</c> 又为真，十五秒后窗口自己回来了（用户当场反馈过）。
///         现在关窗时记一笔「这一轮到点之前别再自动打开」（<c>App.SuppressDisplayUntil</c>）。
///     </para>
/// </summary>
public sealed class DisplayAutoShowHostedService(
    MainConfigHandler configHandler,
    NtpTimeService time,
    ILogger<DisplayAutoShowHostedService> logger) : BackgroundService
{
    /// <summary>自动进出的时刻是分钟级的，十几秒看一次足够，也不至于占着 UI 线程。</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     当前这扇窗是不是<b>我们</b>弹出来的。为真时才轮到 <c>auto_hide</c> 去收它；
    ///     用户自己开的窗口一直是 <see langword="false" />，到点也不动。
    /// </summary>
    private bool _autoShown;

    /// <summary>
    ///     挪到线程池上跑。<c>Host.StartAsync</c> 会同步执行 <c>ExecuteAsync</c> 到第一个未完成的 await，
    ///     而这里第一件事就是往 UI 线程排队——不能把启动线程卡在那儿等界面转起来。
    /// </summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => RunLoopAsync(stoppingToken), CancellationToken.None);

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        logger.LogDebug("看板模式自动进出服务已启动");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await CheckOnceAsync(stoppingToken).ConfigureAwait(false);

                try
                {
                    await Task.Delay(CheckInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            logger.LogDebug("看板模式自动进出服务已停止");
        }
    }

    private async Task CheckOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            // 带上取消令牌：退出时排队中的这一笔不再等界面，直接放掉。
            await Dispatcher.UIThread.InvokeAsync(Tick, DispatcherPriority.Background, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // 自动进出是后台的顺手活，失败只记日志，绝不弹窗打断正在看板的人。
            logger.LogError(exception, "看板模式自动进出检查失败");
        }
    }

    /// <summary>一拍判定：先看手上这一轮要不要收，再看该不该弹。</summary>
    private void Tick()
    {
        var settings = configHandler.Data.Display;
        var now = time.NowLocal;
        var slots = ResolveSlots(settings);

        // 考试看板优先：两块都是全屏窗（考试到点自动放映、看板模式到点自动进出），
        // 撞在一起时让考试看板独占屏幕——它讲的是「马上要考什么」，看板模式只是背景信息。
        // ⚠️ 这里必须**只清账、不记抑制**：不记抑制 = 考完下一拍自己回来；
        // 记了抑制（走下面「被用户自己关掉」那条）就等于今天不再打开，用户没关过却再也不出现。
        if (App.IsExamBoardWindowOpen)
        {
            if (_autoShown)
            {
                _autoShown = false;
                App.IsDisplayAutoShown = false;
                logger.LogDebug("考试看板正在放映，看板模式先让位");
            }

            if (App.IsDisplayBoardWindowOpen)
                App.CloseDisplayBoardWindow();

            return;
        }

        // 窗被用户自己关掉了（长按退出 / 关窗 / 托盘）：这一轮自动放映到此为止。
        if (_autoShown && !App.IsDisplayBoardWindowOpen)
        {
            _autoShown = false;
            App.IsDisplayAutoShown = false;

            // 记到这一轮结束：下一拍 ShouldEnter 还是同一个时段，不记就十五秒后自己回来。
            var end = DisplaySchedulePolicy.ResolveActiveEnd(
                          DisplaySchedulePolicy.FindActiveSlot(slots, now), now)
                      ?? now.AddMinutes(15);
            App.SuppressDisplayUntil(end);
            logger.LogDebug("看板模式被手动关掉，{End:HH:mm} 之前不再自动打开", end);
            return;
        }

        // 正在自动放映：这一刻只关心什么时候收。
        if (_autoShown)
        {
            if (!settings.AutoHide)
                return;

            // 「延长 30 分钟」：到点之后再多放一段，延长结束才真的收。
            if (App.DisplayExtendedUntil is { } extended)
            {
                if (now < extended)
                    return;

                logger.LogDebug("看板模式延长结束，自动退出");
                CloseAutoShown();
                return;
            }

            if (DisplaySchedulePolicy.ShouldExit(true, now, slots))
            {
                logger.LogDebug("看板模式到点自动退出");
                CloseAutoShown();
            }

            return;
        }

        if (!settings.AutoShow || App.IsDisplayBoardWindowOpen)
            return;

        // 上一轮被手动关掉 / 用户点了「今天不再自动打开」。
        if (App.IsDisplaySuppressed(now))
            return;

        if (!DisplaySchedulePolicy.ShouldEnter(false, now, slots))
            return;

        logger.LogDebug("看板模式到点自动进入");
        App.ShowDisplayBoardWindow();
        _autoShown = true;
        App.IsDisplayAutoShown = true;
    }

    private void CloseAutoShown()
    {
        App.CloseDisplayBoardWindow();
        _autoShown = false;
        App.IsDisplayAutoShown = false;
    }

    /// <summary>
    ///     真正参与判定的时段。带 <c>?from=&amp;to=</c> 打开的那一次先改第一段的时刻，
    ///     而且改的是副本——用户存下来的配置一个字都不动。
    /// </summary>
    private static IReadOnlyList<DisplaySlot> ResolveSlots(DisplaySettingsConfig settings)
    {
        var slots = settings.ActiveSlots;
        if (App.DisplayScheduleOverride is not { } window || slots.Count == 0)
            return slots;

        var copy = new List<DisplaySlot>(slots.Count);
        foreach (var slot in slots)
            copy.Add(ReferenceEquals(slot, slots[0]) ? slot.Clone() : slot);

        var (enterHour, enterMinute) = DisplaySchedulePolicy.Split(window.Enter);
        var (exitHour, exitMinute) = DisplaySchedulePolicy.Split(window.Exit);
        copy[0].EnterHour = enterHour;
        copy[0].EnterMinute = enterMinute;
        copy[0].ExitHour = exitHour;
        copy[0].ExitMinute = exitMinute;
        return copy;
    }
}
