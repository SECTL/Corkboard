using Microsoft.Extensions.Logging;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Board;
using Corkboard.Core.Notes;

namespace Corkboard.Core.Homework;

/// <summary>一次自动清理的结果，给设置页显示与单测断言用。</summary>
/// <param name="RanAt">这一次判定的时刻。</param>
/// <param name="Cleaned">这次新标成「已清理」的条数（作业本身没被搬走，也没被删）。</param>
public sealed record HomeworkCleanupResult(DateTimeOffset RanAt, int Cleaned)
{
    /// <summary>什么都没清。</summary>
    public static HomeworkCleanupResult Empty(DateTimeOffset ranAt) => new(ranAt, 0);
}

/// <summary>
///     自动清理的编排：判定 → 打「已清理」标记 → 落盘。
///     <para>
///         动手的时机由外面驱动（见应用层的 <c>HomeworkCleanupHostedService</c>），
///         这里只负责「现在按当前设置清一次」，因此可以脱离计时器单测。
///     </para>
///     <para>
///         只有一个该动手的时刻：<b>今天的清理时刻</b>（按清理频率，见 <see cref="IsDue" />）。
///         <b>没到今天的那个时刻之前一律不动手</b>——早先按「最近一个过去的时刻」算，
///         凌晨启动时会把昨天那个时刻当补清算掉，用户看到的就是「没到点就清理了」。
///         应用没开着而错过今天的时刻的，下次启动只要已经过了今天的时刻就补上。
///     </para>
///     <para>
///         谁该被清只看一件事：<b>截止日期过了没有</b>（见 <see cref="HomeworkExpiryPolicy" />）——
///         截止当天不算，第二天才清。没填截止日期的作业永远不会被自动清掉。
///     </para>
///     <para>
///         清理<b>不动作业数据</b>：只给作业打上 <see cref="Note.CleanedAt" />，作业仍然留在
///         它原本那个 <c>&lt;年&gt;/&lt;月&gt;/&lt;日&gt;/&lt;Id&gt;.cbnote</c> 里，只是主页面不再显示它。
///         没有第二个目录，也就没有「搬走了但没删掉」这种两步事情——最坏情况是这一次没清干净，下次再来。
///     </para>
/// </summary>
public sealed class HomeworkCleanupService
{
    private readonly MainConfigHandler _configHandler;
    private readonly IBoardService _boardService;
    private readonly ILogger<HomeworkCleanupService> _logger;

    public HomeworkCleanupService(
        MainConfigHandler configHandler,
        IBoardService boardService,
        ILogger<HomeworkCleanupService> logger)
    {
        _configHandler = configHandler;
        _boardService = boardService;
        _logger = logger;
    }

    /// <summary>上次动手的时刻。<b>只活在这一次进程里</b>，不落盘（见 <see cref="HomeworkCleanupSchedule" />）。</summary>
    public DateTimeOffset? LastRun { get; private set; }

    /// <summary>最近一次清理的结果，设置页拿它显示「上次清了什么」。</summary>
    public HomeworkCleanupResult? LastResult { get; private set; }

    private BoardSettingsConfig Settings => _configHandler.Data.BoardSettings;

    /// <summary>「清理时刻」已经过去、且这一轮还没动过手。关闭自动清理时永远为 <c>false</c>。</summary>
    public bool IsDue(DateTimeOffset now)
    {
        var settings = Settings;
        if (!settings.CleanupEnabled)
            return false;

        return HomeworkCleanupSchedule.ShouldRun(
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
    public HomeworkCleanupResult Run(DateTimeOffset now)
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
            if (!HomeworkExpiryPolicy.IsExpired(note, today))
                continue;

            expired.Add(note.Id);
        }

        if (expired.Count == 0)
        {
            _logger.LogDebug("Board cleanup found nothing to clean");
            return HomeworkCleanupResult.Empty(now);
        }

        var cleaned = _boardService.CleanMany(expired, now);
        var result = new HomeworkCleanupResult(now, cleaned);
        LastResult = result;

        _logger.LogInformation("Board cleanup marked {Cleaned} notes as cleaned", cleaned);

        return result;
    }
}
