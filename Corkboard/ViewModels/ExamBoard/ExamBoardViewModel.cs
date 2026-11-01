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
using Corkboard.Core.Icons;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Services.Fonts;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Time;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.ExamBoard;

/// <summary>
///     全屏考试看板的 ViewModel：时钟、考试表格、标题副标题，以及「这一刻该显示什么」。
///     <para>
///         显示形态由**下一场考试的状态**决定（<see cref="ExamStatusPolicy" />）：
///         未开始 → 时钟 + 全部考试；即将开始 / 进行中 → 默认只留「时钟 + 下面那组信息」（两个开关可改） 。
///         表格与信息块用同一套控件，靠 <see cref="ShowList" /> / <see cref="ShowExamInfo" /> 与
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

    /// <summary>
    ///     列表形态下时钟的字号 / 只剩时钟时的字号：这两个值来自设置（「看板字号」页），
    ///     <see cref="RefreshFontSizes" /> 负责把它们从配置搬过来。
    /// </summary>
    private double ClockSizeWithList = ExamBoardTextStyle.DefaultClockFontSize;

    /// <inheritdoc cref="ClockSizeWithList" />
    private double ClockSizeAlone = ExamBoardTextStyle.DefaultClockAloneFontSize;

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

    /// <summary>
    ///     这一刻那颗角标是不是「最小化」——每秒重算一次（考试状态会随时间翻面），
    ///     值变了才通知界面。见 <see cref="ResolveCanMinimize" />。
    /// </summary>
    private bool _canMinimize;

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

    [ObservableProperty]
    private double _clockFontSize = ExamBoardTextStyle.DefaultClockFontSize;

    /// <summary>
    ///     数字的等宽特性（<c>tnum</c>）：时钟、考试信息里的时间与倒计时、表格的时间列都绑它。
    ///     只在界面字体是默认的 MiSans 时给值（见 <see cref="TabularFigures" />），用户自选字体时留空。
    ///     时钟每秒刷新、倒计时最后一小时每秒刷新，比例宽度的数字会让整块字左右跳；表格那列靠它上下对齐。
    /// </summary>
    [ObservableProperty] private FontFeatureCollection? _numericFontFeatures;

    // ── 字号：全部照抄设置页「看板字号」那一页里的数（ExamBoardTextStyle） ──
    // 每样字都单独给一份，窗口里**一律绑这几个属性**，别在 XAML 里写死 FontSize——
    // 写死的那个用户就改不动了（这次的活儿就是「所有地方的字体都要能设置字号」）。
    // 默认值只用来占位：真正的值在 RefreshFontSizes() 里从配置读。

    /// <summary>主标题（左上角）字号。</summary>
    [ObservableProperty]
    private double _titleFontSize = ExamBoardTextStyle.DefaultTitleFontSize;

    /// <summary>副标题字号。</summary>
    [ObservableProperty]
    private double _subtitleFontSize = ExamBoardTextStyle.DefaultSubtitleFontSize;

    /// <summary>时钟下面那行日期字号。</summary>
    [ObservableProperty]
    private double _clockDateFontSize = ExamBoardTextStyle.DefaultClockDateFontSize;

    /// <summary>考试信息块顶上那行小字（即将开始 / 进行中）字号。</summary>
    [ObservableProperty]
    private double _infoCaptionFontSize = ExamBoardTextStyle.DefaultInfoCaptionFontSize;

    /// <summary>考试信息块里的科目名字号。</summary>
    [ObservableProperty]
    private double _infoSubjectFontSize = ExamBoardTextStyle.DefaultInfoSubjectFontSize;

    /// <summary>考试信息块里的日期与时间区间字号。</summary>
    [ObservableProperty]
    private double _infoTimeFontSize = ExamBoardTextStyle.DefaultInfoTimeFontSize;

    /// <summary>「还剩 40 分钟」那行字号。</summary>
    [ObservableProperty]
    private double _infoRemainingFontSize = ExamBoardTextStyle.DefaultInfoRemainingFontSize;

    /// <summary>卷面信息（几张几页）那行字号。</summary>
    [ObservableProperty]
    private double _infoPaperFontSize = ExamBoardTextStyle.DefaultInfoPaperFontSize;

    /// <summary>表格表头字号。</summary>
    [ObservableProperty]
    private double _tableHeaderFontSize = ExamBoardTextStyle.DefaultTableHeaderFontSize;

    /// <summary>表格正文（日期 / 时间 / 状态）字号。</summary>
    [ObservableProperty]
    private double _tableFontSize = ExamBoardTextStyle.DefaultTableFontSize;

    /// <summary>表格里科目那一列的字号（比正文大一档）。</summary>
    [ObservableProperty]
    private double _tableSubjectFontSize = ExamBoardTextStyle.DefaultTableSubjectFontSize;

    /// <summary>「还没有考试安排」那句的字号。</summary>
    [ObservableProperty]
    private double _emptyFontSize = ExamBoardTextStyle.DefaultEmptyFontSize;

    /// <summary>「还没有考试安排」下面那句提示的字号。</summary>
    [ObservableProperty]
    private double _emptyHintFontSize = ExamBoardTextStyle.DefaultEmptyHintFontSize;

    /// <summary>左下角长按退出那颗图标的大小（看板上唯一一处字形，同样跟着设置页走）。</summary>
    [ObservableProperty]
    private double _exitIconSize = ExamBoardTextStyle.DefaultExitIconSize;

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

    /// <summary>
    ///     是否显示时钟下面那组考试信息（列表收起时才出现）：这一刻考哪科、几点到几点、
    ///     还剩多久、卷面信息。
    /// </summary>
    [ObservableProperty]
    private bool _showExamInfo;

    [ObservableProperty]
    private bool _hasExams;

    [ObservableProperty]
    private bool _isTableHeaderVisible;

    /// <summary>信息块顶上那行小字：复用三档状态名（即将开始 / 进行中）。</summary>
    [ObservableProperty]
    private string _infoCaption = string.Empty;

    [ObservableProperty]
    private string _infoSubject = string.Empty;

    /// <summary>考试日期：今天 / 明天 / 后天 / 「11月7日 周六」，跟表格里那列同一套说法。</summary>
    [ObservableProperty]
    private string _infoDateText = string.Empty;

    [ObservableProperty]
    private string _infoTimeRange = string.Empty;

    /// <summary>「还有 12 分钟」/「还剩 40 分钟」/「还有 13 天」。</summary>
    [ObservableProperty]
    private string _infoRemaining = string.Empty;

    /// <summary>这一行有没有内容（未开始 / 已结束时为空，那一行整行隐藏）。</summary>
    [ObservableProperty]
    private bool _infoHasRemaining;

    /// <summary>剩余时间到了设置里那个门槛：这一行转红。</summary>
    [ObservableProperty]
    private bool _infoIsUrgent;

    [ObservableProperty]
    private string _infoPaperInfo = string.Empty;

    [ObservableProperty]
    private bool _infoHasPaperInfo;

    /// <summary>正在长按退出：进度圈与轨道这时才画出来，平时只有一颗干净的图标。</summary>
    [ObservableProperty]
    private bool _isHolding;

    /// <summary>进度圈的扫过角度（0–360），由按住时长线性推进。</summary>
    [ObservableProperty]
    private double _holdSweepAngle;

    /// <summary>进度圈的颜色：随进度由蓝渐变到红（见 <see cref="HoldRingStartColor" />）。</summary>
    public IBrush HoldRingBrush => _holdRingBrush;

    /// <summary>
    ///     左下角那颗按钮现在画什么图标：**此刻**这一档允许最小化就画最小化那条横线，否则还是退出箭头。
    ///     看板窗口里绑 <c>Text="{Binding CornerGlyph}"</c>，不要写死 <c>x:Static</c>。
    /// </summary>
    public string CornerGlyph => CanMinimizeNow
        ? FluentIcons.ArrowMinimizeFilled
        : FluentIcons.ArrowExitFilled;

    /// <summary>这颗按钮的悬停提示：跟着当前手势走，用户不用去设置页猜怎么用。</summary>
    public string CornerToolTip => CanMinimizeNow
        ? CornerClickMinimizes
            ? CR.Exam_Board_MinimizeClickToolTip
            : CR.Exam_Board_MinimizeHoldToolTip
        : CR.Exam_Board_ExitToolTip;

    /// <summary>「单击就最小化」这一档：手势是单击、而且此刻确实允许最小化。</summary>
    public bool CornerClickMinimizes =>
        CanMinimizeNow && Settings.MinimizeGesture == ExamBoardMinimizeGesture.Click;

    /// <summary>
    ///     此刻那颗按钮是不是「最小化」：由 <see cref="ExamBoardSettingsConfig.MinimizeMode" /> 与考试状态一起决定
    ///     （默认只在**考试进行中**才是最小化，平时只是退出）。每秒随 <see cref="RefreshNow" /> 重算一次，
    ///     所以开考那一刻图标会自己从退出箭头变成最小化。
    /// </summary>
    private bool CanMinimizeNow => _canMinimize;

    /// <summary>长按走满一圈：窗口该关了（code-behind 收到就 <c>Close()</c>）。</summary>
    public event EventHandler? ExitRequested;

    /// <summary>单击了那颗按钮（或按满设置里的手势时长）：窗口该收起来、换成浮窗按钮。</summary>
    public event EventHandler? MinimizeRequested;

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

    /// <summary>
    ///     每帧推进进度圈；走到 360 度就收尾——手势是「长按最小化」时收起来，否则照旧退出。
    /// </summary>
    private void OnHoldTick()
    {
        var progress = Math.Clamp(_holdWatch.Elapsed / HoldToExitDuration, 0d, 1d);

        HoldSweepAngle = progress * 360;
        _holdRingBrush.Color = LerpColor(HoldRingStartColor, HoldRingEndColor, progress);

        if (progress < 1)
            return;

        CancelHold();

        // 长按这一档在「最小化」和「退出」之间二选一：**此刻**允许最小化（默认就是考试进行中）
        // 且手势是长按时，满 2 秒就收起来，退出改到浮窗按钮上去（见 ExamBoardFloatingWindow）。
        if (CanMinimizeNow && Settings.MinimizeGesture == ExamBoardMinimizeGesture.Hold)
            MinimizeRequested?.Invoke(this, EventArgs.Empty);
        else
            ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     左下角那颗按钮松手：长按**没**走满就算一次「单击」——开了单击最小化就发
    ///     <see cref="MinimizeRequested" />；走满 2 秒那条路由 <see cref="OnHoldTick" /> 直接发退出。
    ///     <para>
    ///         手势是「长按最小化」时，单击什么都不做：那颗按钮这时等于第二颗退出键，
    ///         划一下、碰一下都不该把看板收起来。
    ///     </para>
    /// </summary>
    public void FinishCornerPress()
    {
        var clicked = IsHolding && _holdWatch.Elapsed < HoldToExitDuration;
        CancelHold();

        if (clicked && CornerClickMinimizes)
            MinimizeRequested?.Invoke(this, EventArgs.Empty);
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

        RefreshCorner(force: true);
        RefreshFontSizes();
    }

    /// <summary>
    ///     左下角那颗按钮的图标与提示语跟着「此刻能不能最小化」「用哪种手势」变，
    ///     这几个都是现算属性（不落字段），所以值一变就得挨个发通知。
    ///     <para>
    ///         每秒都会调一次（考试状态会随时间翻面）；<paramref name="force" /> 为假时值没变就直接返回，
    ///         免得每秒给绑定刷一遍。
    ///     </para>
    /// </summary>
    private void RefreshCorner(bool force = false)
    {
        var canMinimize = ResolveCanMinimize(_time.NowLocal);
        if (!force && canMinimize == _canMinimize)
            return;

        _canMinimize = canMinimize;

        OnPropertyChanged(nameof(CornerGlyph));
        OnPropertyChanged(nameof(CornerToolTip));
        OnPropertyChanged(nameof(CornerClickMinimizes));
    }

    /// <summary>
    ///     此刻那颗角标该不该是「最小化」。档位由 <see cref="ExamBoardSettingsConfig.MinimizeMode" /> 决定：
    ///     关掉 → 永远退出；「一直」→ 永远最小化；另外两档看**有没有正在考的、有没有快开考的**
    ///     （状态一律问 <see cref="ExamStatusPolicy" />，别在这儿自己比时间）。
    /// </summary>
    private bool ResolveCanMinimize(DateTime now)
    {
        var mode = Settings.MinimizeMode;
        return mode switch
        {
            ExamBoardMinimizeMode.Off => false,
            ExamBoardMinimizeMode.Always => true,
            ExamBoardMinimizeMode.InProgress => HasExamInStatus(now, ExamStatus.InProgress),
            ExamBoardMinimizeMode.InProgressOrUpcoming =>
                HasExamInStatus(now, ExamStatus.InProgress) || HasExamInStatus(now, ExamStatus.Upcoming),
            _ => false
        };
    }

    /// <summary>整块看板里有没有一场考试正处在指定状态（阈值与看板显示页是同一个）。</summary>
    private bool HasExamInStatus(DateTime now, ExamStatus status)
    {
        var threshold = ExamStatusPolicy.ClampUpcomingMinutes(Settings.UpcomingThresholdMinutes);
        foreach (var exam in Exams.Exams)
        {
            if (ExamStatusPolicy.Resolve(exam, now, threshold) == status)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     把配置里那一组字号搬到窗口绑的这批属性上。
    ///     <para>
    ///         走 <see cref="ExamBoardTextStyle.Clamp" /> / <see cref="ExamBoardTextStyle.ClampClock" />，
    ///         与设置页输入框用的是同一个夹取函数——配置文件被手改坏了也不会画出个 9999 号的字。
    ///     </para>
    /// </summary>
    private void RefreshFontSizes()
    {
        // 等宽数字只给默认字体（MiSans 里有 tnum）：用户自选的字体未必有，硬塞会画得更难看。
        // 时钟、时间区间、倒计时、表格时间列用的是同一份特性对象（见 NumericFontFeatures）。
        NumericFontFeatures = TabularFigures.ForNumbers(Config.Appearance.Font);

        TitleFontSize = ExamBoardTextStyle.Clamp(Settings.TitleFontSize, ExamBoardTextStyle.DefaultTitleFontSize);
        SubtitleFontSize = ExamBoardTextStyle.Clamp(Settings.SubtitleFontSize,
            ExamBoardTextStyle.DefaultSubtitleFontSize);
        ClockDateFontSize = ExamBoardTextStyle.Clamp(Settings.ClockDateFontSize,
            ExamBoardTextStyle.DefaultClockDateFontSize);
        InfoCaptionFontSize = ExamBoardTextStyle.Clamp(Settings.InfoCaptionFontSize,
            ExamBoardTextStyle.DefaultInfoCaptionFontSize);
        InfoSubjectFontSize = ExamBoardTextStyle.Clamp(Settings.InfoSubjectFontSize,
            ExamBoardTextStyle.DefaultInfoSubjectFontSize);
        InfoTimeFontSize = ExamBoardTextStyle.Clamp(Settings.InfoTimeFontSize,
            ExamBoardTextStyle.DefaultInfoTimeFontSize);
        InfoRemainingFontSize = ExamBoardTextStyle.Clamp(Settings.InfoRemainingFontSize,
            ExamBoardTextStyle.DefaultInfoRemainingFontSize);
        InfoPaperFontSize = ExamBoardTextStyle.Clamp(Settings.InfoPaperFontSize,
            ExamBoardTextStyle.DefaultInfoPaperFontSize);
        TableHeaderFontSize = ExamBoardTextStyle.Clamp(Settings.TableHeaderFontSize,
            ExamBoardTextStyle.DefaultTableHeaderFontSize);
        TableFontSize = ExamBoardTextStyle.Clamp(Settings.TableFontSize, ExamBoardTextStyle.DefaultTableFontSize);
        TableSubjectFontSize = ExamBoardTextStyle.Clamp(Settings.TableSubjectFontSize,
            ExamBoardTextStyle.DefaultTableSubjectFontSize);
        EmptyFontSize = ExamBoardTextStyle.Clamp(Settings.EmptyFontSize, ExamBoardTextStyle.DefaultEmptyFontSize);
        EmptyHintFontSize = ExamBoardTextStyle.Clamp(Settings.EmptyHintFontSize,
            ExamBoardTextStyle.DefaultEmptyHintFontSize);
        ExitIconSize = ExamBoardTextStyle.Clamp(Settings.ExitIconSize, ExamBoardTextStyle.DefaultExitIconSize);

        // 时钟那两档在上限上跟别的字不是一个量级（480），必须走 ClampClock。
        ClockSizeWithList = ExamBoardTextStyle.ClampClock(Settings.ClockFontSize,
            ExamBoardTextStyle.DefaultClockFontSize);
        ClockSizeAlone = ExamBoardTextStyle.ClampClock(Settings.ClockAloneFontSize,
            ExamBoardTextStyle.DefaultClockAloneFontSize);
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

        var threshold = ExamStatusPolicy.ClampUpcomingMinutes(Settings.UpcomingThresholdMinutes);
        var next = ExamStatusPolicy.FindNext(Exams.Exams, now);
        var nextStatus = next is null ? ExamStatus.Finished : ExamStatusPolicy.Resolve(next, now, threshold);

        // 两种形态：整表 / 只剩下「时钟 + 下面那组信息」。
        // 列表收起时不再另开一张卡，统一由时钟下面那组字承载（科目、时间、剩余、卷面）。
        var showList = nextStatus switch
        {
            ExamStatus.InProgress => Settings.ShowExamListInProgress,
            ExamStatus.Upcoming => !Settings.HideExamListWhenUpcoming,
            _ => true
        };

        ShowList = showList;
        ClockColumnSpan = showList ? 1 : 2;
        ClockFontSize = showList ? ClockSizeWithList : ClockSizeAlone;

        HasExams = Exams.Exams.Count > 0;
        IsTableHeaderVisible = showList && Rows.Count > 0;

        UpdateExamInfo(next, nextStatus, now);

        UpdateRowStatuses(now, threshold);

        // 角标「该不该是最小化」也随时间变（开考 / 收卷那一刻翻面），跟着这一拍重算。
        RefreshCorner();
    }

    /// <summary>
    ///     时钟下面那组信息（**始终独立存在的一块**，不并进右边那张总览表）：
    ///     正在考 / 下一场是哪一科、哪天几点到几点、还剩多久（到了设置里那个门槛就转红），
    ///     以及卷面信息（填了才显示）。
    ///     <para>
    ///         表格收起时它是屏幕上唯一的考试信息；表格展开时它仍然单独待着（左边时钟下面），
    ///         只是与表里那行重复——用户明确要的是「单独一块」，不要跟总览混在一起。
    ///     </para>
    ///     <para>
    ///         只读 <see cref="ExamStatusPolicy" /> 与 <see cref="ExamTimeFormatter" /> 的结果，
    ///         这里不自己算时间差——「还剩多久」与表格里那一句必须是同一个说法。
    ///     </para>
    /// </summary>
    private void UpdateExamInfo(ExamEntry? next, ExamStatus status, DateTime now)
    {
        if (next is null)
        {
            ShowExamInfo = false;
            InfoCaption = string.Empty;
            InfoSubject = string.Empty;
            InfoDateText = string.Empty;
            InfoTimeRange = string.Empty;
            InfoRemaining = string.Empty;
            InfoHasRemaining = false;
            InfoIsUrgent = false;
            InfoPaperInfo = string.Empty;
            InfoHasPaperInfo = false;
            return;
        }

        var paperInfo = ExamTimeFormatter.FormatPaperInfo(next);

        // 设置里关掉之后只剩一个大时钟（默认开：这组字就是看板的信息主体）。
        ShowExamInfo = Settings.ShowExamInfo;
        InfoCaption = ExamTimeFormatter.FormatInfoCaption(status);
        InfoSubject = next.Subject;
        InfoDateText = ExamTimeFormatter.FormatDate(next.Date, DateOnly.FromDateTime(now));
        InfoTimeRange = ExamTimeFormatter.FormatTimeRange(next);
        InfoRemaining = ExamTimeFormatter.FormatInfoRemaining(status, next, now, Settings.RemainingSecondsEnabled);
        InfoHasRemaining = InfoRemaining.Length > 0;
        InfoIsUrgent = ExamStatusPolicy.IsRemainingUrgent(status, next, now, Settings.RemainingWarningMinutes);
        InfoPaperInfo = paperInfo;
        InfoHasPaperInfo = paperInfo.Length > 0;
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
            row.UpdateStatus(ExamTimeFormatter.FormatStatus(status), status, row.Exam.Id == nextId);
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
        // 时钟摆哪边、时钟那两档字号、以及（通过 RefreshNow 重算的）阈值与两个开关。
        // 字号一律整组重搬一次（RefreshFontSizes 很便宜，且改了任一档都可能连锁夹取），
        // 顺手把 ClockFontSize 也重算——它由 RefreshNow 按当前形态从这两档里挑一个。
        if (e.PropertyName is null or nameof(ExamBoardSettingsConfig.ClockSide))
            RefreshStatic();
        else
            RefreshFontSizes();

        // 「什么时候最小化 / 手势」也在这条总线上：它不是字号也不是时钟位置，单独刷一次角标
        // （手势变了但「此刻能不能最小化」没变，所以这里必须强制刷）。
        RefreshCorner(force: true);
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

    public void UpdateStatus(string statusText, ExamStatus status, bool isNext)
    {
        StatusText = statusText;
        IsUpcoming = status == ExamStatus.Upcoming;
        IsInProgress = status == ExamStatus.InProgress;
        IsFinished = status == ExamStatus.Finished;
        IsNext = isNext;
    }
}
