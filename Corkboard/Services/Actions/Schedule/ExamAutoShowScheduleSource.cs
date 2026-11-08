using Corkboard.Core.Actions;
using Corkboard.Core.Actions.Schedule;
using Corkboard.Core.Exam;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Time;

namespace Corkboard.Services.Actions.Schedule;

/// <summary>
///     考试自动放映的系统日程源：把当前看板的考试排期投影成一条放映区间。
///     <para>
///         放映边界不在这里算，一律复用 <see cref="ExamScheduleProjection" />（内部走
///         <see cref="ExamAutoShowPolicy" />），保证「界面上看到的」与「看门狗真的弹的」是同一个窗口。
///     </para>
///     <para>
///         看板自己的「参加自动放映」开关（<see cref="ExamBoardDocument.AutoShowEnabled" />）叠在
///         <see cref="ScheduleItem.IsEnabled" /> 上；「今天不执行」的抑制也从 state.json 读出来叠上去，
///         窗口据此把条目画成灰色。
///     </para>
/// </summary>
public sealed class ExamAutoShowScheduleSource : IScheduleSource
{
    private readonly IExamService _examService;
    private readonly MainConfigHandler _configHandler;
    private readonly ActionStateStore _state;
    private readonly NtpTimeService _time;

    /// <summary>构造日程源。</summary>
    public ExamAutoShowScheduleSource(
        IExamService examService,
        MainConfigHandler configHandler,
        ActionStateStore state,
        NtpTimeService time)
    {
        _examService = examService;
        _configHandler = configHandler;
        _state = state;
        _time = time;
    }

    /// <inheritdoc />
    public string Id => ExamScheduleProjection.SourceId;

    /// <inheritdoc />
    public string DisplayName => ActionResources.Get("Action_System_ExamAutoShow_Title", "考试自动放映");

    /// <inheritdoc />
    public bool CanPause => true;

    /// <inheritdoc />
    public IReadOnlyList<ScheduleItem> GetItems(DateTime fromInclusive, DateTime toExclusive)
    {
        var board = _examService.ActiveBoard;
        if (board is null)
            return [];

        var items = ExamScheduleProjection.Project(
            board.Exams,
            _configHandler.Data.ExamAutoShow,
            fromInclusive,
            toExclusive);

        if (items.Count == 0)
            return items;

        var autoShowEnabled = board.AutoShowEnabled;
        var today = DateOnly.FromDateTime(_time.NowLocal);

        return items
            .Select(item => item with
            {
                IsEnabled = item.IsEnabled && autoShowEnabled,
                IsSuppressed = IsSuppressed(item, today)
            })
            .ToArray();
    }

    /// <summary>这个条目的「今天不执行」是不是生效：开始那天被停，或今天落在放映期里且今天被停。</summary>
    private bool IsSuppressed(ScheduleItem item, DateOnly today)
    {
        if (_state.IsSuppressed(ExamScheduleProjection.SourceId, DateOnly.FromDateTime(item.Start)))
            return true;

        if (item.End is not { } end)
            return false;

        var todayStart = today.ToDateTime(TimeOnly.MinValue);
        var spansToday = item.Start <= todayStart && end > todayStart;
        return spansToday && _state.IsSuppressed(ExamScheduleProjection.SourceId, today);
    }
}
