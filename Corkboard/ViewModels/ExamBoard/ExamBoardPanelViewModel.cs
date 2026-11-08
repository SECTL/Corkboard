using System.Collections.ObjectModel;
using Avalonia.Threading;
using Corkboard.Core.Exam;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Services.Config;
using Corkboard.ViewModels.SettingsPages.Exam;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.ExamBoard;

/// <summary>
///     「考试看板」面板窗口（<see cref="Corkboard.Views.ExamBoard.ExamBoardPanelWindow" />）的 ViewModel：
///     一份看板 = 列表里一张卡，卡上最显眼的是「放映」按钮。
///     <para>
///         与设置页「考试安排」（<see cref="ExamScheduleSettingsPageViewModel" />）的区别只在外壳：
///         那一页是「配置」语义、埋在设置窗口里；这一扇是**日常操作**——打开就能创建、就能放映，
///         不用先翻设置、也不用先「设为当前」再放映。
///     </para>
///     <para>
///         本窗口的 VM 由键控 DI 以 transient 创建，而 <see cref="IExamService" /> 是单例：
///         订阅了它就必须在窗口关闭时断开（<see cref="Attach" /> / <see cref="Detach" /> 由窗口 code-behind 调）。
///     </para>
///     <para>
///         列表行模型直接复用设置页那个 <see cref="ExamBoardItem" />（标题 / 场次 / 下一场 / 自动放映开关都在里面，
///         包括开关落盘失败的回滚）。两个宿主共用一份行模型，改一处两边一起改，不会漂。
///     </para>
/// </summary>
public partial class ExamBoardPanelViewModel : ViewModelBase
{
    /// <summary>状态那一行要跟着时间走，但不必每秒刷，15 秒足够（与设置页同一口径）。</summary>
    private static readonly TimeSpan StatusRefreshInterval = TimeSpan.FromSeconds(15);

    private readonly IExamService _examService;
    private readonly DispatcherTimer _statusTimer;
    private bool _isAttached;

    public ExamBoardPanelViewModel(MainConfigHandler configHandler, IExamService examService)
        : base(configHandler)
    {
        _examService = examService;
        Items = [];

        _statusTimer = new DispatcherTimer { Interval = StatusRefreshInterval };
        _statusTimer.Tick += (_, _) => RefreshSummaries();

        Rebuild();
    }

    /// <summary>看板显示偏好：这里只用它的「即将开始」阈值，好让卡上的状态与全屏看板一致。</summary>
    public ExamBoardSettingsConfig Settings => Config.ExamBoard;

    /// <summary>全部看板（按创建先后）。</summary>
    public ObservableCollection<ExamBoardItem> Items { get; }

    /// <summary>只剩一块看板时不给删——界面上永远要有一块可看的看板。</summary>
    public bool CanDeleteBoard => Items.Count > 1;

    /// <summary>全屏看板此刻是不是铺在屏幕上（底栏与卡上的「放映中」徽标都看它）。</summary>
    public bool IsPresenting => App.IsExamBoardWindowOpen;

    /// <summary>底栏那句「正在放映：期中考试」。</summary>
    public string PresentingText =>
        string.Format(CR.Exam_Panel_PresentingFormat, _examService.ActiveBoard.ResolveTitle());

    /// <summary>当前看板（全屏正在显示的那一块）。</summary>
    public ExamBoardDocument ActiveBoard => _examService.ActiveBoard;

    /// <summary>挂上考试数据与配置变化的订阅（窗口 Opened 时调）。重复调用不会重复挂。</summary>
    public void Attach()
    {
        if (_isAttached)
            return;

        _isAttached = true;
        _examService.Changed += OnServiceChanged;
        Settings.PropertyChanged += OnSettingsChanged;
        App.ExamBoardWindowVisibilityChanged += OnPresentingStateChanged;
        _statusTimer.Start();
        Rebuild();
    }

    /// <summary>断开订阅（窗口 Closed 时调）。漏了这一句，每开一次窗口就多挂一份处理器。</summary>
    public void Detach()
    {
        if (!_isAttached)
            return;

        _isAttached = false;
        _statusTimer.Stop();
        _examService.Changed -= OnServiceChanged;
        Settings.PropertyChanged -= OnSettingsChanged;
        App.ExamBoardWindowVisibilityChanged -= OnPresentingStateChanged;
    }

    /// <summary>放映某一块：先把它设成当前，再把全屏看板铺上去。</summary>
    public void Present(ExamBoardItem item)
    {
        if (item is null)
            return;

        _examService.SelectBoard(item.Id);
        App.ShowExamBoardWindow();
        RefreshSummaries();
    }

    /// <summary>新建一块空看板（名字在编辑窗口里改），返回新建出来的那一块。</summary>
    public ExamBoardDocument CreateBoard()
    {
        var name = string.Format(CR.Settings_Exam_Schedule_BoardNewTitle, Items.Count + 1);
        return _examService.CreateBoard(name);
    }

    /// <summary>把某一块设成「当前看板」（全屏看板显示的就是它）。</summary>
    public void SelectBoard(ExamBoardItem item)
    {
        if (item is null)
            return;

        _examService.SelectBoard(item.Id);
        RefreshSummaries();
    }

    /// <summary>删除某一块看板连同它的考试（确认框在窗口 code-behind 里弹）。</summary>
    public bool DeleteBoard(ExamBoardItem item) => item is not null && _examService.DeleteBoard(item.Id);

    /// <summary>把导入的考试追加进**当前这块**看板，返回真正加进去的场数。</summary>
    public int ImportExams(IEnumerable<ExamEntry> exams) => _examService.AddMany(exams).Count;

    /// <summary>重建整个列表。</summary>
    public void Rebuild()
    {
        Items.Clear();
        foreach (var board in _examService.Boards)
            Items.Add(new ExamBoardItem(board, board.Id == _examService.ActiveBoard.Id, _examService));

        MarkLast();
        RefreshSummaries();
        OnPropertyChanged(nameof(CanDeleteBoard));
    }

    /// <summary>
    ///     把「最后一张卡」标出来：卡片之间的间距靠外边距留，最后一张不需要（与设置页的
    ///     「后面没有别的文件了就别画线」同一条口径）。
    /// </summary>
    private void MarkLast()
    {
        for (var i = 0; i < Items.Count; i++)
            Items[i].IsLast = i == Items.Count - 1;
    }

    /// <summary>只刷新「场次 / 下一场 / 当前 / 放映中」这些会随时间变的小结，不重建控件。</summary>
    private void RefreshSummaries()
    {
        var now = DateTime.Now;
        var threshold = Settings.UpcomingThresholdMinutes;
        var presentingActiveBoard = App.IsExamBoardWindowOpen;
        var activeBoardId = _examService.ActiveBoard.Id;

        foreach (var item in Items)
        {
            item.Refresh(now, threshold);
            item.IsActive = item.Id == activeBoardId;
            item.IsPresenting = presentingActiveBoard && item.IsActive;
        }

        OnPropertyChanged(nameof(IsPresenting));
        OnPropertyChanged(nameof(PresentingText));
    }

    private void OnServiceChanged(object? sender, EventArgs e) => Rebuild();

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 阈值变了，小结里的状态分档也跟着变（看板上是同一套规则）。
        if (e.PropertyName is null or nameof(ExamBoardSettingsConfig.UpcomingThresholdMinutes))
            RefreshSummaries();
    }

    /// <summary>
    ///     全屏看板开了 / 关了：卡上的「放映中」徽标与底栏要立刻跟上。
    ///     这条通知由 <see cref="App.ShowExamBoardWindow" /> / <see cref="App.CloseExamBoardWindow" />
    ///     / 看板窗口自己的 Closed 发出，不靠 15 秒计时器把它慢慢刷出来。
    /// </summary>
    private void OnPresentingStateChanged(object? sender, EventArgs e) => RefreshSummaries();
}
