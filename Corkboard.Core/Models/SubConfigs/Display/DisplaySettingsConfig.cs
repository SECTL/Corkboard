using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Background;
using Corkboard.Core.Display;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Exam;

namespace Corkboard.Core.Models.SubConfigs.Display;

/// <summary>
///     看板模式（全屏大时钟 + 作业）的配置（<c>settings.json</c> 的 <c>display</c> 段）。
///     <para>
///         它是一块**基础功能**，不属于作业板、也不属于考试看板，所以在设置侧边栏里是顶层页、
///         不进任何分组。字段分成四组：显示什么（时钟 / 日期 / 作业）、怎么排（字号）、
///         展示策略（只看今天要交的 / 超一屏自动轮播）、什么时候自己进出（多段时段 + 星期几）。
///     </para>
///     <para>
///         字号一律过一遍夹取——时钟 / 日期 / 标题 / 退出图标走 <see cref="ExamBoardTextStyle" />
///         （与考试看板共用同一套上下限），天气与一言走 <see cref="DisplayTextStyle" /> 自己那两档，
///         手改过 settings.json 也不会画出个 9999 号的字。
///     </para>
///     <para>
///         ⚠️ 旧版本的键名是 <c>study</c>，现在是 <c>display</c>；旧键由
///         <c>MainConfigModel.LegacyDisplayOnLoad</c> 读进来，不用用户手改文件。
///     </para>
/// </summary>
public partial class DisplaySettingsConfig : BoardBackgroundSettings
{
    /// <summary>自动轮播两屏之间最少停几秒（再短就一直在动，看不清）。</summary>
    public const int MinAutoScrollPauseSeconds = 3;

    /// <summary>自动轮播两屏之间最多停几秒。</summary>
    public const int MaxAutoScrollPauseSeconds = 60;

    // ── 显示什么 ───────────────────────────────────────────────

    /// <summary>全屏里显示大时钟。默认开。</summary>
    [ObservableProperty] private bool _showClock = true;

    /// <summary>时钟下面那行日期。默认开。</summary>
    [ObservableProperty] private bool _showDate = true;

    /// <summary>全屏里显示作业板。默认开；关掉之后整屏就只剩一个大时钟。</summary>
    [ObservableProperty] private bool _showBoard = true;

    /// <summary>
    ///     日期下面那行「下一场考试还有多久」。默认开。
    ///     <para>
    ///         它读的是考试看板的<b>当前那块看板</b>（与全屏考试看板同源），没有排考试时整行不画，
    ///         所以默认开着不会给没考试的人多出一行空白。
    ///     </para>
    ///     <para>
    ///         ⚠️ 它挂在时钟那一格的<b>外面</b>（见 <c>DisplayBoardWindow.axaml</c> 里那个纵向
    ///         <c>StackPanel</c>）：关掉大时钟只留这一行照样能看，字号也不会被 Viewbox 缩放。
    ///     </para>
    /// </summary>
    [ObservableProperty] private bool _showExamCountdown = true;

    /// <summary>
    ///     全屏里的作业能不能编辑（点一条作业会弹出编辑 / 删除按钮）。
    ///     <b>默认开</b>——用户明确要的「默认需要可以编辑」。
    ///     <para>
    ///         关掉之后作业照常显示、照常滚动，只是不出现那两颗按钮：展示时把屏幕摆给别人看，
    ///         不希望谁顺手把作业改了。屏幕上会挂一颗小锁图标，点作业会提示「已锁定」，
    ///         不会像以前那样点了毫无反应。
    ///     </para>
    /// </summary>
    [ObservableProperty] private bool _editable = true;

    /// <summary>
    ///     全屏看板右下角那颗「布置作业」浮标画不画（能不能在屏幕上<b>新增</b>一条作业）。默认开。
    ///     <para>
    ///         与 <see cref="Editable" /> 分开：<see cref="Editable" /> 管的是「改 / 删已有的作业」，
    ///         这一项管的是「新布置一条」。展示时放在教室里的那块屏，常常是「可以加作业、但不许动已经有的」。
    ///         关掉之后浮标收起来；编辑与布置都关着时右下角改挂一颗小锁，说明整屏只读。
    ///     </para>
    /// </summary>
    [ObservableProperty] private bool _allowAssign = true;

    /// <summary>时钟显示到秒。默认开。</summary>
    [ObservableProperty] private bool _showSeconds = true;

    /// <summary>
    ///     秒数与主时间<b>同字号</b>（「大秒」）。默认开。
    ///     <para>
    ///         关掉之后秒数缩到主时间的四成、压到基线下方，是钟表上常见的小尾巴样子；
    ///         开着就是「21:42:07」一整串同样大的数字——远看也读得出几秒，用户要的是这个。
    ///     这一档只在 <see cref="ShowSeconds" /> 开着时有意义。
    ///     </para>
    /// </summary>
    [ObservableProperty] private bool _bigSeconds = true;

    /// <summary>时钟的制式：24 小时制 / 12 小时制。默认 24 小时制。</summary>
    [ObservableProperty] private DisplayClockFormat _clockFormat = DisplayClockFormat.TwentyFourHour;

    /// <summary>日期那行的写法：完整 / 只到月日 / 数字。默认完整。</summary>
    [ObservableProperty] private DisplayDateStyle _dateStyle = DisplayDateStyle.Full;

    /// <summary>日期那行用哪种历法：只公历 / 只农历 / 公历后面带农历。默认只公历。</summary>
    [ObservableProperty] private DisplayDateCalendar _dateCalendar = DisplayDateCalendar.Gregorian;

    // ── 展示策略 ──────────────────────────────────────────────

    /// <summary>展示哪些作业：全部 / 只看今天到期。默认全部。</summary>
    [ObservableProperty] private DisplayBoardScope _boardScope = DisplayBoardScope.All;

    /// <summary>作业多到一屏放不下时自动往下轮播。默认开。</summary>
    [ObservableProperty] private bool _autoScroll = true;

    /// <summary>轮播到底停一下、再回到顶部；两屏之间停几秒。默认 8 秒。</summary>
    [ObservableProperty] private int _autoScrollPauseSeconds = 8;

    // ── 怎么排 ────────────────────────────────────────────────

    /// <summary>左上角那块大时钟的默认字号。</summary>
    [ObservableProperty] private double _clockFontSize = DisplayTextStyle.DefaultClockFontSize;

    /// <summary>时钟下面那行日期的默认字号。</summary>
    [ObservableProperty] private double _clockDateFontSize = DisplayTextStyle.DefaultClockDateFontSize;

    /// <summary>作业区顶部那行小标题（「今天的作业」）的字号。</summary>
    [ObservableProperty] private double _titleFontSize = DisplayTextStyle.DefaultTitleFontSize;

    /// <summary>
    ///     右上角那块天气的字号（温度大字；图标、「多云」那行现象与底下一排小字都由它折算）。
    ///     <para>
    ///         早先它是跟着时钟字号按比例走的，用户要求「天气的字号要能设置」，于是改成显式配置项：
    ///         屏幕摆得远近不一样，同一个比例不会都合适。
    ///     </para>
    /// </summary>
    [ObservableProperty] private double _weatherFontSize = DisplayTextStyle.DefaultWeatherFontSize;

    /// <summary>屏幕最底下那行一言的字号（同理，从「跟着时钟走」改成显式配置项）。</summary>
    [ObservableProperty] private double _quoteFontSize = DisplayTextStyle.DefaultQuoteFontSize;

    /// <summary>
    ///     时钟下面那行「下一场考试还有多久」的字号（用户可改）。
    ///     <para>
    ///         与天气 / 一言同一档待遇：自己一项配置，不跟时钟字号按比例走——
    ///         这块字常常要单独调（离屏远近、前面还要看得清科目）。
    ///     </para>
    /// </summary>
    [ObservableProperty] private double _examCountdownFontSize = DisplayTextStyle.DefaultExamCountdownFontSize;

    /// <summary>左下角长按退出那颗图标的大小。</summary>
    [ObservableProperty] private double _exitIconSize = DisplayTextStyle.DefaultExitIconSize;

    /// <summary>鼠标停着不动多久之后把光标藏起来（秒）。0 = 从不隐藏。</summary>
    [ObservableProperty] private int _hideCursorAfterSeconds = 3;

    // ── 什么时候自己进出 ────────────────────────────────────────

    /// <summary>
    ///     到点自动进入全屏看板模式。默认关——这是「装了就自己弹全屏」的行为，
    ///     不该在用户没要求的情况下发生。
    /// </summary>
    [ObservableProperty] private bool _autoShow;

    /// <summary>
    ///     到点自动退出。默认开：自动进去的那一次，到点就该自己收掉。
    ///     <para>
    ///         只关「自动打开的那一次」——用户自己点开的那扇窗不受影响（见看门狗）。
    ///     </para>
    /// </summary>
    [ObservableProperty] private bool _autoHide = true;

    /// <summary>
    ///     自动进出的时段（可以配多段、各自选星期几）。默认一段：每天 19:00 进、21:30 出。
    ///     <para>
    ///         旧版本只有一对 <see cref="EnterHour" /> / <see cref="ExitHour" /> 字段，
    ///         读盘后由 <see cref="Normalize" /> 折成这里的第一段，<b>不会丢用户的设置</b>。
    ///     </para>
    /// </summary>
    [ObservableProperty] private List<DisplaySlot> _slots = [];

    // ── 旧字段（只为兼容旧 settings.json 与老调用方）───────────

    /// <summary>旧版：自动进入的小时。读盘后折进 <see cref="Slots" /> 第一段。</summary>
    [ObservableProperty] private int _enterHour = DisplaySchedulePolicy.DefaultEnterTime.Hour;

    /// <summary>旧版：自动进入的分钟。</summary>
    [ObservableProperty] private int _enterMinute = DisplaySchedulePolicy.DefaultEnterTime.Minute;

    /// <summary>旧版：自动退出的小时。</summary>
    [ObservableProperty] private int _exitHour = DisplaySchedulePolicy.DefaultExitTime.Hour;

    /// <summary>旧版：自动退出的分钟。</summary>
    [ObservableProperty] private int _exitMinute = DisplaySchedulePolicy.DefaultExitTime.Minute;

    // ── 派生值 ────────────────────────────────────────────────

    /// <summary>第一段的进入时刻（旧调用方与单测还读它）。</summary>
    public TimeOnly EnterTime => Slots is { Count: > 0 } slots ? slots[0].EnterTime : DisplaySchedulePolicy.ResolveTime(EnterHour, EnterMinute);

    /// <summary>第一段的退出时刻。</summary>
    public TimeOnly ExitTime => Slots is { Count: > 0 } slots ? slots[0].ExitTime : DisplaySchedulePolicy.ResolveTime(ExitHour, ExitMinute);

    /// <summary>真正参与自动进出的时段（已归一化）。</summary>
    public IReadOnlyList<DisplaySlot> ActiveSlots => Slots ?? [];

    /// <summary>自动进出的时段数量。</summary>
    public int SlotCount => Slots?.Count ?? 0;

    /// <summary>夹取过字号之后的时钟字号，窗口直接绑它。</summary>
    public double ResolvedClockFontSize =>
        ExamBoardTextStyle.ClampClock(ClockFontSize, DisplayTextStyle.DefaultClockFontSize);

    /// <summary>夹取过的日期字号。</summary>
    public double ResolvedClockDateFontSize =>
        ExamBoardTextStyle.Clamp(ClockDateFontSize, DisplayTextStyle.DefaultClockDateFontSize);

    /// <summary>夹取过的作业区标题字号。</summary>
    public double ResolvedTitleFontSize =>
        ExamBoardTextStyle.Clamp(TitleFontSize, DisplayTextStyle.DefaultTitleFontSize);

    /// <summary>夹取过的退出图标字号。</summary>
    public double ResolvedExitIconSize =>
        ExamBoardTextStyle.Clamp(ExitIconSize, DisplayTextStyle.DefaultExitIconSize);

    /// <summary>夹取过的天气字号（温度大字）。</summary>
    public double ResolvedWeatherFontSize => DisplayTextStyle.WeatherFontSize(WeatherFontSize);

    /// <summary>夹取过的一言字号。</summary>
    public double ResolvedQuoteFontSize => DisplayTextStyle.QuoteFontSize(QuoteFontSize);

    /// <summary>夹取过的考试倒计时字号。</summary>
    public double ResolvedExamCountdownFontSize =>
        DisplayTextStyle.ExamCountdownFontSize(ExamCountdownFontSize);

    /// <summary>
    ///     时钟、作业、考试倒计时都被关掉了——屏幕上什么都不剩，得给一句提示而不是一块空白。
    ///     <para>
    ///         倒计时单独算一项：它挂在时钟那一格外面，时钟关掉、作业关掉之后它还能自己显示。
    ///     </para>
    /// </summary>
    public bool EverythingHidden => !ShowClock && !ShowBoard && !ShowExamCountdown;

    /// <summary>光标隐藏的等待时长；0 表示不隐藏。</summary>
    public TimeSpan CursorHideDelay =>
        HideCursorAfterSeconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(HideCursorAfterSeconds);

    /// <summary>
    ///     读盘后收拾一遍：时刻夹进合法区间、字号夹进上下限、枚举脏值折成合法项、
    ///     旧单段时刻迁进时段列表。
    ///     由 <c>MainConfigModel.NormalizeLoaded</c> 在反序列化之后调用——
    ///     <b>不要</b>挪进设置页：全屏窗口在设置页打开之前就会先按脏值渲染。
    /// </summary>
    public void Normalize()
    {
        EnterHour = Math.Clamp(EnterHour, 0, 23);
        EnterMinute = Math.Clamp(EnterMinute, 0, 59);
        ExitHour = Math.Clamp(ExitHour, 0, 23);
        ExitMinute = Math.Clamp(ExitMinute, 0, 59);
        HideCursorAfterSeconds = Math.Clamp(HideCursorAfterSeconds, 0, 60);
        AutoScrollPauseSeconds = Math.Clamp(AutoScrollPauseSeconds, MinAutoScrollPauseSeconds, MaxAutoScrollPauseSeconds);

        ClockFormat = Enum.IsDefined(ClockFormat) ? ClockFormat : DisplayClockFormat.TwentyFourHour;
        DateStyle = Enum.IsDefined(DateStyle) ? DateStyle : DisplayDateStyle.Full;
        DateCalendar = Enum.IsDefined(DateCalendar) ? DateCalendar : DisplayDateCalendar.Gregorian;
        BoardScope = Enum.IsDefined(BoardScope) ? BoardScope : DisplayBoardScope.All;

        ClockFontSize = ExamBoardTextStyle.ClampClock(ClockFontSize, DisplayTextStyle.DefaultClockFontSize);
        ClockDateFontSize = ExamBoardTextStyle.Clamp(ClockDateFontSize, DisplayTextStyle.DefaultClockDateFontSize);
        TitleFontSize = ExamBoardTextStyle.Clamp(TitleFontSize, DisplayTextStyle.DefaultTitleFontSize);
        ExitIconSize = ExamBoardTextStyle.Clamp(ExitIconSize, DisplayTextStyle.DefaultExitIconSize);

        // 天气 / 一言走自己那两档上下限（见 DisplayTextStyle）：坏值退回默认字号。
        WeatherFontSize = DisplayTextStyle.WeatherFontSize(WeatherFontSize);
        QuoteFontSize = DisplayTextStyle.QuoteFontSize(QuoteFontSize);
        ExamCountdownFontSize = DisplayTextStyle.ExamCountdownFontSize(ExamCountdownFontSize);

        Slots ??= [];

        NormalizeBackground();

        // 旧版本只有一对时刻：读盘后折成第一段，用户原来的设置照旧生效。
        if (Slots.Count == 0)
        {
            Slots.Add(new DisplaySlot
            {
                EnterHour = EnterHour,
                EnterMinute = EnterMinute,
                ExitHour = ExitHour,
                ExitMinute = ExitMinute
            });
        }

        while (Slots.Count > DisplaySlot.MaxCount)
            Slots.RemoveAt(Slots.Count - 1);

        foreach (var slot in Slots)
            slot?.Normalize();

        // 老字段跟着第一段走：第三方调用方与老单测读 EnterTime / ExitTime 时不能停在旧值上。
        if (Slots[0] is { } first)
        {
            EnterHour = first.EnterHour;
            EnterMinute = first.EnterMinute;
            ExitHour = first.ExitHour;
            ExitMinute = first.ExitMinute;
        }
    }

    /// <summary>
    ///     只写属性：老配置里的 <c>layout</c>（版式）已经下架——时钟<b>固定显示在左上角</b>，
    ///     设置页里不再给这一项（用户要求「时间的显示位置不能改」）。
    ///     <para>
    ///         老版本的第 4 档（<c>3</c> =「只有作业」）本意就是「别画时钟」，读盘时折算成关掉
    ///         <see cref="ShowClock" />，升级上来的人不会突然多出一块大钟；其余几档本来就画时钟，
    ///         折成左上角即可，什么都不用做。
    ///     </para>
    ///     <para>
    ///         只写不读（没有 getter）→ 序列化时不会再把 <c>layout</c> 写回 settings.json，下次落盘就干净了。
    ///     </para>
    /// </summary>
    [JsonPropertyName("layout")]
    public int? LegacyLayoutOnLoad
    {
        set
        {
            if (value == BoardOnlyLayout)
                ShowClock = false;
        }
    }

    /// <summary>老配置里「只有作业」那一档的数值（枚举已删，单个数字留在这儿给折算用）。</summary>
    private const int BoardOnlyLayout = 3;
}
