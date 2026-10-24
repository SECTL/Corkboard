using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Exam;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Services.Config;
using Corkboard.ViewModels;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Exam;

/// <summary>
///     「考试安排」设置页：这里只当**看板文件列表**——每一行是一份看板（自带标题、副标题与考试安排），
///     点「编辑」把那一份交给 <see cref="Corkboard.Views.ExamBoard.ExamBoardEditorWindow" /> 去改。
///     <para>
///         为什么编辑不放在这里：一份看板要同时摆开「十来列的表」与「十来个字段的表单」，
///         设置页这一列限宽 960 且是纵向滚动，两样只能互相压扁（用户反馈过「布局不太好」）。
///         本页因此只管「有哪些看板、用哪一块、开哪一块」，编辑交给独立窗口。
///     </para>
///     <para>
///         本页由键控 DI 以 transient 创建，而 <see cref="IExamService" /> 是单例：
///         订阅了它就必须在页面离开可视树时断开（<see cref="Attach" /> / <see cref="Detach" />
///         由页面 code-behind 调）。
///     </para>
/// </summary>
public partial class ExamScheduleSettingsPageViewModel : ViewModelBase
{
    /// <summary>状态那一行要跟着时间走，但不必每秒刷，15 秒足够。</summary>
    private static readonly TimeSpan StatusRefreshInterval = TimeSpan.FromSeconds(15);

    private readonly IExamService _examService;
    private readonly IExamTemplateService _templateService;
    private readonly DispatcherTimer _statusTimer;
    private bool _isAttached;

    public ExamScheduleSettingsPageViewModel(
        MainConfigHandler configHandler,
        IExamService examService,
        IExamTemplateService templateService)
        : base(configHandler)
    {
        _examService = examService;
        _templateService = templateService;
        Items = [];
        Templates = [];

        _statusTimer = new DispatcherTimer { Interval = StatusRefreshInterval };
        _statusTimer.Tick += (_, _) => RefreshSummaries();

        Rebuild();
        RebuildTemplates();
    }

    /// <summary>看板显示偏好：这里只用它的「即将开始」阈值，好让列表里的状态与看板一致。</summary>
    public ExamBoardSettingsConfig Settings => Config.ExamBoard;

    /// <summary>全部看板文件（按创建先后）。</summary>
    public ObservableCollection<ExamBoardItem> Items { get; }

    /// <summary>只剩一块看板时不给删——界面上永远要有一块可看的看板。</summary>
    public bool CanDeleteBoard => Items.Count > 1;

    /// <summary>存下来的考试模板（新的在前）。</summary>
    public ObservableCollection<ExamTemplateItem> Templates { get; }

    /// <summary>一份模板都没有：界面上说清楚去哪儿存一份。</summary>
    public bool HasNoTemplate => Templates.Count == 0;

    /// <summary>挂上考试数据与配置变化的订阅（页面进入可视树时调）。重复调用不会重复挂。</summary>
    public void Attach()
    {
        if (_isAttached)
            return;

        _isAttached = true;
        _examService.Changed += OnServiceChanged;
        _templateService.Changed += OnTemplateChanged;
        Settings.PropertyChanged += OnSettingsChanged;
        _statusTimer.Start();
        Rebuild();
        RebuildTemplates();
    }

    /// <summary>断开订阅（页面离开可视树时调）。</summary>
    public void Detach()
    {
        if (!_isAttached)
            return;

        _isAttached = false;
        _statusTimer.Stop();
        _examService.Changed -= OnServiceChanged;
        _templateService.Changed -= OnTemplateChanged;
        Settings.PropertyChanged -= OnSettingsChanged;
    }

    /// <summary>新建一块空看板，并直接把编辑窗口开到它上面（名字在那儿改）。</summary>
    [RelayCommand]
    private void CreateBoard()
    {
        var name = string.Format(CR.Settings_Exam_Schedule_BoardNewTitle, Items.Count + 1);
        var board = _examService.CreateBoard(name);
        App.ShowExamBoardEditorWindow(board.Id);
    }

    /// <summary>删除某一块看板连同它的考试（确认框在页面 code-behind 里弹）。</summary>
    public bool DeleteBoard(ExamBoardItem item) => item is not null && _examService.DeleteBoard(item.Id);

    /// <summary>把某一块设成「当前看板」（全屏看板显示的就是它）。</summary>
    public void SelectBoard(ExamBoardItem item)
    {
        if (item is not null)
            _examService.SelectBoard(item.Id);
    }

    /// <summary>重建整个列表。</summary>
    private void Rebuild()
    {
        Items.Clear();
        foreach (var board in _examService.Boards)
            Items.Add(new ExamBoardItem(board, board.Id == _examService.ActiveBoard.Id, _examService));

        MarkLast(Items);
        RefreshSummaries();
        OnPropertyChanged(nameof(CanDeleteBoard));
    }

    /// <summary>
    ///     把「最后一行」标出来：列表行之间的分界线画在行尾，最后一行底下没有下一行了，
    ///     那条线就该收起来（用户要求过：后面没有别的文件了就别画线）。
    /// </summary>
    private static void MarkLast(ObservableCollection<ExamBoardItem> items)
    {
        for (var i = 0; i < items.Count; i++)
            items[i].IsLast = i == items.Count - 1;
    }

    /// <inheritdoc cref="MarkLast(ObservableCollection{ExamBoardItem})" />
    private static void MarkLast(ObservableCollection<ExamTemplateItem> items)
    {
        for (var i = 0; i < items.Count; i++)
            items[i].IsLast = i == items.Count - 1;
    }

    /// <summary>只刷新「场次 / 下一场 / 当前」这些会随时间变的小结，不重建控件。</summary>
    private void RefreshSummaries()
    {
        var now = DateTime.Now;
        var threshold = Settings.UpcomingThresholdMinutes;

        foreach (var item in Items)
        {
            item.Refresh(now, threshold);
            item.IsActive = item.Id == _examService.ActiveBoard.Id;
        }
    }

    private void OnServiceChanged(object? sender, EventArgs e) => Rebuild();

    private void OnTemplateChanged(object? sender, EventArgs e) => RebuildTemplates();

    /// <summary>重建模板列表（新的在前，顺序由服务保证）。</summary>
    private void RebuildTemplates()
    {
        Templates.Clear();
        foreach (var template in _templateService.Templates)
            Templates.Add(new ExamTemplateItem(template));

        MarkLast(Templates);
        OnPropertyChanged(nameof(HasNoTemplate));
    }

    /// <summary>删掉一份模板（确认框在页面 code-behind 里弹）。</summary>
    public bool DeleteTemplate(ExamTemplateItem item) => item is not null && _templateService.Remove(item.Id);

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 阈值变了，小结里的状态分档也跟着变（看板上是同一套规则）。
        if (e.PropertyName is null or nameof(ExamBoardSettingsConfig.UpcomingThresholdMinutes))
            RefreshSummaries();
    }
}

/// <summary>
///     看板列表里的一行：标题、场次数、下一场、是不是当前那块，以及「自动放映」开关。
///     文案由页面按当前时刻刷新（「下一场」会随考试结束而换）。
/// </summary>
public partial class ExamBoardItem(ExamBoardDocument board, bool isActive, IExamService examService)
    : ObservableObject
{
    /// <summary>这块看板的文件（改标题等直接走它 + <c>Save()</c>）。</summary>
    public ExamBoardDocument Board { get; } = board;

    public Guid Id => Board.Id;

    /// <summary>看板标题（用户填的，或资源里的默认名）。</summary>
    [ObservableProperty]
    private string _title = board.ResolveTitle();

    /// <summary>
    ///     这块看板参不参加自动放映（落盘到看板文件自己的 <c>auto_show_enabled</c>，默认不参加）。
    ///     <para>
    ///         写回走 <see cref="IExamService.SetAutoShow" />：它只改这一份看板文件、不换当前看板、
    ///         也不触发 <c>Changed</c>——列表正在重建，把开关的宿主控件抽走会点不动。
    ///     </para>
    /// </summary>
    [ObservableProperty]
    private bool _autoShowEnabled = board.AutoShowEnabled;

    /// <summary>副标题，没填就是空串（界面上那一行不显示）。</summary>
    [ObservableProperty]
    private string _subtitle = board.ResolveSubtitle();

    [ObservableProperty]
    private bool _hasSubtitle = !string.IsNullOrWhiteSpace(board.ResolveSubtitle());

    /// <summary>场次数（「共 3 场」）。</summary>
    [ObservableProperty]
    private string _countText = string.Empty;

    /// <summary>下一场（「下一场 语文 · 11月6日 周五 08:00 – 10:00」），一场都没有就是空串。</summary>
    [ObservableProperty]
    private string _nextText = string.Empty;

    [ObservableProperty]
    private bool _hasNext;

    /// <summary>是不是全屏看板正在显示的那一块。</summary>
    [ObservableProperty]
    private bool _isActive = isActive;

    /// <summary>
    ///     列表里的最后一行：底下没有下一行了，行尾那条分界线就不该画
    ///     （axaml 侧绑 <c>IsVisible="{Binding !IsLast}"</c> 到分隔线元素上）。
    /// </summary>
    [ObservableProperty]
    private bool _isLast;

    /// <summary>按当前时刻重算这一行的文案。</summary>
    public void Refresh(DateTime now, int upcomingMinutes)
    {
        Title = Board.ResolveTitle();
        Subtitle = Board.ResolveSubtitle();
        HasSubtitle = !string.IsNullOrWhiteSpace(Subtitle);

        // 看板文件可能被别处改过（编辑窗口里也能改这块的开关），刷新时对齐一次。
        AutoShowEnabled = Board.AutoShowEnabled;

        var exams = Board.Exams?.Where(exam => exam is not null).ToList() ?? [];
        CountText = string.Format(CR.Settings_Exam_Schedule_Count, exams.Count);

        var next = exams
            .Where(exam => ExamStatusPolicy.Resolve(exam, now, upcomingMinutes) != ExamStatus.Finished)
            .OrderBy(exam => exam.StartAt)
            .FirstOrDefault();

        HasNext = next is not null;
        NextText = next is null
            ? string.Empty
            : string.Format(CR.Settings_Exam_Schedule_Next,
                next.Subject,
                ExamTimeFormatter.FormatDate(next.Date, DateOnly.FromDateTime(now)),
                ExamTimeFormatter.FormatTimeRange(next));
    }

    /// <summary>
    ///     开关被按了就落盘。落盘失败（看板文件读不了）时把界面退回原来那一档，
    ///     不然用户会以为已经存上了。
    /// </summary>
    partial void OnAutoShowEnabledChanged(bool value)
    {
        if (value == Board.AutoShowEnabled)
            return;

        if (!examService.SetAutoShow(Board.Id, value))
            AutoShowEnabled = Board.AutoShowEnabled;
    }
}

/// <summary>
///     模板列表里的一行：名字 + 「共 12 场 · 跨 3 天」。
///     <para>
///         模板本身不带日期（存的是「第几天」），所以这里也不显示任何具体日期——
///         显示的日期只会误导人以为模板锁在某一年。
///     </para>
/// </summary>
public sealed partial class ExamTemplateItem(ExamPlanTemplate template) : ObservableObject
{
    /// <summary>背后那份模板。</summary>
    public ExamPlanTemplate Template { get; } = template;

    public Guid Id => Template.Id;

    /// <summary>模板名（没填名字时给一个默认名）。</summary>
    public string Name => Template.ResolveName();

    /// <summary>「共 12 场 · 跨 3 天」。</summary>
    public string Summary => Template.Describe();

    /// <summary>
    ///     列表里的最后一行：底下没有下一行了，行尾那条分界线就不该画
    ///     （axaml 侧绑 <c>IsVisible="{Binding !IsLast}"</c> 到分隔线元素上）。
    /// </summary>
    [ObservableProperty]
    private bool _isLast;
}
