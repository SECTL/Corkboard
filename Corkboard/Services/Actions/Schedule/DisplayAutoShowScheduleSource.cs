using Corkboard.Core.Actions;
using Corkboard.Core.Actions.Schedule;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Time;

namespace Corkboard.Services.Actions.Schedule;

/// <summary>
///     看板模式自动进出的系统日程源：把设置里配的时段投影成日程条目。
///     <para>
///         跨零点的时段由 <see cref="DisplayScheduleProjection" /> 在 00:00 拆成两段，这里只管叠
///         「今天不执行」的抑制；「自动显示」总开关关掉时条目照旧出现，只是 <see cref="ScheduleItem.IsEnabled" />
///         为假（窗口显示成灰色「未启用」）。
///     </para>
/// </summary>
public sealed class DisplayAutoShowScheduleSource : IScheduleSource
{
    private readonly MainConfigHandler _configHandler;
    private readonly ActionStateStore _state;
    private readonly NtpTimeService _time;

    /// <summary>构造日程源。</summary>
    public DisplayAutoShowScheduleSource(
        MainConfigHandler configHandler,
        ActionStateStore state,
        NtpTimeService time)
    {
        _configHandler = configHandler;
        _state = state;
        _time = time;
    }

    /// <inheritdoc />
    public string Id => DisplayScheduleProjection.SourceId;

    /// <inheritdoc />
    public string DisplayName => ActionResources.Get("Action_System_DisplayAutoShow_Title", "看板模式自动进出");

    /// <inheritdoc />
    public bool CanPause => true;

    /// <inheritdoc />
    public IReadOnlyList<ScheduleItem> GetItems(DateTime fromInclusive, DateTime toExclusive)
    {
        var items = DisplayScheduleProjection.Project(
            _configHandler.Data.Display,
            fromInclusive,
            toExclusive);

        if (items.Count == 0)
            return items;

        var today = DateOnly.FromDateTime(_time.NowLocal);
        return items
            .Select(item => item with { IsSuppressed = IsSuppressed(item, today) })
            .ToArray();
    }

    /// <summary>这个条目的「今天不执行」是不是生效：开始那天被停，或跨夜段把今天盖住且今天被停。</summary>
    private bool IsSuppressed(ScheduleItem item, DateOnly today)
    {
        if (_state.IsSuppressed(DisplayScheduleProjection.SourceId, DateOnly.FromDateTime(item.Start)))
            return true;

        if (item.End is not { } end)
            return false;

        var todayStart = today.ToDateTime(TimeOnly.MinValue);
        var spansToday = item.Start <= todayStart && end > todayStart;
        return spansToday && _state.IsSuppressed(DisplayScheduleProjection.SourceId, today);
    }
}
