using Avalonia.Threading;
using Corkboard.Core.Exam;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Time;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.Exam;

/// <summary>
///     自动放映的看门狗：每隔十几秒看一眼「现在该不该把全屏考试看板弹出来 / 该不该收」。
///     <para>
///         规则全在 <see cref="ExamAutoShowPolicy" /> 里（纯函数、有单测），这里只做三件事：
///         读配置、按规则算、调界面上的开窗 / 关窗。判定与开窗都跑在 UI 线程上——
///         开窗会建窗口、挂订阅，在线程池线程上走这一趟会跟渲染打架。
///     </para>
///     <para>
///         为什么是轮询而不是「算好下一个时刻睡过去」：用户随时可能改提前量、改延迟、给看板
///         加一场考试，定时器一旦睡死就得等它醒。十几秒一次的比较是纯内存判断，代价可以忽略。
///     </para>
///     <para>
///         时间源一律走 <see cref="NtpTimeService.NowLocal" />：看板上的时钟是校过时的，
///         自动放映要是按本机时间算，就会跟屏幕上显示的倒计时对不上。
///     </para>
/// </summary>
public sealed class ExamAutoShowHostedService(
    IExamService examService,
    MainConfigHandler configHandler,
    NtpTimeService time,
    ILogger<ExamAutoShowHostedService> logger) : BackgroundService
{
    /// <summary>自动放映的时刻是分钟级的，十几秒看一次足够，也不至于占着 UI 线程。</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     这一轮已经为哪些考试弹过。同一场只弹一次：用户长按退出之后不该再被弹回来，
    ///     考试中途把窗口关掉也不该每一拍都重建一个。
    /// </summary>
    private readonly HashSet<Guid> _shownExamIds = [];

    /// <summary>自动弹出来的那一块看板；<see langword="null" /> 表示当前没有自动放映中的看板。</summary>
    private Guid? _sessionBoardId;

    /// <summary>
    ///     上一次看到的「当前看板」。用来认出**用户自己**换了看板
    ///     （设置页点「设为当前」、打开编辑窗口），而不是我们切的。
    /// </summary>
    private Guid? _lastActiveBoardId;

    /// <summary>
    ///     用户手动换过看板之后，自动切换要「让位」到什么时候（下一场考试开考）。
    ///     让位期间不去覆盖用户的选择——他刚点的那块，十几秒后就被改回去，等于那个按钮没用了。
    ///     <para>
    ///         <see langword="null" /> 表示当前没有让位；一场考试都排不出来时置成
    ///         <see cref="DateTime.MaxValue" />（一直让位下去）。
    ///     </para>
    /// </summary>
    private DateTime? _manualOverrideUntil;

    /// <summary>
    ///     挪到线程池上跑。<c>Host.StartAsync</c> 会同步执行 <c>ExecuteAsync</c> 到第一个未完成的 await，
    ///     而这里第一件事就是往 UI 线程排队——不能把启动线程卡在那儿等界面转起来。
    /// </summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => RunLoopAsync(stoppingToken), CancellationToken.None);

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        logger.LogDebug("考试看板自动放映服务已启动");

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
            logger.LogDebug("考试看板自动放映服务已停止");
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
            // 自动放映是后台的顺手活，失败只记日志，绝不弹窗打断正在考试的人。
            logger.LogError(exception, "考试看板自动放映检查失败");
        }
    }

    /// <summary>一拍判定：先看手上这一轮要不要收，再看有没有该弹的。</summary>
    private void Tick()
    {
        var settings = configHandler.Data.ExamAutoShow;
        var now = time.NowLocal;

        // 窗口被用户自己关掉了（长按退出）：这一轮自动放映到此为止。
        // ⚠️ 已经弹过的那几场仍留在 _shownExamIds 里——长按退出是「我看完了」，不是「等下再弹给我」。
        if (_sessionBoardId is not null && !App.IsExamBoardWindowOpen)
            EndSession("窗口已被用户关闭", clearShown: false);

        // 正在自动放映：这一刻只关心什么时候收。
        if (_sessionBoardId is { } sessionId)
        {
            if (examService.Boards.FirstOrDefault(board => board.Id == sessionId) is not { } session)
            {
                EndSession("看板已被删除", clearShown: false);
                return;
            }

            if (ExamAutoShowPolicy.IsFinished(session.Exams, now, settings.CloseDelayMinutes))
            {
                App.CloseExamBoardWindow();
                // 这一轮整场考试都过去了：把弹过的记录一起清掉，下一季从零开始。
                EndSession("最后一场已考完", clearShown: true);
            }

            return;
        }

        // 当前看板自己跟着今天考什么走（手动放看板也管用），这一步不看 AutoShowEnabled。
        AutoSwitchBoard(now, settings);

        if (FindPending(now, settings) is not { } pending)
            return;

        var (targetBoard, targetExam) = pending;

        // 用户已经把看板自己开着了：算这一场「已经放过了」，不抢窗口、也不用等他关掉之后再弹一次。
        if (App.IsExamBoardWindowOpen)
        {
            _shownExamIds.Add(targetExam.Id);
            return;
        }

        // 全屏看板显示的是「当前看板」，所以先把参与放映的那块切过去（换看板会重读考试集合）。
        if (!ReferenceEquals(examService.ActiveBoard, targetBoard))
            SwitchTo(targetBoard.Id);

        _shownExamIds.Add(targetExam.Id);
        _sessionBoardId = targetBoard.Id;
        App.ShowExamBoardWindow();

        logger.LogInformation("自动放映开始：{Board} · {Exam} {Time}", targetBoard.ResolveTitle(), targetExam.Subject,
            ExamTimeFormatter.FormatTimeRange(targetExam));
    }

    /// <summary>
    ///     当前看板跟着「今天考什么」自己换。换的是 <see cref="IExamService.ActiveBoard" />，不弹窗——
    ///     弹不弹仍然只由每块看板自己的 <see cref="ExamBoardDocument.AutoShowEnabled" /> 决定。
    /// </summary>
    private void AutoSwitchBoard(DateTime now, ExamAutoShowSettingsConfig settings)
    {
        var activeId = examService.ActiveBoard?.Id;

        // 认一下这次变化是不是用户自己点的（设置页「设为当前」、打开编辑窗口）。
        // 我们自己切的都走 SwitchTo()，那里会把 _lastActiveBoardId 一起更新，所以不会误判。
        if (_lastActiveBoardId is { } previousId && activeId != previousId)
        {
            // 让位到下一场开考：用户刚挑的那块不能十几秒后就被改回去。
            // 一场考试都排不出来（没有下一场）就一直让位，等他加了新考试再说。
            _manualOverrideUntil = ExamBoardSwitchPolicy.FindNextStart(examService.Boards, now) ?? DateTime.MaxValue;
            logger.LogDebug("用户手动换了看板，自动切换让位到 {Until}", _manualOverrideUntil);
        }

        _lastActiveBoardId = activeId;

        if (!settings.AutoSwitchBoard)
            return;

        if (_manualOverrideUntil is { } until)
        {
            // 「当时一场考试都没排」的让位是一直让位，但用户后来加了考试就得认账：
            // 每拍重新问一次下一场什么时候，问到了就按它到期。
            if (until == DateTime.MaxValue)
            {
                until = ExamBoardSwitchPolicy.FindNextStart(examService.Boards, now) ?? DateTime.MaxValue;
                _manualOverrideUntil = until;
            }

            if (now < until)
                return;

            // 让位到期（下一场已经开考）：交还给自动切换。
            _manualOverrideUntil = null;
        }

        if (activeId is null)
            return;

        if (ExamBoardSwitchPolicy.FindTarget(examService.Boards, now) is not { } target || target.Id == activeId)
            return;

        SwitchTo(target.Id);
        logger.LogInformation("当前看板自动切换到：{Board}", target.ResolveTitle());
    }

    /// <summary>切当前看板，并把「上一次看到的当前看板」一起更新，免得被当成用户手动切换。</summary>
    private void SwitchTo(Guid boardId)
    {
        examService.SelectBoard(boardId);
        _lastActiveBoardId = boardId;
    }

    /// <summary>
    ///     在「参了加自动放映的看板」里找出此刻该弹的那一场。多块看板同时满足时取开考最早的，
    ///     这样两块板子都到点也不会按列表顺序乱弹。
    /// </summary>
    private (ExamBoardDocument Board, ExamEntry Exam)? FindPending(DateTime now, ExamAutoShowSettingsConfig settings)
    {
        (ExamBoardDocument Board, ExamEntry Exam)? best = null;

        foreach (var board in examService.Boards)
        {
            if (board is null || !board.AutoShowEnabled)
                continue;

            var trigger = ExamAutoShowPolicy.FindTrigger(
                board.Exams, now, settings.LeadMinutes, settings.AlsoShowAfterStart, _shownExamIds);

            if (trigger is null)
                continue;

            if (best is null || trigger.StartAt < best.Value.Exam.StartAt)
                best = (board, trigger);
        }

        return best;
    }

    /// <summary>收掉这一轮自动放映。</summary>
    /// <param name="reason">写日志用。</param>
    /// <param name="clearShown">
    ///     整场考试放完了才清「弹过哪些」；用户自己关窗时不清——那一场已经给他看过了。
    /// </param>
    private void EndSession(string reason, bool clearShown)
    {
        if (_sessionBoardId is null)
            return;

        logger.LogInformation("自动放映结束：{Reason}", reason);
        _sessionBoardId = null;

        if (clearShown)
            _shownExamIds.Clear();
    }
}
