using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Display;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Exam;
using Corkboard.Core.Models.SubConfigs.Display;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Time;
using Corkboard.Core.Widgets.Weather;
using Corkboard.Platforms.Abstractions;
using Corkboard.ViewModels;
using Microsoft.Extensions.Logging;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Display;

/// <summary>
///     「设置 → 看板模式」页。
///     <para>
///         这一页刻意**不用一列可展开的设置卡**：整页就是几段「一行一项」的列表，
///         选项少的直接摆成分段按钮，字号那几项是滑块、拖动时弹一个预览浮窗，
///         让人一眼看出「调多大会变成什么样」——全是照考试看板「看板字号」那页的做法。
///     </para>
///     <para>
///         ⚠️ 这里**没有**「时钟放在哪」这一项：时钟固定显示在左上角（用户要求
///         「时间的显示位置不能改」），老配置里的 <c>layout</c> 只在读盘时折算一次。
///     </para>
///     <para>
///         字号一律走草稿 + 松手提交（<see cref="DisplaySliderItem" />）：配置 handler 每变一次属性
///         就落一次盘，直接绑配置等于拖一路写几十遍 <c>settings.json</c>。
///     </para>
///     <para>
///         ⚠️ 时段那几段是**集合里的元素**，<c>ConfigHandlerBase</c> 的自动保存走不到
///         （它只订阅 INotifyPropertyChanged 类型，<c>List&lt;T&gt;</c> 不是），
///         所以每改一处都要显式 <c>Save()</c>——见 <see cref="DisplaySlotItem" /> 的 <c>_changed</c> 回调。
///     </para>
/// </summary>
public partial class DisplaySettingsPageViewModel : ViewModelBase
{
    private readonly MainConfigHandler _configHandler;

    /// <summary>状态那几行会过时（跨过开点、手动开 / 关、今天被抑制），定时刷一下。</summary>
    private readonly DispatcherTimer _statusTimer;

    public DisplaySettingsPageViewModel(MainConfigHandler configHandler)
        : base(configHandler)
    {
        _configHandler = configHandler;

        ClockFormatChoices =
        [
            new DisplayChoice((int)DisplayClockFormat.TwentyFourHour, CR.Settings_Display_ClockFormat24),
            new DisplayChoice((int)DisplayClockFormat.TwelveHour, CR.Settings_Display_ClockFormat12)
        ];

        DateStyleChoices =
        [
            new DisplayChoice((int)DisplayDateStyle.Full, CR.Settings_Display_DateFormatFull),
            new DisplayChoice((int)DisplayDateStyle.MonthDay, CR.Settings_Display_DateFormatMonthDay),
            new DisplayChoice((int)DisplayDateStyle.Numeric, CR.Settings_Display_DateFormatNumeric)
        ];

        DateCalendarChoices =
        [
            new DisplayChoice((int)DisplayDateCalendar.Gregorian, CR.Settings_Display_DateCalendarGregorian),
            new DisplayChoice((int)DisplayDateCalendar.Lunar, CR.Settings_Display_DateCalendarLunar),
            new DisplayChoice((int)DisplayDateCalendar.GregorianAndLunar, CR.Settings_Display_DateCalendarBoth)
        ];

        // 同一件事的几个开关合成一个下拉（用户要求「同一个类别的合并一下，别什么都是开关」）：
        // 「显示作业」+「展示哪些作业」本来就是一个三档的选项，摆成一颗开关加一个下拉，
        // 用户还得先在脑子里算「关掉的开关遇上只看今天要交的是哪一档」。
        BoardDisplayChoices =
        [
            new DisplayChoice(BoardDisplayAll, CR.Settings_Display_BoardScopeAll),
            new DisplayChoice(BoardDisplayDueToday, CR.Settings_Display_BoardScopeDueToday),
            new DisplayChoice(BoardDisplayNone, CR.Settings_Display_BoardDisplayNone)
        ];

        // 同一件事的两个开关合成一个下拉（用户要求「同一个类别的合并一下，别什么都是开关」）：
        // 「显示秒」+「秒和时间一样大」本来就是一个三档的选项，摆成两个开关用户还得先在脑子里算组合。
        ClockDateChoices =
        [
            new DisplayChoice(ClockDateBoth, CR.Settings_Display_ClockDateBoth),
            new DisplayChoice(ClockDateClockOnly, CR.Settings_Display_ClockOnly),
            new DisplayChoice(ClockDateDateOnly, CR.Settings_Display_DateOnly),
            new DisplayChoice(ClockDateNone, CR.Settings_Display_ClockDateNone)
        ];

        SecondsChoices =
        [
            new DisplayChoice(SecondsModeNone, CR.Settings_Display_SecondsNone),
            new DisplayChoice(SecondsModeSmall, CR.Settings_Display_SecondsSmall),
            new DisplayChoice(SecondsModeBig, CR.Settings_Display_SecondsBig)
        ];

        FontItems =
        [
            new DisplaySliderItem(
                CR.Settings_Display_ClockFontSize,
                CR.Settings_Display_ClockFontSizeDescription,
                DisplayTextStyle.DefaultClockFontSize,
                ExamBoardTextStyle.MinFontSize,
                ExamBoardTextStyle.MaxClockFontSize,
                CR.Settings_Display_FontUnit,
                isClock: true,
                showsPreview: true,
                sampleText: () => SampleClockText,
                previewFontWeight: FontWeight.SemiBold,
                read: () => Settings.ClockFontSize,
                write: value => Settings.ClockFontSize = value),
            new DisplaySliderItem(
                CR.Settings_Display_ClockDateFontSize,
                CR.Settings_Display_ClockDateFontSizeDescription,
                DisplayTextStyle.DefaultClockDateFontSize,
                ExamBoardTextStyle.MinFontSize,
                ExamBoardTextStyle.MaxFontSize,
                CR.Settings_Display_FontUnit,
                isClock: false,
                showsPreview: true,
                sampleText: () => SampleDateText,
                previewFontWeight: FontWeight.Normal,
                read: () => Settings.ClockDateFontSize,
                write: value => Settings.ClockDateFontSize = value),
            new DisplaySliderItem(
                CR.Settings_Display_TitleFontSize,
                CR.Settings_Display_TitleFontSizeDescription,
                DisplayTextStyle.DefaultTitleFontSize,
                ExamBoardTextStyle.MinFontSize,
                ExamBoardTextStyle.MaxFontSize,
                CR.Settings_Display_FontUnit,
                isClock: false,
                showsPreview: true,
                sampleText: () => CR.Display_Board_Heading,
                previewFontWeight: FontWeight.SemiBold,
                read: () => Settings.TitleFontSize,
                write: value => Settings.TitleFontSize = value),
            new DisplaySliderItem(
                CR.Settings_Display_WeatherFontSize,
                CR.Settings_Display_WeatherFontSizeDescription,
                DisplayTextStyle.DefaultWeatherFontSize,
                DisplayTextStyle.MinWeatherFontSize,
                DisplayTextStyle.MaxWeatherFontSize,
                CR.Settings_Display_FontUnit,
                isClock: false,
                showsPreview: true,
                sampleText: () => SampleWeatherText,
                previewFontWeight: FontWeight.SemiBold,
                read: () => Settings.WeatherFontSize,
                write: value => Settings.WeatherFontSize = value),
            new DisplaySliderItem(
                CR.Settings_Display_QuoteFontSize,
                CR.Settings_Display_QuoteFontSizeDescription,
                DisplayTextStyle.DefaultQuoteFontSize,
                DisplayTextStyle.MinQuoteFontSize,
                DisplayTextStyle.MaxQuoteFontSize,
                CR.Settings_Display_FontUnit,
                isClock: false,
                showsPreview: true,
                sampleText: () => CR.Settings_Display_QuoteSample,
                previewFontWeight: FontWeight.Normal,
                read: () => Settings.QuoteFontSize,
                write: value => Settings.QuoteFontSize = value),
            new DisplaySliderItem(
                CR.Settings_Display_ExamCountdownFontSize,
                CR.Settings_Display_ExamCountdownFontSizeDescription,
                DisplayTextStyle.DefaultExamCountdownFontSize,
                DisplayTextStyle.MinExamCountdownFontSize,
                DisplayTextStyle.MaxExamCountdownFontSize,
                CR.Settings_Display_FontUnit,
                isClock: false,
                showsPreview: true,
                sampleText: () => SampleExamCountdownText,
                previewFontWeight: FontWeight.Normal,
                read: () => Settings.ExamCountdownFontSize,
                write: value => Settings.ExamCountdownFontSize = value)
        ];

        AutoScrollPauseItem = new DisplaySliderItem(
            CR.Settings_Display_AutoScrollPause,
            CR.Settings_Display_AutoScrollPauseDescription,
            8,
            DisplaySettingsConfig.MinAutoScrollPauseSeconds,
            DisplaySettingsConfig.MaxAutoScrollPauseSeconds,
            CR.Settings_Display_CursorSecondsUnit,
            isClock: false,
            showsPreview: false,
            sampleText: () => string.Empty,
            previewFontWeight: FontWeight.Normal,
            read: () => Settings.AutoScrollPauseSeconds,
            write: value => Settings.AutoScrollPauseSeconds = (int)Math.Round(value));

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _statusTimer.Tick += (_, _) => RefreshStatus();

        RebuildSlots();
        RefreshStatus();
    }

    /// <summary>看板模式的配置段（这一页改的就是它）。</summary>
    public DisplaySettingsConfig Settings => Config.Display;

    // ── 钟面写什么 ────────────────────────────────────────────

    /// <summary>12 / 24 小时制。</summary>
    public IReadOnlyList<DisplayChoice> ClockFormatChoices { get; }

    /// <summary>日期那行的写法。</summary>
    public IReadOnlyList<DisplayChoice> DateStyleChoices { get; }

    /// <summary>日期那行用哪种历法：只公历 / 只农历 / 公历后面带农历。</summary>
    public IReadOnlyList<DisplayChoice> DateCalendarChoices { get; }

    /// <summary>作业区怎么显示：全部 / 只看今天要交的 / 不显示。取值见 <c>BoardDisplay*</c> 常量。</summary>
    public IReadOnlyList<DisplayChoice> BoardDisplayChoices { get; }

    // ── 显示内容：两件事各合成一个下拉 ────────────────────────

    /// <summary>钟面上显示什么（时钟 / 日期 / 都要 / 都不要）。取值见 <c>ClockDate*</c> 常量。</summary>
    public IReadOnlyList<DisplayChoice> ClockDateChoices { get; }

    /// <summary>秒怎么显示（不显示 / 小一号 / 和时间一样大）。取值见 <c>SecondsMode*</c> 常量。</summary>
    public IReadOnlyList<DisplayChoice> SecondsChoices { get; }

    private const int ClockDateBoth = 0;
    private const int ClockDateClockOnly = 1;
    private const int ClockDateDateOnly = 2;
    private const int ClockDateNone = 3;

    private const int SecondsModeNone = 0;
    private const int SecondsModeSmall = 1;
    private const int SecondsModeBig = 2;

    // 作业区怎么显示那一档（只在这一页的 UI 里用，不落盘：落盘的还是 ShowBoard + BoardScope 两项）。
    private const int BoardDisplayAll = 0;
    private const int BoardDisplayDueToday = 1;
    private const int BoardDisplayNone = 2;

    /// <summary>
    ///     钟面上显示什么。
    /// </summary>
    /// <remarks>
    ///     配置里仍然是 <c>ShowClock</c> / <c>ShowDate</c> 两个布尔（JSON 键冻结，别合并成一个字段：
    ///     老配置里「只有作业」（<c>layout = 3</c>）折算下来就是 <c>ShowClock = false</c> + <c>ShowDate = true</c>）。
    ///     这里只做界面上的等价换算。
    /// </remarks>
    public DisplayChoice SelectedClockDate
    {
        get => Find(ClockDateChoices, CurrentClockDate);
        set
        {
            if (value is null || CurrentClockDate == value.Value)
                return;

            switch (value.Value)
            {
                case ClockDateClockOnly:
                    Settings.ShowDate = false;
                    Settings.ShowClock = true;
                    break;
                case ClockDateDateOnly:
                    Settings.ShowClock = false;
                    Settings.ShowDate = true;
                    break;
                case ClockDateNone:
                    Settings.ShowClock = false;
                    Settings.ShowDate = false;
                    break;
                default:
                    Settings.ShowClock = true;
                    Settings.ShowDate = true;
                    break;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>秒怎么显示：关掉就是只剩时分，打开还分「小一号」与「跟时间一样大」两档。</summary>
    public DisplayChoice SelectedSeconds
    {
        get => Find(SecondsChoices, CurrentSecondsMode);
        set
        {
            if (value is null || CurrentSecondsMode == value.Value)
                return;

            switch (value.Value)
            {
                case SecondsModeNone:
                    // 只关「显示秒」，不动 BigSeconds：用户回头再选「跟时间一样大」时还是原来那一档。
                    Settings.ShowSeconds = false;
                    break;
                case SecondsModeSmall:
                    Settings.ShowSeconds = true;
                    Settings.BigSeconds = false;
                    break;
                default:
                    Settings.ShowSeconds = true;
                    Settings.BigSeconds = true;
                    break;
            }

            OnPropertyChanged();
        }
    }

    private int CurrentClockDate => (Settings.ShowClock, Settings.ShowDate) switch
    {
        (true, true) => ClockDateBoth,
        (true, false) => ClockDateClockOnly,
        (false, true) => ClockDateDateOnly,
        _ => ClockDateNone
    };

    private int CurrentSecondsMode => !Settings.ShowSeconds
        ? SecondsModeNone
        : Settings.BigSeconds
            ? SecondsModeBig
            : SecondsModeSmall;

    public DisplayChoice SelectedClockFormat
    {
        get => Find(ClockFormatChoices, (int)Settings.ClockFormat);
        set
        {
            if (value is null || (int)Settings.ClockFormat == value.Value)
                return;

            Settings.ClockFormat = (DisplayClockFormat)value.Value;
            OnPropertyChanged();
            RefreshSamples();
        }
    }

    public DisplayChoice SelectedDateStyle
    {
        get => Find(DateStyleChoices, (int)Settings.DateStyle);
        set
        {
            if (value is null || (int)Settings.DateStyle == value.Value)
                return;

            Settings.DateStyle = (DisplayDateStyle)value.Value;
            OnPropertyChanged();
            RefreshSamples();
        }
    }

    /// <summary>日期历法：只公历 / 只农历 / 公历后面带农历。</summary>
    /// <remarks>
    ///     农历拿不到（运行时的 .NET 农历日历不可用）时这一项也照旧可选，只是钟面上落回公历——
    ///     这里不把下拉框禁掉：禁掉用户只会以为「这个功能没了」，而不是「这台机器上没有农历」。
    /// </remarks>
    public DisplayChoice SelectedDateCalendar
    {
        get => Find(DateCalendarChoices, (int)Settings.DateCalendar);
        set
        {
            if (value is null || (int)Settings.DateCalendar == value.Value)
                return;

            Settings.DateCalendar = (DisplayDateCalendar)value.Value;
            OnPropertyChanged();
            RefreshSamples();
        }
    }

    /// <summary>
    ///     作业区怎么显示：全部 / 只看今天要交的 / 不显示。
    /// </summary>
    /// <remarks>
    ///     配置里仍然是 <c>ShowBoard</c> + <c>BoardScope</c> 两项（JSON 键冻结，别合并成一个字段：
    ///     老配置里的 <c>show_board</c> 与 <c>board_scope</c> 都要原样读得出来）。这里只做界面上的等价换算。
    /// </remarks>
    public DisplayChoice SelectedBoardDisplay
    {
        get => Find(BoardDisplayChoices, CurrentBoardDisplay);
        set
        {
            if (value is null || CurrentBoardDisplay == value.Value)
                return;

            switch (value.Value)
            {
                case BoardDisplayNone:
                    // 只关作业区，不动 BoardScope：回头选「只看今天要交的」时还是原来那一档。
                    Settings.ShowBoard = false;
                    break;
                case BoardDisplayDueToday:
                    Settings.ShowBoard = true;
                    Settings.BoardScope = DisplayBoardScope.DueToday;
                    break;
                default:
                    Settings.ShowBoard = true;
                    Settings.BoardScope = DisplayBoardScope.All;
                    break;
            }

            OnPropertyChanged();
        }
    }

    private int CurrentBoardDisplay => !Settings.ShowBoard
        ? BoardDisplayNone
        : Settings.BoardScope == DisplayBoardScope.DueToday
            ? BoardDisplayDueToday
            : BoardDisplayAll;

    // ── 字号与轮播 ────────────────────────────────────────────

    /// <summary>三处字号，滑块 + 拖动预览浮窗。</summary>
    public IReadOnlyList<DisplaySliderItem> FontItems { get; }

    /// <summary>
    ///     自动轮播每屏停几秒（滑块，不弹预览）。
    ///     <para>
    ///         光标隐藏**没有设置项**：早就默认 3 秒不动就藏起来，用户明确说这一项不用给（见
    ///         <see cref="DisplaySettingsConfig.HideCursorAfterSeconds" />，键留着、值固定 3）。
    ///     </para>
    /// </summary>
    public DisplaySliderItem AutoScrollPauseItem { get; }

    // ── 时段 ─────────────────────────────────────────────────

    /// <summary>自动进出的时段。可以有好几段，每段自己选星期几。</summary>
    public ObservableCollection<DisplaySlotItem> SlotItems { get; } = [];

    /// <summary>还能不能再加一段（配置里最多 <see cref="DisplaySlot.MaxCount" /> 段）。</summary>
    public bool CanAddSlot => SlotItems.Count < DisplaySlot.MaxCount;

    // ── 状态 ─────────────────────────────────────────────────

    /// <summary>现在开没开。</summary>
    [ObservableProperty] private bool _statusIsOpen;

    /// <summary>「现在正开着」/「现在没开」。</summary>
    [ObservableProperty] private string _statusTitle = string.Empty;

    /// <summary>「今天不再自动打开」那一行要不要露出来（开了自动打开才有意义）。</summary>
    [ObservableProperty] private bool _showSuppressToday;

    /// <summary>还没压住今天：按钮可以点。</summary>
    [ObservableProperty] private bool _canSuppressToday;

    /// <summary>按钮上的字：没压住时是「今天不再自动打开」，压住后变成一句已生效的说明。</summary>
    [ObservableProperty] private string _suppressTodayText = string.Empty;

    /// <summary>时钟和作业都关掉了：警告一句，免得全屏里只剩空白。</summary>
    public bool ShowNothingVisibleWarning => Settings.EverythingHidden;

    /// <summary>那句警告的文案。</summary>
    public string NothingVisibleWarning => CR.Settings_Display_WarningNothingVisible;

    // ── 生命周期 ──────────────────────────────────────────────

    /// <summary>页面进入可视树：订阅配置变化，把草稿对齐到当前配置。</summary>
    public void Attach()
    {
        Settings.PropertyChanged -= OnSettingsChanged;
        Settings.PropertyChanged += OnSettingsChanged;
        ResetDrafts();
        RefreshStatus();
        _statusTimer.Start();
    }

    /// <summary>页面离开可视树：必须退订，否则每开一次页就往单例配置上多挂一个处理器。</summary>
    public void Detach()
    {
        _statusTimer.Stop();
        Settings.PropertyChanged -= OnSettingsChanged;
    }

    /// <summary>把滑块与时段草稿全部提交进配置。</summary>
    /// <remarks>
    ///     ⚠️ 这里**只提交草稿、不落盘**：滑块提交写的都是配置上的标量属性，<c>ConfigHandlerBase</c>
    ///     自己会存；时段那种集合元素由 <see cref="OnSlotChanged" /> 单独存。以前在末尾无条件
    ///     <c>Save()</c>，结果是滚一下页面、切一次焦点就写一遍 settings.json（实测每秒两次）。
    /// </remarks>
    public void CommitAll()
    {
        foreach (var item in FontItems)
            item.Commit();

        AutoScrollPauseItem.Commit();

        foreach (var slot in SlotItems)
            slot.CommitTimes();

        RefreshStatus();
    }

    /// <summary>只提交各段时刻（TimePicker 失焦用）。</summary>
    public void CommitSchedule()
    {
        foreach (var slot in SlotItems)
            slot.CommitTimes();
    }

    /// <summary>把草稿全部拉回配置里的值（页面重新打开、外部改了配置时用）。</summary>
    public void ResetDrafts()
    {
        foreach (var item in FontItems)
            item.ResetDraft();

        AutoScrollPauseItem.ResetDraft();

        RebuildSlots();
        RefreshSamples();

        OnPropertyChanged(nameof(ShowNothingVisibleWarning));
        OnPropertyChanged(nameof(SelectedClockFormat));
        OnPropertyChanged(nameof(SelectedDateStyle));
        OnPropertyChanged(nameof(SelectedDateCalendar));
        OnPropertyChanged(nameof(SelectedBoardDisplay));
        OnPropertyChanged(nameof(SelectedClockDate));
        OnPropertyChanged(nameof(SelectedSeconds));
    }

    /// <summary>今天剩下的时间都不再自动打开（状态卡里那颗按钮，与托盘同一套记账）。</summary>
    [RelayCommand]
    private void SuppressToday()
    {
        App.SuppressDisplayForToday(Now);
        RefreshStatus();
    }

    /// <summary>再加一段时段：接着上一段的结束时刻往后排一个半小时。</summary>
    [RelayCommand]
    private void AddSlot()
    {
        if (!CanAddSlot)
            return;

        Settings.Slots ??= [];
        Settings.Slots.Add(CreateSlotAfter(Settings.Slots.LastOrDefault()));
        _configHandler.Save();
        RebuildSlots();
        RefreshStatus();
    }

    /// <summary>删掉这一段；删到一段不剩时补回一段默认的（空列表等于「没配时段」）。</summary>
    private void RemoveSlotItem(DisplaySlotItem item)
    {
        if (Settings.Slots is not { } slots)
            return;

        slots.Remove(item.Slot);
        if (slots.Count == 0)
            slots.Add(DisplaySlot.CreateDefault());

        _configHandler.Save();
        RebuildSlots();
        RefreshStatus();
    }

    // ── 内部 ─────────────────────────────────────────────────

    private static DisplayChoice Find(IReadOnlyList<DisplayChoice> choices, int value) =>
        choices.FirstOrDefault(choice => choice.Value == value) ?? choices[0];

    private static DisplaySlot CreateSlotAfter(DisplaySlot? previous)
    {
        if (previous is null)
            return DisplaySlot.CreateDefault();

        // 接着上一段结束的时刻往下排，省得每加一段都要手调两个时间。
        var enter = previous.ExitTime;
        var exit = enter.AddMinutes(90);

        return new DisplaySlot
        {
            EnterHour = enter.Hour,
            EnterMinute = enter.Minute,
            ExitHour = exit.Hour,
            ExitMinute = exit.Minute
        };
    }

    private void RebuildSlots()
    {
        SlotItems.Clear();

        Settings.Slots ??= [];
        if (Settings.Slots.Count == 0)
            Settings.Slots.Add(DisplaySlot.CreateDefault());

        foreach (var slot in Settings.Slots.Take(DisplaySlot.MaxCount))
            SlotItems.Add(new DisplaySlotItem(slot, OnSlotChanged, RemoveSlotItem));

        OnPropertyChanged(nameof(CanAddSlot));
    }

    private void OnSlotChanged()
    {
        _configHandler.Save();
        RefreshStatus();
    }

    /// <summary>预览浮窗里那几行示例：换制式 / 换日期写法 / 开关秒之后要重画。</summary>
    private void RefreshSamples()
    {
        foreach (var item in FontItems)
            item.RefreshSample();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null
            or nameof(DisplaySettingsConfig.ShowSeconds)
            or nameof(DisplaySettingsConfig.BigSeconds)
            or nameof(DisplaySettingsConfig.ClockFormat)
            or nameof(DisplaySettingsConfig.DateStyle)
            or nameof(DisplaySettingsConfig.DateCalendar))
            RefreshSamples();

        if (e.PropertyName is null
            or nameof(DisplaySettingsConfig.ClockFormat)
            or nameof(DisplaySettingsConfig.DateStyle)
            or nameof(DisplaySettingsConfig.DateCalendar))
        {
            OnPropertyChanged(nameof(SelectedClockFormat));
            OnPropertyChanged(nameof(SelectedDateStyle));
            OnPropertyChanged(nameof(SelectedDateCalendar));
        }

        // 作业区那一档是两个字段拼出来的（ShowBoard + BoardScope）：哪一个变了都要重算下拉的选中项。
        if (e.PropertyName is null
            or nameof(DisplaySettingsConfig.ShowBoard)
            or nameof(DisplaySettingsConfig.BoardScope))
            OnPropertyChanged(nameof(SelectedBoardDisplay));

        if (e.PropertyName is null
            or nameof(DisplaySettingsConfig.ShowClock)
            or nameof(DisplaySettingsConfig.ShowDate))
            OnPropertyChanged(nameof(SelectedClockDate));

        if (e.PropertyName is null
            or nameof(DisplaySettingsConfig.ShowSeconds)
            or nameof(DisplaySettingsConfig.BigSeconds))
            OnPropertyChanged(nameof(SelectedSeconds));

        if (e.PropertyName is null or nameof(DisplaySettingsConfig.ShowClock) or nameof(DisplaySettingsConfig.ShowBoard))
            OnPropertyChanged(nameof(ShowNothingVisibleWarning));

        RefreshStatus();
    }

    /// <summary>把状态卡重新算一遍（只说现在开没开，不预告几点会自动打开）。</summary>
    private void RefreshStatus()
    {
        var now = Now;

        StatusIsOpen = App.IsDisplayBoardWindowOpen;
        StatusTitle = StatusIsOpen ? CR.Settings_Display_StatusOpen : CR.Settings_Display_StatusClosed;

        var suppressed = App.IsDisplaySuppressed(now);
        ShowSuppressToday = Settings.AutoShow;
        CanSuppressToday = !suppressed;
        SuppressTodayText = suppressed ? CR.Settings_Display_StatusSuppressed : CR.Menu_SuppressDisplayToday;
    }

    /// <summary>应用内的「现在」（有校时服务就用它，跟全屏上的钟同一个来源）。</summary>
    private static DateTime Now => IAppHost.TryGetService<NtpTimeService>()?.NowLocal ?? DateTime.Now;

    /// <summary>时钟字号预览用的示例时间：「21:42:07」/「9:42:07 PM」。</summary>
    /// <remarks>
    ///     用 <see cref="DisplayClockText.Combined" /> 拼，跟全屏上真实画出来的那一串一模一样
    ///     （以前是 <c>$"{Main} {Seconds}"</c>，预览里是「21:42 07」——冒号没了，
    ///     跟钟面上那个「04:03 47」是同一个毛病）。
    /// </remarks>
    private string SampleClockText
    {
        get
        {
            var sample = DateTime.Today.AddHours(21).AddMinutes(42).AddSeconds(7);
            var text = DisplayClockFormatter.Format(
                sample,
                Settings.ClockFormat,
                Settings.ShowSeconds,
                showDate: false,
                Settings.DateStyle);

            return $"{text.Combined} {text.Period}".TrimEnd();
        }
    }

    /// <summary>预览浮窗里那句示例日期：跟全屏上真实渲染的格式一模一样，才算「所见即所得」。</summary>
    /// <remarks>
    ///     写法（<see cref="DisplaySettingsConfig.DateStyle" />）与历法（<see cref="DisplaySettingsConfig.DateCalendar" />）
    ///     都要一起跟上，否则下拉框里选「公历 + 农历」、预览却只给公历。
    /// </remarks>
    private string SampleDateText
    {
        get
        {
            var sample = DateOnly.FromDateTime(DateTime.Today);
            return DisplayClockFormatter.FormatDate(sample, Settings.DateStyle, calendar: Settings.DateCalendar);
        }
    }

    /// <summary>天气字号预览浮窗里的示例：跟屏幕上那块一样是「温度大字 + 现象」。</summary>
    /// <remarks>
    ///     取 <see cref="WeatherText" /> 拼，别在预览里另写「9°」——那块的字样（度数符号、
    ///     现象的说法）本来就归 Core 管，两边不一致的话预览就不是「所见即所得」了。
    /// </remarks>
    private static string SampleWeatherText =>
        $"{WeatherText.Temperature(9)} {WeatherText.Describe(WeatherCondition.PartlyCloudy)}";

    /// <summary>考试倒计时那一行字号预览用的示例：跟日期下面真实画出来的那句同一个写法。</summary>
    /// <remarks>
    ///     有排期就用真的「当前看板标题 + 还有几天」（与屏幕上读的同一份：当前看板的标题 +
    ///     <see cref="ExamStatusPolicy.FindFirst" /> 那一场），一场都没排、或者这一大场已经开考时
    ///     退回资源里那句示例——预览空着的话，用户根本不知道自己在调哪一行字。
    ///     屏幕上的口径是「开考后就不画」，预览跟着一样：那条规则变了却只改一边，预览就不是所见即所得了。
    /// </remarks>
    private string SampleExamCountdownText
    {
        get
        {
            var now = Now;

            if (IAppHost.TryGetService<IExamService>() is not { } examService
                || ExamStatusPolicy.FindFirst(examService.Exams) is not { } first
                || now >= first.StartAt)
            {
                return CR.Settings_Display_ExamCountdownSample;
            }

            return ExamTimeFormatter.FormatBoardCountdown(examService.ActiveBoard.Title, first.StartAt - now);
        }
    }
}

/// <summary>下拉框里的一项：<see cref="Value" /> 落盘用的枚举数值，<see cref="Name" /> 给人看的文案。</summary>
public sealed class DisplayChoice(int value, string name)
{
    public int Value { get; } = value;

    public string Name { get; } = name;
}

/// <summary>
///     一段自动进出的时段：开关、星期几、进出时刻、删掉这一段。
///     <para>
///         每次改动都往配置里写并立刻 <c>Save()</c>：时段是集合元素，
///         配置 handler 的自动保存订阅不到它们（见页面 VM 的注释）。
///     </para>
/// </summary>
public partial class DisplaySlotItem : ObservableObject
{
    private readonly DisplaySlot _slot;
    private readonly Action _changed;
    private readonly Action<DisplaySlotItem> _remove;

    /// <summary>正在把配置里的值刷到界面上：此时别把改动又写回去。</summary>
    private bool _suppress;

    public DisplaySlotItem(DisplaySlot slot, Action changed, Action<DisplaySlotItem> remove)
    {
        _slot = slot;
        _changed = changed;
        _remove = remove;

        DayChoices =
        [
            new DisplayDayChoice(DisplayDayFlags.Monday, CR.Settings_Display_DayMonday, value => SetDay(DisplayDayFlags.Monday, value)),
            new DisplayDayChoice(DisplayDayFlags.Tuesday, CR.Settings_Display_DayTuesday, value => SetDay(DisplayDayFlags.Tuesday, value)),
            new DisplayDayChoice(DisplayDayFlags.Wednesday, CR.Settings_Display_DayWednesday, value => SetDay(DisplayDayFlags.Wednesday, value)),
            new DisplayDayChoice(DisplayDayFlags.Thursday, CR.Settings_Display_DayThursday, value => SetDay(DisplayDayFlags.Thursday, value)),
            new DisplayDayChoice(DisplayDayFlags.Friday, CR.Settings_Display_DayFriday, value => SetDay(DisplayDayFlags.Friday, value)),
            new DisplayDayChoice(DisplayDayFlags.Saturday, CR.Settings_Display_DaySaturday, value => SetDay(DisplayDayFlags.Saturday, value)),
            new DisplayDayChoice(DisplayDayFlags.Sunday, CR.Settings_Display_DaySunday, value => SetDay(DisplayDayFlags.Sunday, value))
        ];

        _enabled = slot.Enabled;
        _enterTimeDraft = Draft(slot.EnterTime);
        _exitTimeDraft = Draft(slot.ExitTime);

        RefreshDays();
    }

    public DisplaySlot Slot => _slot;

    /// <summary>星期几那七颗小按钮。</summary>
    public IReadOnlyList<DisplayDayChoice> DayChoices { get; }

    [ObservableProperty] private bool _enabled;

    [ObservableProperty] private TimeSpan? _enterTimeDraft;

    [ObservableProperty] private TimeSpan? _exitTimeDraft;

    /// <summary>进和出是同一个时刻：这一段其实永远不会生效，界面上给一句提醒。</summary>
    public bool HasEmptyWarning => _slot.IsEmpty;

    public string EmptyWarning => CR.Settings_Display_SlotEmpty;

    partial void OnEnabledChanged(bool value)
    {
        if (_suppress)
            return;

        _slot.Enabled = value;
        Commit();
    }

    partial void OnEnterTimeDraftChanged(TimeSpan? value) => ApplyTimes();

    partial void OnExitTimeDraftChanged(TimeSpan? value) => ApplyTimes();

    /// <summary>把时刻草稿写回配置（TimePicker 失焦时页面也会调一次）。</summary>
    public void CommitTimes() => ApplyTimes();

    /// <summary>删掉这一段（由页面 VM 真正动集合，这里只转一手）。</summary>
    [RelayCommand]
    private void Remove() => _remove(this);

    private void ApplyTimes()
    {
        if (_suppress)
            return;

        // TimePicker 会反复把同一个值推回来（失焦、重排、滚动都会来一发），
        // 真变了才往配置写并落盘——否则页面滚一下就是几十次 Save。
        var changed = false;

        if (EnterTimeDraft is { } enter
            && (_slot.EnterHour != enter.Hours || _slot.EnterMinute != enter.Minutes))
        {
            _slot.EnterHour = enter.Hours;
            _slot.EnterMinute = enter.Minutes;
            changed = true;
        }

        if (ExitTimeDraft is { } exit
            && (_slot.ExitHour != exit.Hours || _slot.ExitMinute != exit.Minutes))
        {
            _slot.ExitHour = exit.Hours;
            _slot.ExitMinute = exit.Minutes;
            changed = true;
        }

        OnPropertyChanged(nameof(HasEmptyWarning));

        if (changed)
            Commit();
    }

    private void SetDay(DisplayDayFlags flag, bool value)
    {
        if (_suppress)
            return;

        SetDays(value ? _slot.Days | flag : _slot.Days & ~flag);
    }

    private void SetDays(DisplayDayFlags days)
    {
        _slot.Days = DisplayDayFlagsExtensions.Normalize(days);
        RefreshDays();
        Commit();
    }

    /// <summary>把配置里的星期几刷到那七颗按钮上。</summary>
    private void RefreshDays()
    {
        _suppress = true;
        foreach (var choice in DayChoices)
            choice.Refresh(_slot.Days.HasFlag(choice.Flag));

        _suppress = false;
    }

    private void Commit() => _changed();

    private static TimeSpan Draft(TimeOnly time) => new(time.Hour, time.Minute, 0);
}

/// <summary>星期几的一颗小按钮。</summary>
public partial class DisplayDayChoice : ObservableObject
{
    private readonly Action<bool> _write;

    public DisplayDayChoice(DisplayDayFlags flag, string label, Action<bool> write)
    {
        Flag = flag;
        Label = label;
        _write = write;
    }

    /// <summary>这一颗对应哪个星期（<see cref="DisplaySlotItem" /> 刷新时按它比对）。</summary>
    public DisplayDayFlags Flag { get; }

    public string Label { get; }

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _write(value);

    /// <summary>从配置刷新，不触发回写（回写由 <see cref="DisplaySlotItem" /> 自己压住）。</summary>
    public void Refresh(bool value) => IsSelected = value;
}

/// <summary>
///     一项「滑块 + 数值 + 拖动预览浮窗」。
///     <para>
///         值与配置之间隔着一层草稿（<see cref="Draft" />）：拖动只改草稿，
///         松手（页面 code-behind 收到指针抬起）才 <see cref="Commit" /> 写回配置。
///         理由见页面 VM 的注释——配置一改就落盘，直接绑会拖出一路写盘。
///     </para>
///     <para>
///         预览字号封顶 <see cref="PreviewMaxFontSize" />：480 号的字塞不进一个浮窗，
///         超过这个数就按 96 号画，并提示「实际更大」。
///     </para>
/// </summary>
public partial class DisplaySliderItem : ObservableObject
{
    /// <summary>预览浮窗里最多按多大画（再大浮窗也放不下）。</summary>
    public const double PreviewMaxFontSize = 96;

    private readonly Func<double> _read;
    private readonly Action<double> _write;
    private readonly Func<string> _sampleText;

    public DisplaySliderItem(
        string name,
        string description,
        double defaultValue,
        double minimum,
        double maximum,
        string unit,
        bool isClock,
        bool showsPreview,
        Func<string> sampleText,
        FontWeight previewFontWeight,
        Func<double> read,
        Action<double> write)
    {
        Name = name;
        Description = description;
        DefaultValue = defaultValue;
        Minimum = minimum;
        Maximum = maximum;
        Unit = unit;
        IsClock = isClock;
        ShowsPreview = showsPreview;
        _sampleText = sampleText;
        PreviewFontWeight = previewFontWeight;
        _read = read;
        _write = write;

        _draft = (decimal)Clamp(defaultValue);
    }

    public string Name { get; }

    public string Description { get; }

    public double DefaultValue { get; }

    public double Minimum { get; }

    public double Maximum { get; }

    /// <summary>数值后缀（「号」/「秒」），与数值一起显示在滑块右边。</summary>
    public string Unit { get; }

    /// <summary>这一项是不是时钟字号（决定夹取走哪一档上限）。</summary>
    public bool IsClock { get; }

    /// <summary>拖动时弹不弹预览浮窗（整秒数这类没有「大小」可看的项不弹）。</summary>
    public bool ShowsPreview { get; }

    /// <summary>浮窗里的示例文字：每次读都现算，换制式 / 换日期写法之后重画就行。</summary>
    public string SampleText => _sampleText();

    public FontWeight PreviewFontWeight { get; }

    [ObservableProperty] private decimal? _draft;

    [ObservableProperty] private bool _isDragging;

    /// <summary>滑块绑的值：草稿优先，没草稿时退回默认值。</summary>
    public double SliderValue
    {
        get => (double)(Draft ?? (decimal)DefaultValue);
        set
        {
            var rounded = Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), Minimum, Maximum);
            if (Draft is { } draft && (double)draft == rounded)
                return;

            Draft = (decimal)rounded;
        }
    }

    /// <summary>滑块右边那个数值文本（「96 号」）。</summary>
    public string ValueText => $"{SliderValue:0} {Unit}";

    /// <summary>预览浮窗按多大画（封顶 <see cref="PreviewMaxFontSize" />）。</summary>
    public double PreviewFontSize => Math.Min(SliderValue, PreviewMaxFontSize);

    /// <summary>实际值已经超过浮窗能画的上限，浮窗里要提示一句。</summary>
    public bool IsPreviewScaled => ShowsPreview && SliderValue > PreviewMaxFontSize;

    /// <summary>浮窗此时该不该弹：只有「拖着的、而且这一类有预览」时才弹。</summary>
    public bool IsPreviewVisible => ShowsPreview && IsDragging;

    partial void OnDraftChanged(decimal? value)
    {
        OnPropertyChanged(nameof(SliderValue));
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(PreviewFontSize));
        OnPropertyChanged(nameof(IsPreviewScaled));
    }

    partial void OnIsDraggingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPreviewVisible));

        // 浮窗刚弹出来时要拿到最新的示例文字（比如刚换过 12 小时制）。
        OnPropertyChanged(nameof(SampleText));
    }

    /// <summary>示例文字变了（制式 / 日期写法 / 关闭秒）：让浮窗重读一次。</summary>
    public void RefreshSample() => OnPropertyChanged(nameof(SampleText));

    /// <summary>把草稿写回配置（夹取之后再写，界面也同步成夹过的值）。</summary>
    public void Commit()
    {
        var current = _read();
        var value = Draft is { } draft ? Clamp((double)draft) : DefaultValue;

        if (Math.Abs(current - value) > double.Epsilon)
            _write(value);

        var normalized = (decimal)value;
        if (Draft != normalized)
            Draft = normalized;
    }

    /// <summary>草稿拉回配置当前值（不动配置）。</summary>
    public void ResetDraft()
    {
        var value = (decimal)Clamp(_read());
        if (Draft != value)
            Draft = value;
    }

    /// <summary>
    ///     夹进这一项自己的区间。
    ///     <para>
    ///         ⚠️ 不要借用 <c>ExamBoardTextStyle.Clamp</c>：那是「字号」的 8–120，
    ///         拿来夹光标秒数会把默认的 3 秒抬成 8 秒（默认值当场被改写）。
    ///         字号的两档上限（120 / 480）在本项构造时已经变成了 <see cref="Maximum" />。
    ///     </para>
    /// </summary>
    private double Clamp(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, Minimum, Maximum) : DefaultValue;
}
