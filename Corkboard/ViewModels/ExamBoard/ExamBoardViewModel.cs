using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Exam;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Time;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.ExamBoard;

/// <summary>
///     全屏考试看板的 ViewModel：时钟、考试表格、标题副标题，以及「这一刻该显示什么」。
///     <para>
///         显示形态由**下一场考试的状态**决定（<see cref="ExamStatusPolicy" />）：
///         未开始 → 时钟 + 全部考试；即将开始 → 默认只留下一场（可关）；进行中 → 默认只留时钟（可开）。
///         三种形态共用同一套控件，靠 <see cref="ShowList" /> / <see cref="ShowNextCard" /> 与
///         时钟的列位 / 字号切换，所以切换时不会有「整块重建」的跳动。
///     </para>
///     <para>
///         时钟每秒走一次、状态每分钟变一次：前者只更新几个字符串，后者顺带把每行的状态刷一遍。
///         本类是 transient、服务与配置是单例，订阅必须在窗口关闭时断开（<see cref="Attach" /> /
///         <see cref="Detach" /> 由窗口 code-behind 调）。
///     </para>
/// </summary>
public partial class ExamBoardViewModel : ViewModelBase
{
    /// <summary>时钟心跳。1 秒是秒针的自然粒度，再密只是白烧 CPU。</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    /// <summary>列表形态下时钟的字号。</summary>
    private const double ClockFontSizeWithList = 108;

    /// <summary>只剩时钟时把它放大到屏幕级别（进行中就是整块屏幕上只有时间）。</summary>
    private const double ClockFontSizeAlone = 220;

    /// <summary>长按退出的判定时长：进度圈走满一圈才真的关掉看板。</summary>
    private static readonly TimeSpan HoldToExitDuration = TimeSpan.FromSeconds(2);

    /// <summary>进度圈的重绘间隔：约 60 fps，两秒 120 帧，顺滑又不费。</summary>
    private static readonly TimeSpan HoldFrameInterval = TimeSpan.FromMilliseconds(16);

    /// <summary>进度圈起点色（蓝）：刚按下，还没什么危险。</summary>
    private static readonly Color HoldRingStartColor = Color.Parse("#0078D4");

    /// <summary>进度圈终点色（红）：走满了就要退了，颜色得先提醒到。</summary>
    private static readonly Color HoldRingEndColor = Color.Parse("#E81123");

    private readonly MainConfigHandler _configHandler;
    private readonly NtpTimeService _time;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _holdTimer;
    private readonly Stopwatch _holdWatch = new();

    /// <summary>进度圈的颜色笔刷：一个实例反复改 <see cref="SolidColorBrush.Color" />，不每帧新建对象。</summary>
    private readonly SolidColorBrush _holdRingBrush = new(HoldRingStartColor);

    private bool _isAttached;
    private DateOnly _today;

    public ExamBoardViewModel(MainConfigHandler configHandler, IExamService examService, NtpTimeService time)
        : base(configHandler)
    {
        _configHandler = configHandler;
        _time = time;
        Exams = examService;

        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += (_, _) => Tick();

        _holdTimer = new DispatcherTimer { Interval = HoldFrameInterval };
        _holdTimer.Tick += (_, _) => OnHoldTick();

        RefreshStatic();
        RefreshNow();
    }

    /// <summary>考试安排数据源（窗口开着时它会随设置页的改动实时更新）。</summary>
    public IExamService Exams { get; }

    public ExamBoardSettingsConfig Settings => Config.ExamBoard;

    /// <summary>右侧 / 另一侧的考试表格行（按开考时间升序）。</summary>
    public ObservableCollection<ExamRowItem> Rows { get; } = new();

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private bool _hasSubtitle;

    /// <summary>副标题的颜色：没自定义时用主题的次要文字色（比主标题淡）。</summary>
    [ObservableProperty]
    private IBrush _subtitleBrush = Brushes.Gray;

    [ObservableProperty]
    private string _clockTime = string.Empty;

    [ObservableProperty]
    private string _clockDate = string.Empty;

    /// <summary>「已校时」/「本机时间」——右下角那行小字，让用户知道这个钟准不准。</summary>
    [ObservableProperty]
    private string _clockSource = string.Empty;

    [ObservableProperty]
    private bool _isClockSynced;

    [ObservableProperty]
    private double _clockFontSize = ClockFontSizeWithList;

    /// <summary>时钟所在的列（0 / 1），跟着「时钟位置」设置走。</summary>
    [ObservableProperty]
    private int _clockColumn;

    /// <summary>考试列表所在的列，与时钟互补。</summary>
    [ObservableProperty]
    private int _listColumn = 1;

    /// <summary>只剩时钟时让它横跨两列，才会真正居中。</summary>
    [ObservableProperty]
    private int _clockColumnSpan = 1;

    /// <summary>是否显示考试表格（未开始 / 即将开始没收起 / 考试中开了显示）。</summary>
    [ObservableProperty]
    private bool _showList = true;

    /// <summary>即将开始且收起了列表：只显示下一场的那张卡。</summary>
    [ObservableProperty]
    private bool _showNextCard;

    [ObservableProperty]
    private bool _hasExams;

    [ObservableProperty]
    private bool _isTableHeaderVisible;

    [ObservableProperty]
    private string _nextSubject = string.Empty;

    [ObservableProperty]
    private string _nextDate = string.Empty;

    [ObservableProperty]
    private string _nextTime = string.Empty;

    [ObservableProperty]
    private string _nextStatus = string.Empty;

    [ObservableProperty]
    private string _nextPaperInfo = string.Empty;

    [ObservableProperty]
    private bool _nextHasPaperInfo;

    [ObservableProperty]
    private bool _nextIsUpcoming;

    [ObservableProperty]
    private bool _nextIsInProgress;

    /// <summary>正在长按退出：进度圈与轨道这时才画出来，平时只有一颗干净的图标。</summary>
    [ObservableProperty]
    private bool _isHolding;

    /// <summary>进度圈的扫过角度（0–360），由按住时长线性推进。</summary>
    [ObservableProperty]
    private double _holdSweepAngle;

    /// <summary>进度圈的颜色：随进度由蓝渐变到红（见 <see cref="HoldRingStartColor" />）。</summary>
    public IBrush HoldRingBrush => _holdRingBrush;

    /// <summary>长按走满一圈：窗口该关了（code-behind 收到就 <c>Close()</c>）。</summary>
    public event EventHandler? ExitRequested;

    /// <summary>按下左下角那颗图标：进度圈从零开始长，满 2 秒发 <see cref="ExitRequested" />。</summary>
    public void BeginHold()
    {
        _holdTimer.Stop();
        _holdWatch.Restart();

        IsHolding = true;
        HoldSweepAngle = 0;
        _holdRingBrush.Color = HoldRingStartColor;

        _holdTimer.Start();
    }

    /// <summary>松手 / 失去指针捕获：进度归零，什么都不发生（误触不能关掉看板）。</summary>
    public void CancelHold()
    {
        _holdTimer.Stop();
        _holdWatch.Reset();

        IsHolding = false;
        HoldSweepAngle = 0;
        _holdRingBrush.Color = HoldRingStartColor;
    }

    /// <summary>每帧推进进度圈；走到 360 度就收尾并发退出请求。</summary>
    private void OnHoldTick()
    {
        var progress = Math.Clamp(_holdWatch.Elapsed / HoldToExitDuration, 0d, 1d);

        HoldSweepAngle = progress * 360;
        _holdRingBrush.Color = LerpColor(HoldRingStartColor, HoldRingEndColor, progress);

        if (progress < 1)
            return;

        CancelHold();
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>两色之间线性插值（含透明度），<paramref name="t" /> 夹在 0–1。</summary>
    private static Color LerpColor(Color from, Color to, double t)
    {
        var ratio = Math.Clamp(t, 0d, 1d);
        return Color.FromArgb(
            (byte)Math.Round(from.A + (to.A - from.A) * ratio),
            (byte)Math.Round(from.R + (to.R - from.R) * ratio),
            (byte)Math.Round(from.G + (to.G - from.G) * ratio),
            (byte)Math.Round(from.B + (to.B - from.B) * ratio));
    }

    /// <summary>挂上订阅并开始走钟（窗口打开时调）。重复调用不会重复挂。</summary>
    public void Attach()
    {
        if (_isAttached)
            return;

        _isAttached = true;
        Exams.Changed += OnExamsChanged;
        Settings.PropertyChanged += OnSettingsChanged;
        _time.StatusChanged += OnTimeStatusChanged;

        RebuildRows();
        _timer.Start();
        RefreshNow();
    }

    /// <summary>断开订阅、停掉计时器（窗口关闭时调）。</summary>
    public void Detach()
    {
        if (!_isAttached)
            return;

        _isAttached = false;
        _timer.Stop();
        CancelHold();
        Exams.Changed -= OnExamsChanged;
        Settings.PropertyChanged -= OnSettingsChanged;
        _time.StatusChanged -= OnTimeStatusChanged;
    }

    /// <summary>每秒一次：先刷新时钟，跨日了才重建行（日期列会从「今天」变成「明天」）。</summary>
    private void Tick()
    {
        var today = DateOnly.FromDateTime(_time.NowLocal);
        if (today != _today)
            RebuildRows();

        RefreshNow();
    }

    /// <summary>标题 / 副标题 / 颜色这类「只有配置变了才动」的东西。</summary>
    private void RefreshStatic()
    {
        // 标题与副标题跟着**当前看板**走（每块看板一个文件），不是全局配置。
        var board = Exams.ActiveBoard;
        Title = board.ResolveTitle();
        Subtitle = board.ResolveSubtitle();
        HasSubtitle = !string.IsNullOrWhiteSpace(Subtitle);
        SubtitleBrush = ResolveSubtitleBrush();

        var clockOnRight = Settings.ClockSide == ExamBoardClockSide.Right;
        ClockColumn = clockOnRight ? 1 : 0;
        ListColumn = clockOnRight ? 0 : 1;
    }

    /// <summary>
    ///     每秒刷新的那一批：时钟文字、下一场考试的状态，以及「这一刻该显示什么」。
    ///     形态与行状态都在这里算，切换点（开考前 30 分钟、开考、考完）到点即生效。
    /// </summary>
    private void RefreshNow()
    {
        var now = _time.NowLocal;

        ClockTime = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        ClockDate = FormatClockDate(now);
        IsClockSynced = _time.IsSynced;
        ClockSource = IsClockSynced ? CR.Exam_Board_ClockSynced : CR.Exam_Board_ClockLocal;

        var threshold = ExamStatusPolicy.ClampUpcomingMinutes(Settings.UpcomingThresholdMinutes);
        var next = ExamStatusPolicy.FindNext(Exams.Exams, now);
        var nextStatus = next is null ? ExamStatus.Finished : ExamStatusPolicy.Resolve(next, now, threshold);

        // 三种形态：整表 / 只留下一场 / 只剩时钟。
        // 收起列表时，即将开始那一档单独留一张卡；进行中则连卡都不留（整块屏幕上只有时间）。
        var showList = nextStatus switch
        {
            ExamStatus.InProgress => Settings.ShowExamListInProgress,
            ExamStatus.Upcoming => !Settings.HideExamListWhenUpcoming,
            _ => true
        };
        var showNextCard = !showList && nextStatus == ExamStatus.Upcoming;

        ShowList = showList;
        ShowNextCard = showNextCard;
        ClockColumnSpan = showList ? 1 : 2;
        ClockFontSize = showList ? ClockFontSizeWithList : ClockFontSizeAlone;

        HasExams = Exams.Exams.Count > 0;
        IsTableHeaderVisible = showList && Rows.Count > 0;

        if (next is null)
        {
            NextSubject = string.Empty;
            NextDate = string.Empty;
            NextTime = string.Empty;
            NextStatus = string.Empty;
            NextPaperInfo = string.Empty;
            NextHasPaperInfo = false;
            NextIsUpcoming = false;
            NextIsInProgress = false;
        }
        else
        {
            NextSubject = next.Subject;
            NextDate = ExamTimeFormatter.FormatDate(next.Date, DateOnly.FromDateTime(now));
            NextTime = ExamTimeFormatter.FormatTimeRange(next);
            NextStatus = ExamTimeFormatter.FormatStatusDetail(nextStatus, next, now);
            NextPaperInfo = ExamTimeFormatter.FormatPaperInfo(next);
            NextHasPaperInfo = !string.IsNullOrEmpty(NextPaperInfo);
            NextIsUpcoming = nextStatus == ExamStatus.Upcoming;
            NextIsInProgress = nextStatus == ExamStatus.InProgress;
        }

        UpdateRowStatuses(now, threshold);
    }

    /// <summary>整表重建：考试增删改、跨日时调。行对象保持稳定，之后只改它们的文字。</summary>
    private void RebuildRows()
    {
        var now = _time.NowLocal;
        _today = DateOnly.FromDateTime(now);

        Rows.Clear();
        foreach (var exam in Exams.Exams.OrderBy(exam => exam.StartAt).ThenBy(exam => exam.Subject,
                     StringComparer.CurrentCulture))
            Rows.Add(new ExamRowItem(exam, ExamTimeFormatter.FormatDate(exam.Date, _today),
                ExamTimeFormatter.FormatTimeRange(exam)));

        var threshold = ExamStatusPolicy.ClampUpcomingMinutes(Settings.UpcomingThresholdMinutes);
        UpdateRowStatuses(now, threshold);

        HasExams = Exams.Exams.Count > 0;
        IsTableHeaderVisible = ShowList && Rows.Count > 0;
    }

    /// <summary>每行的状态文字与配色标记（配色靠样式类，见窗口 XAML）。</summary>
    private void UpdateRowStatuses(DateTime now, int threshold)
    {
        var nextId = ExamStatusPolicy.FindNext(Exams.Exams, now)?.Id;
        foreach (var row in Rows)
        {
            var status = ExamStatusPolicy.Resolve(row.Exam, now, threshold);
            row.UpdateStatus(ExamTimeFormatter.FormatStatusDetail(status, row.Exam, now), status, row.Exam.Id == nextId);
        }
    }

    /// <summary>时钟下面那行日期：「2026年10月18日 周日」。</summary>
    private static string FormatClockDate(DateTime now)
    {
        var date = DateOnly.FromDateTime(now);
        return string.Format(CultureInfo.CurrentCulture, "{0}年{1}月{2}日 {3}",
            date.Year, date.Month, date.Day, ExamTimeFormatter.WeekdayName(date));
    }

    /// <summary>
    ///     副标题颜色：配了就用配的，没配就用主题的次要文字色（比主标题淡一档）。
    ///     ⚠️ 取主题资源必须显式带 <see cref="Application.ActualThemeVariant" />，
    ///     否则拿到的是亮色字典里的那份，深色主题下反而更黑。
    /// </summary>
    private IBrush ResolveSubtitleBrush()
    {
        if (Exams.ActiveBoard.SubtitleColor is { } color)
            return new SolidColorBrush(color);

        if (Application.Current is { } app
            && app.TryGetResource("TextFillColorSecondaryBrush", app.ActualThemeVariant, out var resource)
            && resource is IBrush brush)
            return brush;

        return Brushes.Gray;
    }

    /// <summary>考试增删改 / 切看板 / 改标题副标题都会走这里（服务只在落盘后发一次通知）。</summary>
    private void OnExamsChanged(object? sender, EventArgs e)
    {
        RebuildRows();
        RefreshStatic();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 时钟摆哪边、以及（通过 RefreshNow 重算的）阈值与两个开关。
        if (e.PropertyName is null or nameof(ExamBoardSettingsConfig.ClockSide))
            RefreshStatic();

        RefreshNow();
    }

    private void OnTimeStatusChanged(object? sender, EventArgs e) => RefreshNow();
}

/// <summary>
///     表格里的一行。文字在重建时定下来（日期、科目、时间区间），状态每秒可能变，
///     所以单独用 <see cref="UpdateStatus" /> 更新，避免整表重建导致列表闪动。
/// </summary>
public partial class ExamRowItem : ObservableObject
{
    public ExamRowItem(ExamEntry exam, string dateText, string timeText)
    {
        Exam = exam;
        _dateText = dateText;
        _subject = exam.Subject;
        _timeText = timeText;
    }

    public ExamEntry Exam { get; }

    [ObservableProperty]
    private string _dateText;

    [ObservableProperty]
    private string _subject;

    [ObservableProperty]
    private string _timeText;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>状态配色用的标记（窗口 XAML 里按这三个类换前景色）。</summary>
    [ObservableProperty]
    private bool _isUpcoming;

    [ObservableProperty]
    private bool _isInProgress;

    [ObservableProperty]
    private bool _isFinished;

    /// <summary>是不是「下一场」——那一行加粗，一眼能找出来。</summary>
    [ObservableProperty]
    private bool _isNext;

    /// <summary>卷面信息（「2 张 · 8 页 · 3 大题」），没填就是空串。</summary>
    [ObservableProperty]
    private string _paperInfo = string.Empty;

    [ObservableProperty]
    private bool _hasPaperInfo;

    public void UpdateStatus(string statusText, ExamStatus status, bool isNext)
    {
        StatusText = statusText;
        IsUpcoming = status == ExamStatus.Upcoming;
        IsInProgress = status == ExamStatus.InProgress;
        IsFinished = status == ExamStatus.Finished;
        IsNext = isNext;

        if (HasPaperInfo)
            return;

        var paperInfo = ExamTimeFormatter.FormatPaperInfo(Exam);
        PaperInfo = paperInfo;
        HasPaperInfo = !string.IsNullOrEmpty(paperInfo);
    }
}
