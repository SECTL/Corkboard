using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Abstraction.Services;
using Corkboard.Core.Models.Board;
using Corkboard.Core.Services.Board;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Services.Ui;

/// <summary>时间轴下拉里的一项：某一天，以及那天为止累计的作业数。</summary>
public sealed record BoardReplayStopItem(DateOnly Date, int NoteCount)
{
    /// <summary>下拉里显示的文本，例如「2026-10-06 · 共 3 项」。</summary>
    public string DisplayName =>
        string.Format(CR.Board_ReplayStopFormat, Date.ToString("yyyy-MM-dd"), NoteCount);
}

/// <summary>
///     「时空回放」的状态宿主：把作业按创建日期摊成一条时间轴，让用户沿时间轴往回看。
///     <para>
///         入口画在主界面标题栏（<c>MainView</c>），作业列表画在主页面（<c>BoardPage</c>），
///         两边是两个不同的对象，所以这份状态放在<b>单例服务</b>上：壳负责控制，页面订阅它并据此筛选。
///     </para>
///     <para>
///         回放看的是<b>累计</b>结果——停在某一天时，板子上显示的是「到那天为止已经存在的全部作业」，
///         而不是只看那一天新增的；否则回放会变成「按天翻页」，看不出板子是怎么长起来的。
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
    private DispatcherTimer? _armTimer;

    public BoardReplayService(IBoardService boardService)
    {
        _boardService = boardService;
        _boardService.Changed += OnBoardChanged;
        RefreshStops();
    }

    /// <summary>时间轴上的停靠点，按日期升序。下拉直接绑它。</summary>
    public ObservableCollection<BoardReplayStopItem> Stops { get; } = [];

    /// <summary>是否处于回放模式。为真时主页面显示历史快照，并且不能编辑。</summary>
    [ObservableProperty] private bool _isActive;

    /// <summary>是否已「预备」：第一次点过了，正等第二次点确认。</summary>
    [ObservableProperty] private bool _isArmed;

    /// <summary>当前停靠点。下拉直接双向绑它。</summary>
    [ObservableProperty] private BoardReplayStopItem? _selectedStop;

    public bool HasStops => Stops.Count > 0;

    /// <summary>
    ///     回放入口是否该出现：有可回放的作业、且当前没在回放。
    ///     一条作业都没有时不显示入口——空板上没有「过去」可看，留着按钮只会让人点进去看空页面。
    /// </summary>
    public bool CanEnterReplay => BoardReplayGate.CanEnter(HasStops, IsActive);

    /// <summary>入口按钮上的文案：预备之后提示再点一次。</summary>
    public string EnterButtonText => IsArmed ? CR.Board_ReplayConfirm : CR.Board_Replay;

    /// <summary>当前停在的日期；没有可回放的作业时为 <c>null</c>。</summary>
    public DateOnly? CurrentDate => SelectedStop?.Date;

    /// <summary>停在当前日期时，板子上应该出现的作业条数。</summary>
    public int CurrentCount => SelectedStop?.NoteCount ?? 0;

    /// <summary>主页面顶部的回放提示文案，例如「回放中：停在 2026-10-06，板上有 5 项作业」。</summary>
    public string BannerText => CurrentDate is { } date
        ? string.Format(CR.Board_ReplayBannerFormat, date.ToString("yyyy-MM-dd"), CurrentCount)
        : CR.Board_ReplayEmpty;

    /// <summary>
    ///     回放入口的点击：第一下进入「预备」并起一个超时，第二下才真的进回放。
    ///     超时内没等到第二下就自动退回原按钮（见 <see cref="Disarm" />）。
    ///     该不该进、这是第几下，由 <see cref="BoardReplayGate" /> 判定。
    /// </summary>
    public void Enter()
    {
        switch (BoardReplayGate.Click(HasStops, IsArmed))
        {
            case BoardReplayClickResult.Ignored:
                return;

            case BoardReplayClickResult.Armed:
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

    /// <summary>按当前回放位置筛出该显示的作业；没在回放时返回原样的全部作业。</summary>
    public IReadOnlyList<BoardNote> Filter(IEnumerable<BoardNote> notes)
    {
        if (!IsActive)
            return [.. notes];

        return CurrentDate is { } date ? BoardReplayTimeline.Select(notes, date) : [];
    }

    /// <summary>
    ///     重建时间轴。重建后尽量停在原来那一天上：回放期间时间轴可能因为别处改动而变化，
    ///     但用户正看的那一天不该被悄悄挪走。
    /// </summary>
    private void RefreshStops()
    {
        var previousDate = CurrentDate;

        Stops.Clear();
        foreach (var stop in BoardReplayTimeline.Build(_boardService.Notes))
            Stops.Add(new BoardReplayStopItem(stop.Date, stop.NoteCount));

        if (previousDate is { } date)
        {
            var restored = Stops.FirstOrDefault(stop => stop.Date == date);
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
    ///     作业量很小，整份重算的成本可以忽略。
    /// </summary>
    private void OnBoardChanged(object? sender, EventArgs e)
    {
        RefreshStops();
        OnPropertyChanged(nameof(HasStops));
        OnPropertyChanged(nameof(CanEnterReplay));

        // 作业删空之后回放已经没意义了，别把用户留在空的历史视图里。
        if (IsActive && !HasStops)
            Exit();
    }

    partial void OnSelectedStopChanged(BoardReplayStopItem? value)
    {
        OnPropertyChanged(nameof(CurrentDate));
        OnPropertyChanged(nameof(CurrentCount));
        OnPropertyChanged(nameof(BannerText));
    }

    partial void OnIsArmedChanged(bool value) => OnPropertyChanged(nameof(EnterButtonText));

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEnterReplay));

        if (value)
            return;

        OnPropertyChanged(nameof(CurrentDate));
        OnPropertyChanged(nameof(CurrentCount));
        OnPropertyChanged(nameof(BannerText));
    }
}
