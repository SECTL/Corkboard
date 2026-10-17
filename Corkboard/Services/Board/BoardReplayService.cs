using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Board;
using Corkboard.Core.Board.History;
using Corkboard.Core.Notes;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Services.Board;

/// <summary>时间轴上的一个停靠点：某个时刻（精确到秒），以及那一刻板子上看得见的作业数。</summary>
public sealed record BoardReplayStopItem(DateTimeOffset At, int NoteCount)
{
    /// <summary>下拉里显示的文本，例如「2026-10-06 08:30:00 · 共 3 项」。</summary>
    public string DisplayName =>
        string.Format(CR.Board_ReplayStopFormat, At.ToString("yyyy-MM-dd HH:mm:ss"), NoteCount);

    /// <summary>这个停靠点所在的本地日期（日历按月分组、按天亮格都用它）。</summary>
    public DateOnly Day => DateOnly.FromDateTime(At.ToLocalTime().Date);
}

/// <summary>
///     「时空回放」的状态宿主：沿**历史日志**（<see cref="INoteHistoryStore" />）往回看作业板。
///     <para>
///         入口画在主界面标题栏（<c>MainView</c>），作业列表画在主页面（<c>BoardPage</c>），
///         两边是两个不同的对象，所以这份状态放在<b>单例服务</b>上：壳负责控制，页面订阅它并据此筛选。
///     </para>
///     <para>
///         与旧实现（按作业的创建时刻摊时间轴）最大的区别：停靠点来自**每一次真实的增删改**，
///         停在某一秒时显示的是「那一刻板子上的样子」——之后被删掉、被改过的作业，
///         在它还在的那一刻仍然按当时的内容画出来。
///     </para>
///     <para>
///         本服务是单例，订阅 <see cref="IBoardService.Changed" /> 不需要断开：
///         它的生命周期和进程一样长（单例订阅单例，不存在 transient 那种越挂越多的问题）。
///     </para>
/// </summary>
public partial class BoardReplayService : ObservableObject
{
    /// <summary>
    ///     进入回放要点两次：第一次只是「预备」，超过这段时间没再点就自动退回原按钮。
    ///     回放会把整块板子切到历史快照，误触代价不小，所以做成两下确认。
    /// </summary>
    private static readonly TimeSpan ArmTimeout = TimeSpan.FromSeconds(3);

    private readonly IBoardService _boardService;
    private readonly INoteHistoryStore _historyStore;
    private DispatcherTimer? _armTimer;

    public BoardReplayService(IBoardService boardService, INoteHistoryStore historyStore)
    {
        _boardService = boardService;
        _historyStore = historyStore;
        _boardService.Changed += OnBoardChanged;
        RefreshStops();
    }

    /// <summary>时间轴上的停靠点，按时刻升序。下拉与日历都绑它。</summary>
    public ObservableCollection<BoardReplayStopItem> Stops { get; } = [];

    /// <summary>是否处于回放模式。为真时主页面显示历史快照，并且不能编辑。</summary>
    [ObservableProperty] private bool _isActive;

    /// <summary>是否已「预备」：第一次点过了，正等第二次点确认。</summary>
    [ObservableProperty] private bool _isArmed;

    /// <summary>当前停靠点。下拉与日历都双向绑它。</summary>
    [ObservableProperty] private BoardReplayStopItem? _selectedStop;

    public bool HasStops => Stops.Count > 0;

    /// <summary>
    ///     回放入口是否该出现：历史里有时刻可停、且当前没在回放。
    ///     一条历史都没有时不显示入口——空板上没有「过去」可看，留着按钮只会让人点进去看空页面。
    /// </summary>
    public bool CanEnterReplay => ReplayGate.CanEnter(HasStops, IsActive);

    /// <summary>入口按钮上的文案：预备之后提示再点一次。</summary>
    public string EnterButtonText => IsArmed ? CR.Board_ReplayConfirm : CR.Board_Replay;

    /// <summary>当前停在的时刻；没有可回放的历史时为 <c>null</c>。</summary>
    public DateTimeOffset? CurrentAt => SelectedStop?.At;

    /// <summary>停在当前时刻时，板子上应该出现的作业条数。</summary>
    public int CurrentCount => SelectedStop?.NoteCount ?? 0;

    /// <summary>主页面顶部的回放提示文案，例如「回放中：停在 2026-10-06 08:30:00，板上有 5 项作业」。</summary>
    public string BannerText => CurrentAt is { } at
        ? string.Format(CR.Board_ReplayBannerFormat, at.ToString("yyyy-MM-dd HH:mm:ss"), CurrentCount)
        : CR.Board_ReplayEmpty;

    /// <summary>
    ///     回放入口的点击：第一下进入「预备」并起一个超时，第二下才真的进回放。
    ///     超时内没等到第二下就自动退回原按钮（见 <see cref="Disarm" />）。
    ///     该不该进、这是第几下，由 <see cref="ReplayGate" /> 判定。
    /// </summary>
    public void Enter()
    {
        switch (ReplayGate.Click(HasStops, IsArmed))
        {
            case ReplayClickResult.Ignored:
                return;

            case ReplayClickResult.Armed:
                Arm();
                return;

            default:
                CancelArmTimer();
                IsArmed = false;
                EnterReplay();
                return;
        }
    }

    /// <summary>点第一下：进入预备状态，并起一个超时自动退回。</summary>
    private void Arm()
    {
        IsArmed = true;

        CancelArmTimer();
        _armTimer = new DispatcherTimer { Interval = ArmTimeout };
        _armTimer.Tick += OnArmTimeout;
        _armTimer.Start();
    }

    private void OnArmTimeout(object? sender, EventArgs e) => Disarm();

    /// <summary>退回原按钮：预备状态消失，什么都不发生。</summary>
    private void Disarm()
    {
        CancelArmTimer();
        IsArmed = false;
    }

    private void CancelArmTimer()
    {
        if (_armTimer is null)
            return;

        _armTimer.Stop();
        _armTimer.Tick -= OnArmTimeout;
        _armTimer = null;
    }

    /// <summary>真的进入回放：默认停在最后一个停靠点，也就是「现在」，再由用户往回选。</summary>
    private void EnterReplay()
    {
        RefreshStops();

        IsActive = true;
        SelectedStop = Stops.Count > 0 ? Stops[^1] : null;
    }

    /// <summary>退出回放，回到实时视图。</summary>
    public void Exit()
    {
        Disarm();
        IsActive = false;
        SelectedStop = null;
    }

    /// <summary>
    ///     按当前回放位置筛出该显示的作业；没在回放时返回原样的全部作业。
    ///     回放期间返回的是**历史快照重放出来的作业**（不是 <see cref="IBoardService.Notes" /> 里的对象），
    ///     所以那一刻之后被删掉、被改过的都不会影响它。
    /// </summary>
    public IReadOnlyList<Note> Filter(IEnumerable<Note> notes)
    {
        if (!IsActive)
            return [.. notes];

        return CurrentAt is { } at ? SnapshotAt(at) : [];
    }

    /// <summary>重放出「停在 <paramref name="at" /> 这一刻时板子上的作业」。</summary>
    public IReadOnlyList<Note> SnapshotAt(DateTimeOffset at)
        => BoardHistory.StateAt(_historyStore.LoadAll(), at, _historyStore.LoadSnapshot);

    /// <summary>
    ///     把当前回放位置那一刻板子上的作业整份「还原」回当前页面，然后退出回放让用户马上看到。
    ///     返回真正动过的条数。
    ///     <para>
    ///         与旧的「只把收起来的放出来」不同：这里认的是历史快照，
    ///         被整条删掉的作业会连 <see cref="Note.Id" /> 与创建时间一起重新插回来。
    ///     </para>
    /// </summary>
    public int RestoreCurrentSnapshot()
    {
        if (!IsActive || CurrentAt is not { } at)
            return 0;

        var restored = _boardService.RestoreSnapshot(SnapshotAt(at));

        if (restored > 0)
            Exit();

        return restored;
    }

    /// <summary>
    ///     重建时间轴。重建后尽量停在原来那个时刻上：回放期间时间轴可能因为别处改动而变化，
    ///     但用户正看的那一刻不该被悄悄挪走。
    /// </summary>
    private void RefreshStops()
    {
        var previousAt = CurrentAt;

        Stops.Clear();

        IReadOnlyList<NoteHistoryEntry> entries;
        try
        {
            entries = _historyStore.LoadAll();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            entries = [];
        }

        foreach (var stop in BoardHistory.BuildStops(entries, _historyStore.LoadSnapshot))
            Stops.Add(new BoardReplayStopItem(stop.At, stop.NoteCount));

        if (previousAt is { } at)
        {
            var restored = Stops.FirstOrDefault(stop => stop.At == at);
            if (restored is not null)
            {
                SelectedStop = restored;
                return;
            }
        }

        if (SelectedStop is not null && !Stops.Contains(SelectedStop))
            SelectedStop = Stops.Count > 0 ? Stops[^1] : null;
    }

    /// <summary>
    ///     作业有任何变化都要跟着刷新：时间轴决定入口按钮的显隐（见 <see cref="CanEnterReplay" />），
    ///     只在回放期间刷新的话，实时视图里新增第一条作业时按钮不会出现、删光最后一条也不会消失。
    ///     历史条数不大，整份重算的成本可以忽略。
    /// </summary>
    private void OnBoardChanged(object? sender, EventArgs e)
    {
        RefreshStops();
        OnPropertyChanged(nameof(HasStops));
        OnPropertyChanged(nameof(CanEnterReplay));

        // 历史被清空（例如用户在设置里清掉了历史）之后回放已经没有意义了，别把用户留在空视图里。
        if (IsActive && !HasStops)
            Exit();
    }

    partial void OnSelectedStopChanged(BoardReplayStopItem? value)
    {
        OnPropertyChanged(nameof(CurrentAt));
        OnPropertyChanged(nameof(CurrentCount));
        OnPropertyChanged(nameof(BannerText));
    }

    partial void OnIsArmedChanged(bool value) => OnPropertyChanged(nameof(EnterButtonText));

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEnterReplay));

        if (value)
            return;

        OnPropertyChanged(nameof(CurrentAt));
        OnPropertyChanged(nameof(CurrentCount));
        OnPropertyChanged(nameof(BannerText));
    }
}
