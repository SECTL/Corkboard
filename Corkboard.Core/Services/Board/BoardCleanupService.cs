using Microsoft.Extensions.Logging;
using Corkboard.Core.Abstraction.Services;
using Corkboard.Core.Models.Board;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Services.Config;

namespace Corkboard.Core.Services.Board;

/// <summary>一次自动清理的结果，给设置页显示与单测断言用。</summary>
/// <param name="RanAt">这一次判定的时刻。</param>
/// <param name="Cleaned">这次新标成「已清理」的条数（作业本身没被搬走，也没被删）。</param>
public sealed record BoardCleanupResult(DateTimeOffset RanAt, int Cleaned)
{
    /// <summary>什么都没清。</summary>
    public static BoardCleanupResult Empty(DateTimeOffset ranAt) => new(ranAt, 0);
}

/// <summary>
///     自动清理的编排：判定 → 打「已清理」标记 → 落盘。
///     <para>
///         动手的时机由外面驱动（见应用层的 <c>BoardCleanupHostedService</c>），
///         这里只负责「现在按当前设置清一次」，因此可以脱离计时器单测。
///     </para>
///     <para>
///         只有一个该动手的时刻：<b>清理时刻</b>（按清理频率，见 <see cref="IsDue" />）。
///         它只看「最近一个该动手的时刻过去了没有」，所以应用当时没开着也不会漏掉——
///         下次运行（含刚启动那一刻）就补上，不需要应用一直开着。
///     </para>
///     <para>
///         谁该被清只看一件事：<b>截止日期过了没有</b>（见 <see cref="BoardExpiryPolicy" />）。
///         没填截止日期的作业永远不会被自动清掉。
///     </para>
///     <para>
///         清理<b>不动作业数据</b>：只给作业打上 <see cref="BoardNote.CleanedAt" />，作业仍然留在
///         它原本那个 <c>&lt;年&gt;/&lt;月&gt;/&lt;日&gt;/notes.json</c> 里，只是主页面不再显示它。
///         没有第二个目录，也就没有「搬走了但没删掉」这种两步事情——最坏情况是这一次没清干净，下次再来。
///     </para>
/// </summary>
public sealed class BoardCleanupService
{
    private readonly MainConfigHandler _configHandler;
    private readonly IBoardService _boardService;
    private readonly ILogger<BoardCleanupService> _logger;

    public BoardCleanupService(
        MainConfigHandler configHandler,
        IBoardService boardService,
        ILogger<BoardCleanupService> logger)
    {
        _configHandler = configHandler;
        _boardService = boardService;
        _logger = logger;
    }

    /// <summary>上次动手的时刻。<b>只活在这一次进程里</b>，不落盘（见 <see cref="BoardCleanupSchedule" />）。</summary>
    public DateTimeOffset? LastRun { get; private set; }

    /// <summary>最近一次清理的结果，设置页拿它显示「上次清了什么」。</summary>
    public BoardCleanupResult? LastResult { get; private set; }

    private BoardSettingsConfig Settings => _configHandler.Data.BoardSettings;

    /// <summary>「清理时刻」已经过去、且这一轮还没动过手。关闭自动清理时永远为 <c>false</c>。</summary>
    public bool IsDue(DateTimeOffset now)
    {
        var settings = Settings;
        if (!settings.CleanupEnabled)
            return false;

        return BoardCleanupSchedule.ShouldRun(
            settings.CleanupFrequency,
            settings.CleanupWeekday,
            settings.CleanupHour,
            settings.CleanupMinute,
            LastRun,
            now);
    }

    /// <summary>
    ///     按当前设置清一次（不看时刻）。用户点「立即清理」和计时器到点都走这里。
    ///     关闭自动清理不影响手动清：手动这一下本来就是用户明确要的。
    /// </summary>
    /// <param name="now">这一次判定的时刻。</param>
    public BoardCleanupResult Run(DateTimeOffset now)
    {
        LastRun = now;

        var today = DateOnly.FromDateTime(now.LocalDateTime);

        var expired = new List<Guid>();
        foreach (var note in _boardService.Notes)
        {
            // 已经清过的不用再看：它还在内存里（数据不能丢），只是已经不在板子上了。
            if (note.CleanedAt is not null)
                continue;

            // 只看「过了截止日期」这一条：没填截止日期的作业永远不清。
            if (!BoardExpiryPolicy.IsExpired(note, today))
                continue;

            expired.Add(note.Id);
        }

        if (expired.Count == 0)
        {
            _logger.LogDebug("Board cleanup found nothing to clean");
            return BoardCleanupResult.Empty(now);
        }

        var cleaned = _boardService.CleanMany(expired, now);
        var result = new BoardCleanupResult(now, cleaned);
        LastResult = result;

        _logger.LogInformation("Board cleanup marked {Cleaned} notes as cleaned", cleaned);

        return result;
    }
}
