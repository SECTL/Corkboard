using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Exam;
using Corkboard.Core.Models.SubConfigs.Display;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Services.Fonts;
using Corkboard.Core.Display;
using Corkboard.Core.Time;
using Corkboard.Core.Widgets.Quote;
using Corkboard.Core.Widgets.Weather;
using Corkboard.Services.Widgets;

namespace Corkboard.ViewModels.DisplayBoard;

/// <summary>
///     全屏看板模式的 ViewModel：大时钟 + 作业板，加上「长按退出」和「光标自己藏起来」。
///     <para>
///         时钟<b>固定在左上角</b>（用户要求「时间的显示位置不能改」），只占顶栏的第一格，
///         作业从顶栏下面一直铺到底，所以字号不用多大就看得清，
///         也不会出现「顶上空一大片、作业挤成一条缝」。
///     </para>
///     <para>
///         时钟每秒走一次（<see cref="NtpTimeService.NowLocal" />，与考试看板、自动进出判定同源）。
///         本类是 transient、配置与时间是单例，订阅必须在窗口关闭时断开
///         （<see cref="Attach" /> / <see cref="Detach" /> 由窗口 code-behind 调）。
///     </para>
/// </summary>
public partial class DisplayBoardViewModel : ViewModelBase
{
    /// <summary>时钟心跳。1 秒是秒针的自然粒度，再密只是白烧 CPU。</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    /// <summary>长按退出的判定时长：进度圈走满一圈才真的关掉全屏。</summary>
    private static readonly TimeSpan HoldToExitDuration = TimeSpan.FromSeconds(2);

    /// <summary>进度圈的重绘间隔：约 60 fps，两秒 120 帧，顺滑又不费。</summary>
    private static readonly TimeSpan HoldFrameInterval = TimeSpan.FromMilliseconds(16);

    /// <summary>进度圈起点色（蓝）：刚按下，还没什么危险。</summary>
    private static readonly Color HoldRingStartColor = Color.Parse("#0078D4");

    /// <summary>进度圈终点色（红）：走满了就要退了，颜色得先提醒到。</summary>
    private static readonly Color HoldRingEndColor = Color.Parse("#E81123");

    /// <summary>光标藏起来之后的那一份（不画任何形状）。</summary>
    private static readonly Cursor HiddenCursor = new(StandardCursorType.None);

    /// <summary>一言 / 天气多久问一次服务（服务自己还会按配置判新鲜度，这里只负责「醒一下」）。</summary>
    private static readonly TimeSpan WidgetPollInterval = TimeSpan.FromMinutes(1);

    private readonly NtpTimeService _time;
    private readonly QuoteService _quoteService;
    private readonly WeatherService _weatherService;
    private readonly IExamService _examService;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _holdTimer;
    private readonly DispatcherTimer _cursorTimer;
    private readonly DispatcherTimer _widgetTimer;
    private readonly Stopwatch _holdWatch = new();

    /// <summary>窗口关掉时用它掐掉还在路上的「一言 / 天气」请求。</summary>
    private CancellationTokenSource? _widgetCancellation;

    /// <summary>最近一次拿到的那份天气：改「显示哪几块」时靠它重算，不必为改配置再请求一次。</summary>
    private WeatherSnapshot? _lastWeather;

    /// <summary>
    ///     最近一次拿到的那条一言：改「显示来源 / 显示作者」时靠它重拼底下那行出处，
    ///     同样不必为改一个开关再请求一次接口。
    /// </summary>
    private HitokotoQuote _lastQuote = HitokotoQuote.Empty;

    /// <summary>天气图标按文件名缓存（同一张图只解码一次；单例式静态表，窗口开开关关共用）。</summary>
    private static readonly Dictionary<string, Bitmap> WeatherIconCache = new(StringComparer.Ordinal);

    /// <summary>进度圈的颜色笔刷：一个实例反复改 <see cref="SolidColorBrush.Color" />，不每帧新建对象。</summary>
    private readonly SolidColorBrush _holdRingBrush = new(HoldRingStartColor);

    private bool _isAttached;

    public DisplayBoardViewModel(MainConfigHandler configHandler, NtpTimeService time,
        QuoteService quoteService, WeatherService weatherService, IExamService examService)
        : base(configHandler)
    {
        _time = time;
        _quoteService = quoteService;
        _weatherService = weatherService;
        _examService = examService;

        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += (_, _) => Tick();

        _holdTimer = new DispatcherTimer { Interval = HoldFrameInterval };
        _holdTimer.Tick += (_, _) => OnHoldTick();

        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _cursorTimer.Tick += (_, _) => OnCursorIdleTick();

        _widgetTimer = new DispatcherTimer { Interval = WidgetPollInterval };
        _widgetTimer.Tick += (_, _) => RefreshWidgets(forceQuote: false);

        RefreshStatic();
        RefreshNow();
    }

    /// <summary>看板模式的配置段（设置页改的就是它，窗口开着时实时生效）。</summary>
    public DisplaySettingsConfig Settings => Config.Display;

    // ── 时钟 ─────────────────────────────────────────────────

    /// <summary>
    ///     主时间那一截：「21:42」/「21:42:07」/「21:42:」。
    ///     <para>
    ///         「大秒」那一档（默认）里，冒号与秒都在这一串里——整块钟一次画出来，
    ///         跟考试看板上的写法完全一样；只有「小秒」那一档才只留到冒号，秒另起一只尾巴。
    ///     </para>
    /// </summary>
    [ObservableProperty] private string _clockMain = string.Empty;

    /// <summary>「小秒」那一档里冒号后面那两位「07」：字号缩到四成左右，当主时间的尾巴。</summary>
    [ObservableProperty] private string _clockSeconds = string.Empty;

    /// <summary>画不画那只小尾巴（只有「小秒」那一档才画；「大秒」时秒已经在主时间那一串里）。</summary>
    [ObservableProperty] private bool _showClockSecondsTail;

    [ObservableProperty] private string _clockDate = string.Empty;

    /// <summary>12 小时制时那颗「上午 / 下午」（AM / PM）；24 小时制是空串，界面上不显示。</summary>
    [ObservableProperty] private string _clockPeriod = string.Empty;

    /// <summary>要不要画那颗「上午 / 下午」。</summary>
    [ObservableProperty] private bool _showClockPeriod;

    /// <summary>时钟字号（夹取过，窗口直接绑）。</summary>
    [ObservableProperty] private double _clockFontSize = DisplayTextStyle.DefaultClockFontSize;

    /// <summary>秒数那一截的字号：跟着主时钟字号按固定比例缩。</summary>
    [ObservableProperty] private double _clockSecondsFontSize =
        DisplayTextStyle.ClockSecondsFontSize(DisplayTextStyle.DefaultClockFontSize);

    /// <summary>
    ///     秒数那截往下压多少：数字字形底边比基线低一截，不压就会看着像整体掉下去。
    ///     跟着字号按比例算，改字号时不用再调一遍。
    /// </summary>
    [ObservableProperty] private Thickness _clockSecondsMargin;

    /// <summary>
    ///     时钟那一块最多能有多宽（DIP）。由窗口在尺寸变化时写进来。
    ///     <para>
    ///         ViewModel 拿不到窗口尺寸，而「480 号的 21:42」比整块屏幕还宽——没有这个上限，
    ///         Viewbox 会按无限宽度量内容、于是永不缩小，时钟被窗口裁成「21:4…」。
    ///     </para>
    /// </summary>
    [ObservableProperty] private double _clockMaxWidth = double.PositiveInfinity;

    /// <summary>日期字号（夹取过）。</summary>
    [ObservableProperty] private double _clockDateFontSize = DisplayTextStyle.DefaultClockDateFontSize;

    /// <summary>左下角长按退出那颗图标的大小（夹取过）。</summary>
    [ObservableProperty] private double _exitIconSize = DisplayTextStyle.DefaultExitIconSize;

    // ── 考试倒计时 ───────────────────────────────────────────

    /// <summary>
    ///     日期下面那一行「还有几天考试」画不画：配置想不想看 × 这一大场还没开考。
    /// </summary>
    /// <remarks>
    ///     内容从考试看板那块板子来（<see cref="IExamService.Exams" /> + <see cref="IExamService.ActiveBoard" /> 的标题），
    ///     与全屏考试看板读同一份、同一个 <see cref="ExamStatusPolicy" /> 判定，
    ///     所以这边说「还有 19 天」那边不会说「18 天」。
    ///     <para>
    ///         放在时钟那一格里、<b>日期下面</b>：顶栏右上那格是天气，塞进去两行就挤成一团了
    ///         （用户明确要求放日期下方）。
    ///     </para>
    /// </remarks>
    [ObservableProperty] private bool _showExamCountdown;

    /// <summary>那一行字本身，例如「贵州省贵阳市2027届高三年级第二次统考 · 还有 19 天」。</summary>
    [ObservableProperty] private string _examCountdownText = string.Empty;

    /// <summary>考试倒计时那一行的字号（夹取在 <see cref="DisplayTextStyle" /> 里，与天气/一言各自一档）。</summary>
    [ObservableProperty] private double _examCountdownFontSize = DisplayTextStyle.DefaultExamCountdownFontSize;

    // ── 摆位与显隐 ───────────────────────────────────────────
    // 作业区顶上那行小标题（「今天的作业」）在看板模式里**不再显示**：右上角留给天气，
    // 标题跟它挤同一行只会互相打架。资源键 Display_Board_Heading 仍留着（作业板主界面在用），别删。

    /// <summary>画不画时钟那一块（关掉「显示大时钟」时为假）。</summary>
    [ObservableProperty] private bool _showClock = true;

    /// <summary>画不画日期那行。</summary>
    [ObservableProperty] private bool _showDate = true;

    /// <summary>画不画作业区（关掉之后整屏就剩左上角那块大时钟）。</summary>
    [ObservableProperty] private bool _showBoard = true;

    /// <summary>
    ///     「时钟」和「作业」都被关掉了：屏幕上什么都不剩。
    ///     <para>
    ///         以前这种情况就是一块纯空白的全屏，用户以为软件卡了。现在改成一句提示
    ///         （「时钟和作业都关掉了，去设置里开一个」），至少知道该去哪儿改。
    ///     </para>
    /// </summary>
    [ObservableProperty] private bool _showEmptyState;

    /// <summary>屏幕上那句「时钟和作业都关掉了」。</summary>
    public string EmptyStateText => Core.Langs.Common.Resources.Display_EmptyState;

    /// <summary>
    ///     作业被锁住了（「允许编辑作业」与「允许布置作业」都关着）：挂一颗小锁，
    ///     点作业会提示已锁定，不再像以前那样点了毫无反应。
    ///     <para>
    ///         画在作业区右下角，跟 <see cref="ShowAssignButton" /> 不是同一处：
    ///         那颗加号在窗口角落、跟长按退出排成一行（用户要求），
    ///         这里只在整屏都只读（改不让改、加也不让加）时才亮，补一句「为什么点不动」。
    ///     </para>
    /// </summary>
    [ObservableProperty] private bool _showLockIcon;

    /// <summary>那颗小锁的提示文案。</summary>
    public string LockTip => Core.Langs.Common.Resources.Display_LockedTip;

    /// <summary>
    ///     画不画那颗「布置作业」浮标：作业区开着、并且「允许布置作业」也开着。
    ///     <para>
    ///         看板模式以前只能改已经有的作业（卡片上的编辑按钮），要新布置一条得关掉全屏、
    ///         回去点主窗口的标题栏，正在展示这块屏的时候根本做不到。所以补一颗浮标，
    ///         点开就是同一个 <c>HomeworkAssignmentForm</c> 弹层。
    ///     </para>
    ///     <para>
    ///         位置在屏幕右下角、纵向跟左下角那颗长按退出对齐（用户要求：先「跟退出按钮排在同一行」，
    ///         再说「在同一行也不要靠左呀，靠右下角」）。别放回作业区那个 Grid 里——
    ///         作业区会随「只看今天到期」等过滤变宽变窄，角落按钮不该跟着跑，还容易跟滚动区抢指针。
    ///     </para>
    ///     <para>
    ///         ⚠️ 与「允许编辑作业」（<see cref="DisplaySettingsConfig.Editable" />）是<b>两个开关</b>：
    ///         那一项管改 / 删已有的作业，这一项管新增。教室里那块屏常常「可以加作业、但不许动别人的」。
    ///     </para>
    /// </summary>
    [ObservableProperty] private bool _showAssignButton;

    /// <summary>右下角那颗「布置作业」浮标的提示文案（与主窗口标题栏那颗复用同一个键）。</summary>
    public string AssignTip => Core.Langs.Common.Resources.Board_AddNote;

    /// <summary>
    ///     时钟贴左边时补的那点缩进——让大钟与下面那行日期的左边缘，跟作业卡片的左边缘对齐。
    ///     <para>
    ///         作业板页面自己的内容还带了一圈内边距（<c>BoardPage.axaml</c> 里那个
    ///         <c>Margin="12 0 12 12"</c>），所以光靠外层网格那 56 是差一截的，
    ///         看着就是「时间日期比作业往左戳出去一块」（用户反馈过）。
    ///     </para>
    ///     <para>
    ///         时钟固定左上角，所以这个值不会变；数在 <see cref="DisplayBoardMetrics.ClockLeftGutter" /> 里。
    ///     </para>
    /// </summary>
    [ObservableProperty] private Thickness _clockGutter;

    // 作业区永远在顶栏下面那一整行，跨满三列。
    [ObservableProperty] private int _boardRow = 1;
    [ObservableProperty] private int _boardRowSpan = 1;
    [ObservableProperty] private int _boardColumn;
    [ObservableProperty] private int _boardColumnSpan = 3;

    /// <summary>
    ///     秒数与主时间同字号（设置里的「大秒」）。开着时秒数连同冒号一起并进主时间那一串
    ///     （<see cref="DisplayClockText.Combined" />，一个 TextBlock 画完）；关掉时才回到字幕式的小尾巴。
    /// </summary>
    [ObservableProperty] private bool _bigSeconds = true;

    /// <summary>
    ///     时钟数字的等宽特性（<c>tnum</c>）：只在界面字体是默认的 MiSans 时给值，
    ///     用户自选了别的字体就交回 <see langword="null" />——那份字体里未必有 <c>tnum</c>，
    ///     硬塞进去等于用错字形表，比不走等宽更难看。
    /// </summary>
    [ObservableProperty] private FontFeatureCollection? _clockFontFeatures;

    // ── 一言与天气 ───────────────────────────────────────────

    /// <summary>画不画屏幕底部那一行一言（取到过内容才为真）。</summary>
    [ObservableProperty] private bool _showQuote;

    /// <summary>一言正文。</summary>
    [ObservableProperty] private string _quoteText = string.Empty;

    /// <summary>一言出处（「—— 出处 · 作者」）；没有出处时为空串，那一行不画。</summary>
    [ObservableProperty] private string _quoteMeta = string.Empty;

    /// <summary>画不画出处那一行。</summary>
    [ObservableProperty] private bool _showQuoteMeta;

    /// <summary>画不画右上角那块天气（取到过数据才为真）。</summary>
    [ObservableProperty] private bool _showWeather;

    /// <summary>
    ///     天气图标（随昼夜换日月）。解码失败或没有数据时为 <see langword="null" />，
    ///     那一格不画——少一张图标不该让整块天气消失。
    /// </summary>
    [ObservableProperty] private Bitmap? _weatherIcon;

    /// <summary>当前温度那一个大字，例如 <c>9°</c>。</summary>
    [ObservableProperty] private string _weatherTemperatureText = string.Empty;

    /// <summary>天气现象，例如 <c>多云</c>。</summary>
    [ObservableProperty] private string _weatherConditionText = string.Empty;

    /// <summary>城市名，例如 <c>北京</c>。</summary>
    [ObservableProperty] private string _weatherCityText = string.Empty;

    /// <summary>今天高低，例如 <c>今天 3–17°</c>。</summary>
    [ObservableProperty] private string _weatherRangeText = string.Empty;

    /// <summary>湿度，例如 <c>湿度 81%</c>。</summary>
    [ObservableProperty] private string _weatherHumidityText = string.Empty;

    /// <summary>空气质量，例如 <c>空气 优</c>。</summary>
    [ObservableProperty] private string _weatherAqiText = string.Empty;

    /// <summary>体感温度，例如 <c>体感 7°</c>。</summary>
    [ObservableProperty] private string _weatherFeelsLikeText = string.Empty;

    /// <summary>明天那一块，例如 <c>明天 小雨 4–12°</c>。</summary>
    [ObservableProperty] private string _weatherTomorrowText = string.Empty;

    // 每一块各自的显隐 = 配置里勾了 + 这一块真的有内容（见 RefreshStatic）。
    [ObservableProperty] private bool _showWeatherIcon;
    [ObservableProperty] private bool _showWeatherTemperature;
    [ObservableProperty] private bool _showWeatherCondition;
    [ObservableProperty] private bool _showWeatherCity;
    [ObservableProperty] private bool _showWeatherRange;
    [ObservableProperty] private bool _showWeatherHumidity;
    [ObservableProperty] private bool _showWeatherAqi;
    [ObservableProperty] private bool _showWeatherFeelsLike;
    [ObservableProperty] private bool _showWeatherTomorrow;

    /// <summary>温度下面那一排小字里有没有东西（一个都没有就整排不画，不留一条空行）。</summary>
    [ObservableProperty] private bool _showWeatherFacts;

    /// <summary>天气那一行的字号（设置里的「天气字号」）：温度大字，图标与下面几行小字都由它折算。</summary>
    [ObservableProperty] private double _weatherFontSize = DisplayTextStyle.DefaultWeatherFontSize;

    /// <summary>一言的字号（设置里的「一言字号」）。</summary>
    [ObservableProperty] private double _quoteFontSize = DisplayTextStyle.DefaultQuoteFontSize;

    /// <summary>天气图标的边长：按天气字号折算，一眼看得出是太阳还是雨。</summary>
    [ObservableProperty] private double _weatherIconSize = DisplayTextStyle.MinWeatherIconSize;

    /// <summary>天气现象（「多云」）的字号：比温度大字小一档，又比底下一排小字大。</summary>
    [ObservableProperty]
    private double _weatherConditionFontSize = DisplayTextStyle.MinWeatherConditionFontSize;

    /// <summary>城市 / 今天高低 / 湿度 / 空气那一排小字的字号。</summary>
    [ObservableProperty]
    private double _weatherFactFontSize = DisplayTextStyle.MinWeatherFactFontSize;

    // ── 光标 ─────────────────────────────────────────────────

    /// <summary>
    ///     窗口当前该用的光标。停着不动到点之后换成「不画」，动一下就还回去。
    ///     <para>
    ///         绑在窗口的 <c>Cursor</c> 上，不要在 code-behind 里另存一份状态——
    ///         两处各判一次就会「鼠标动了但光标没回来」。
    ///     </para>
    /// </summary>
    [ObservableProperty] private Cursor? _cursor;

    private DateTime _lastPointerMove = DateTime.UtcNow;

    // ── 长按退出 ──────────────────────────────────────────────

    /// <summary>手指 / 鼠标是否按在左下角那颗退出图标上。</summary>
    [ObservableProperty] private bool _isHolding;

    /// <summary>进度圈的扫过角度（0–360），由按住时长线性推进。</summary>
    [ObservableProperty] private double _holdSweepAngle;

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

    /// <summary>松手 / 失去指针捕获：进度归零，什么都不发生（误触不能关掉全屏）。</summary>
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

    // ── 光标自己藏起来 ────────────────────────────────────────

    /// <summary>窗口收到指针移动时调（移动即算「在用」，顺带把光标还回去）。</summary>
    public void NotifyPointerActivity()
    {
        _lastPointerMove = DateTime.UtcNow;

        if (Cursor is not null)
            Cursor = null;
    }

    /// <summary>每秒看一眼停多久了；到点就把光标换掉，0 表示从不隐藏。</summary>
    private void OnCursorIdleTick()
    {
        var delay = Settings.CursorHideDelay;
        if (delay <= TimeSpan.Zero)
        {
            if (Cursor is not null)
                Cursor = null;

            return;
        }

        // 按在退出图标上时不藏：用户正盯着那颗按钮呢。
        if (IsHolding)
        {
            _lastPointerMove = DateTime.UtcNow;
            return;
        }

        if (Cursor is null && DateTime.UtcNow - _lastPointerMove >= delay)
            Cursor = HiddenCursor;
    }

    // ── 生命周期 ──────────────────────────────────────────────

    /// <summary>挂上订阅并开始走钟（窗口打开时调）。重复调用不会重复挂。</summary>
    public void Attach()
    {
        if (_isAttached)
            return;

        _isAttached = true;
        Settings.PropertyChanged += OnSettingsChanged;
        _time.StatusChanged += OnTimeStatusChanged;
        Config.Widgets.PropertyChanged += OnWidgetsSettingsChanged;
        _examService.Changed += OnExamsChanged;

        _timer.Start();
        _cursorTimer.Start();
        _widgetTimer.Start();
        NotifyPointerActivity();

        RefreshStatic();
        RefreshNow();

        // 打开看板就取一次：一言按配置可能是「每次打开都换」，天气则只在过期时才真的发请求
        // （两个服务自己判新鲜度，这里只管把它们叫醒）。
        RefreshWidgets(forceQuote: true);
    }

    /// <summary>断开订阅、停掉计时器（窗口关闭时调）。</summary>
    public void Detach()
    {
        if (!_isAttached)
            return;

        _isAttached = false;
        _timer.Stop();
        _cursorTimer.Stop();
        _widgetTimer.Stop();
        CancelHold();
        Settings.PropertyChanged -= OnSettingsChanged;
        Config.Widgets.PropertyChanged -= OnWidgetsSettingsChanged;
        _time.StatusChanged -= OnTimeStatusChanged;
        _examService.Changed -= OnExamsChanged;

        // 窗口都关了，还在路上的请求没意义：直接掐掉，免得回来再改一批已经没人看的属性。
        _widgetCancellation?.Cancel();
        _widgetCancellation?.Dispose();
        _widgetCancellation = null;

        Cursor = null;
    }

    /// <summary>每秒一次：只更新几个字符串。</summary>
    private void Tick() => RefreshNow();

    /// <summary>开关、字号这类「只有配置变了才动」的东西。</summary>
    private void RefreshStatic()
    {
        ShowClock = Settings.ShowClock;
        ShowDate = Settings.ShowDate;
        ShowBoard = Settings.ShowBoard;

        // 两个开关都关掉时屏幕上什么都剩不下——给一句提示，别留一块纯空白。
        ShowEmptyState = Settings.EverythingHidden;

        // 右下角那一格：能布置就挂「布置作业」，改不让改、加也不让加（整屏只读）才挂小锁——两个不会同时出现。
        ShowAssignButton = ShowBoard && Settings.AllowAssign;
        ShowLockIcon = ShowBoard && !Settings.Editable && !Settings.AllowAssign;

        // 「大秒」只在真的显示秒数时才有意义（关掉秒数时那一段本来就不画）。
        BigSeconds = Settings.BigSeconds && Settings.ShowSeconds;

        // 等宽数字只给默认字体（MiSans 里有 tnum）；用户自选的字体未必有。
        ClockFontFeatures = TabularFigures.ForClock(Config.Appearance.Font);

        var widgets = Config.Widgets;

        // 「显示来源 / 显示作者」只改展示：拿手里那条句子按新开关重拼一遍，不重新请求。
        if (!string.IsNullOrEmpty(QuoteText))
            QuoteMeta = BuildQuoteMeta(_lastQuote, widgets.QuoteShowSource, widgets.QuoteShowAuthor);

        ShowQuote = widgets.QuoteEnabled && !string.IsNullOrEmpty(QuoteText);
        ShowQuoteMeta = ShowQuote && !string.IsNullOrEmpty(QuoteMeta);

        // 天气是一块一块的：开关决定「想不想看」，文本非空决定「有没有得看」。
        ShowWeather = widgets.WeatherEnabled && HasWeatherContent;
        ShowWeatherIcon = ShowWeather && widgets.WeatherShowIcon && WeatherIcon is not null;
        ShowWeatherTemperature = ShowWeather && widgets.WeatherShowTemperature && WeatherTemperatureText.Length > 0;
        ShowWeatherCondition = ShowWeather && widgets.WeatherShowCondition && WeatherConditionText.Length > 0;
        ShowWeatherCity = ShowWeather && widgets.WeatherShowCity && WeatherCityText.Length > 0;
        ShowWeatherRange = ShowWeather && widgets.WeatherShowRange && WeatherRangeText.Length > 0;
        ShowWeatherHumidity = ShowWeather && widgets.WeatherShowHumidity && WeatherHumidityText.Length > 0;
        ShowWeatherAqi = ShowWeather && widgets.WeatherShowAqi && WeatherAqiText.Length > 0;
        ShowWeatherFeelsLike = ShowWeather && widgets.WeatherShowFeelsLike && WeatherFeelsLikeText.Length > 0;
        ShowWeatherTomorrow = ShowWeather && widgets.WeatherShowTomorrow && WeatherTomorrowText.Length > 0;
        ShowWeatherFacts = ShowWeatherCity || ShowWeatherRange || ShowWeatherHumidity || ShowWeatherAqi
                           || ShowWeatherFeelsLike || ShowWeatherTomorrow;

        RefreshFontSizes();
        RefreshPlacement();
    }

    /// <summary>
    ///     把配置里那一组字号搬到窗口绑的这批属性上。
    ///     走设置页用的同一个夹取函数（配置被手改坏了也不会画出个 9999 号的字）。
    /// </summary>
    private void RefreshFontSizes()
    {
        ClockFontSize = Settings.ResolvedClockFontSize;

        // 「大秒」打开时秒数与主时间同字号、也不往下压；关掉才回到字幕式的小尾巴。
        var bigSeconds = BigSeconds;
        ClockSecondsFontSize = DisplayTextStyle.ClockSecondsFontSize(ClockFontSize, bigSeconds);
        ClockSecondsMargin = new Thickness(3, 0, 0,
            DisplayTextStyle.ClockSecondsBaselineOffset(ClockFontSize, bigSeconds));
        ClockDateFontSize = Settings.ResolvedClockDateFontSize;
        ExitIconSize = Settings.ResolvedExitIconSize;

        // 天气 / 一言是自己两项配置（不再跟着时钟字号走），夹取在 Core 里，有单测。
        WeatherFontSize = Settings.ResolvedWeatherFontSize;
        QuoteFontSize = Settings.ResolvedQuoteFontSize;

        // 考试倒计时那一行也是独立一档（默认与日期同号，上下限在 DisplayTextStyle 里）。
        ExamCountdownFontSize = Settings.ResolvedExamCountdownFontSize;

        // 天气那块里图标、现象、小字三档各自的比例也由 Core 算，别在 XAML 里写数字。
        WeatherIconSize = DisplayTextStyle.WeatherIconSize(WeatherFontSize);
        WeatherConditionFontSize = DisplayTextStyle.WeatherConditionFontSize(WeatherFontSize);
        WeatherFactFontSize = DisplayTextStyle.WeatherFactFontSize(WeatherFontSize);
    }

    /// <summary>
    ///     摆位：顶栏那点缩进 + 作业区的位置。
    ///     <para>
    ///         时钟**固定在左上角**——「时钟贴哪一角」那个版式选项已经整体下架
    ///         （用户要求「时间的显示位置不能改，只能显示到左上角」），
    ///         网格位置直接在 XAML 里写死（<c>Grid.Row="0" Grid.Column="0"</c>），不再走绑定。
    ///     </para>
    /// </summary>
    private void RefreshPlacement()
    {
        // 顶栏那块补一点左边距，让大钟与日期的左边缘跟作业卡片的左边缘对上
        // （作业板页面自己还有 12 的内边距，见 BoardPage.axaml 里那个 Margin="12 0 12 12"）。
        ClockGutter = new Thickness(DisplayBoardMetrics.ClockLeftGutter, 0, 0, 0);

        SetBoard(1, 1, 0, 3);
    }

    private void SetBoard(int row, int rowSpan, int column, int columnSpan)
    {
        BoardRow = row;
        BoardRowSpan = rowSpan;
        BoardColumn = column;
        BoardColumnSpan = columnSpan;
    }

    /// <summary>每秒刷新时钟。日期那行只在跨日时才真的变，但每秒拼一次也就一次字符串格式化。</summary>
    private void RefreshNow()
    {
        var now = _time.NowLocal;
        var text = DisplayClockFormatter.Format(
            now,
            Settings.ClockFormat,
            Settings.ShowSeconds,
            ShowDate,
            Settings.DateStyle,
            calendar: Settings.DateCalendar);

        // 「大秒」那一档（默认）把主时间、冒号、秒连成一串一次画出来——跟考试看板上那个钟一模一样
        // （一个 TextBlock、一个字重）。拆成两个 TextBlock 并排会拼出「04:03 47」：没有冒号，
        // 而且秒那一段不带主时间的 FontWeight，粗细也对不上（用户截图反馈过）。
        // 「小秒」那一档才拆：冒号留在主时间身上，秒缩成一只尾巴。
        var hasSeconds = text.Seconds.Length > 0;
        var bigSeconds = BigSeconds;

        ClockMain = bigSeconds ? text.Combined : hasSeconds ? $"{text.Main}:" : text.Main;
        ClockSeconds = bigSeconds ? string.Empty : text.Seconds;
        ShowClockSecondsTail = hasSeconds && !bigSeconds;
        ClockPeriod = text.Period;
        ShowClockPeriod = !string.IsNullOrEmpty(text.Period) && ShowClock;

        if (ShowDate)
            ClockDate = text.Date;

        // 考试倒计时跟着这一拍一起重算：最后一小时要按分钟走，最后 24 小时里也得准点变。
        RefreshExamCountdown(now);
    }

    /// <summary>
    ///     日期下面那一行：「{看板大标题} · 还有 19 天」。
    ///     <para>
    ///         倒的是**这一大场考试**（当前看板），不是某门科目：抬头取
    ///         <see cref="IExamService.ActiveBoard" /> 的 <c>Title</c>，时间取「全部考试里开考最早的那一场」
    ///         （<see cref="ExamStatusPolicy.FindFirst" />），后半句交给
    ///         <see cref="ExamTimeFormatter.FormatBoardCountdown" />——报日子的口径与全屏考试看板一致，
    ///         <b>不要在这里另算一套</b>。
    ///     </para>
    ///     <para>
    ///         那场一开考这行就整行不画（<c>now ≥ 第一场开考</c>，考完也不会再出现）：考试期间要盯的是
    ///         「还剩几分钟」，那是全屏考试看板的活；这里只剩一句「已开始」，挂着反而占地方。
    ///     </para>
    /// </summary>
    private void RefreshExamCountdown(DateTime now)
    {
        // 只由自己那个开关决定：关掉大时钟不代表不要倒计时（它不在 Viewbox 里、本来就不跟时钟缩放）。
        if (!Settings.ShowExamCountdown)
        {
            HideExamCountdown();
            return;
        }

        if (ExamStatusPolicy.FindFirst(_examService.Exams) is not { } first || now >= first.StartAt)
        {
            // 一场都没排、或者这一大场已经开考（含考完）：这一行整个不画，别留一句占位的话。
            HideExamCountdown();
            return;
        }

        ExamCountdownText = ExamTimeFormatter.FormatBoardCountdown(
            _examService.ActiveBoard.Title,
            first.StartAt - now);
        ShowExamCountdown = ExamCountdownText.Length > 0;
    }

    private void HideExamCountdown()
    {
        ExamCountdownText = string.Empty;
        ShowExamCountdown = false;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 版式 / 显示开关 / 字号 —— 谁变了都整组重算一遍：RefreshStatic 很便宜，
        // 而且改其中一项时另外几项可能跟着夹取，分点判断反而容易漏。
        RefreshStatic();

        // 时间制式、显示秒、日期写法都会改到时钟那几行文字，一并重算。
        RefreshNow();
    }

    private void OnTimeStatusChanged(object? sender, EventArgs e) => RefreshNow();

    /// <summary>
    ///     考试排期变了（在设置页 / 编辑窗口里加了、改了一场，或者换了当前看板）：
    ///     那一行字要立刻跟着换，不能等到下次打开看板。
    /// </summary>
    private void OnExamsChanged(object? sender, EventArgs e) => RefreshNow();

    // ── 一言与天气 ───────────────────────────────────────────

    /// <summary>
    ///     改了一言 / 天气的设置：立刻重来一次（换了类别或城市，屏幕上不该还是旧内容）。
    ///     不强制刷新另一项——服务自己会判新鲜度，没到期就不会发请求。
    /// </summary>
    private async void OnWidgetsSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        try
        {
            await LoadWidgetsAsync(forceQuote: true).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // 设置改一下不该有机会把窗口带崩：这里只当这一拍没取到。
        }
    }

    /// <summary>叫醒两个服务（不等待结果）。计时器与配置变化都走这里。</summary>
    private void RefreshWidgets(bool forceQuote)
    {
        if (!_isAttached)
            return;

        _ = LoadWidgetsAsync(forceQuote);
    }

    /// <summary>
    ///     取一言与天气。两个请求并行发出，各自失败互不影响；
    ///     取不到就<b>保留上一次的内容</b>（从来没有成功过则整行不画）。
    /// </summary>
    /// <param name="forceQuote">刚打开看板：一言按「每次打开都换」处理。</param>
    private async Task LoadWidgetsAsync(bool forceQuote)
    {
        if (!_isAttached)
            return;

        var widgets = Config.Widgets;
        var quoteNeeded = widgets.QuoteEnabled;
        var weatherNeeded = widgets.WeatherEnabled;
        if (!quoteNeeded && !weatherNeeded)
            return;

        _widgetCancellation?.Dispose();
        _widgetCancellation = new CancellationTokenSource();
        var cancellationToken = _widgetCancellation.Token;

        try
        {
            var quoteTask = quoteNeeded
                ? _quoteService.GetAsync(forceQuote, cancellationToken)
                : null;
            var weatherTask = weatherNeeded
                ? _weatherService.GetAsync(false, cancellationToken)
                : null;

            if (quoteTask is not null)
            {
                var quote = await quoteTask.ConfigureAwait(true);
                if (!cancellationToken.IsCancellationRequested)
                    ApplyQuote(quote);
            }

            if (weatherTask is not null)
            {
                var weather = await weatherTask.ConfigureAwait(true);
                if (!cancellationToken.IsCancellationRequested)
                    ApplyWeather(weather);
            }
        }
        catch (OperationCanceledException)
        {
            // 窗口关了或又发了一次：这一拍的结果不要了。
        }

        if (!cancellationToken.IsCancellationRequested)
            RefreshStatic();
    }

    /// <summary>把一条一言搬到界面上；空内容时什么都不改（保留上一次）。</summary>
    private void ApplyQuote(HitokotoQuote quote)
    {
        if (string.IsNullOrWhiteSpace(quote.Text))
            return;

        _lastQuote = quote;
        QuoteText = quote.Text;

        var widgets = Config.Widgets;
        QuoteMeta = BuildQuoteMeta(quote, widgets.QuoteShowSource, widgets.QuoteShowAuthor);
    }

    /// <summary>
    ///     把一份天气搬到界面上；没内容时什么都不改（保留上一次）。
    ///     <para>
    ///         每一块都先算成文字，显隐交给 <see cref="RefreshStatic" />——这样用户改「显示哪几块」
    ///         时不用重新请求接口，只是把已经有值的块重新摆一遍。
    ///     </para>
    /// </summary>
    private void ApplyWeather(WeatherSnapshot? weather)
    {
        if (weather is not { HasContent: true })
            return;

        _lastWeather = weather;

        WeatherTemperatureText = WeatherText.Temperature(weather.Temperature);
        WeatherConditionText = weather.Condition == WeatherCondition.Unknown
            ? string.Empty
            : WeatherText.Describe(weather.Condition);
        WeatherCityText = weather.CityName?.Trim() ?? string.Empty;
        WeatherRangeText = WeatherText.Range(weather.HighTemperature, weather.LowTemperature);
        WeatherHumidityText = WeatherText.Humidity(weather.Humidity);
        WeatherAqiText = WeatherText.AirQuality(weather.Aqi);
        WeatherFeelsLikeText = WeatherText.FeelsLike(weather.FeelsLike);
        WeatherTomorrowText = WeatherText.Tomorrow(weather.TomorrowCondition,
            weather.TomorrowHigh, weather.TomorrowLow);
        WeatherIcon = LoadWeatherIcon(weather.Condition);
    }

    /// <summary>有没有拿到过天气内容（决定右上角那块画不画）。</summary>
    private bool HasWeatherContent => _lastWeather is { HasContent: true };

    /// <summary>
    ///     按现象与昼夜挑图标并解码。同一张图只解一次（一分钟一拍的刷新不该重复解码）。
    ///     图标随包分发，见 <c>Corkboard/Assets/Weather/</c> 与 <c>THIRD-PARTY-NOTICES.md</c>。
    /// </summary>
    private Bitmap? LoadWeatherIcon(WeatherCondition condition)
    {
        var name = WeatherIconCatalog.FileName(condition, WeatherDaylight.IsDay(_time.NowLocal));
        if (WeatherIconCache.TryGetValue(name, out var cached))
            return cached;

        try
        {
            var uri = WeatherIconUri(name);
            if (!AssetLoader.Exists(uri))
                uri = WeatherIconUri(WeatherIconCatalog.Fallback);

            var bitmap = new Bitmap(AssetLoader.Open(uri));
            WeatherIconCache[name] = bitmap;
            return bitmap;
        }
        catch (Exception)
        {
            // 图片缺失/解码失败只影响图标这一格，文字照旧显示。
            return null;
        }
    }

    private static Uri WeatherIconUri(string name) => new($"avares://Corkboard/Assets/Weather/{name}.png");

    /// <summary>拼「—— 出处 · 作者」那一行：两个都没有就返回空串，界面上不画。</summary>
    /// <param name="quote">当前这一条一言。</param>
    /// <param name="showSource">设置里开没开「显示来源」。</param>
    /// <param name="showAuthor">设置里开没开「显示作者」。</param>
    private static string BuildQuoteMeta(HitokotoQuote quote, bool showSource, bool showAuthor)
    {
        var source = showSource ? quote.Source?.Trim() : null;
        var author = showAuthor ? quote.Author?.Trim() : null;
        var hasSource = !string.IsNullOrEmpty(source);
        var hasAuthor = !string.IsNullOrEmpty(author);

        return (hasSource, hasAuthor) switch
        {
            (true, true) when !string.Equals(source, author, StringComparison.Ordinal) => $"—— {source} · {author}",
            (true, _) => $"—— {source}",
            (_, true) => $"—— {author}",
            _ => string.Empty
        };
    }
}
