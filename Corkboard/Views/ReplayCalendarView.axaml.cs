using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Corkboard.Services.Board;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views;

/// <summary>
///     「回放时刻」的月历浮层：上面是月历（7 列星期头 + 6 行日期格），选中某一天之后
///     下面列出那天所有停靠点（一颗胶囊 = 一个 <see cref="BoardReplayStopItem" />，显示 HH:mm:ss）。
///     <para>
///         它只消费 <see cref="BoardReplayService" /> 的公开接口（<c>Stops</c> / <c>SelectedStop</c>），
///         用户点中某颗胶囊就把 <c>SelectedStop</c> 写回去，再由壳（<see cref="MainView" />）收起浮层。
///     </para>
///     <para>
///         停靠点按「本地日期」分组：同一<b>天</b>里的所有时刻都排在那一格下面。打开浮层时定位到
///         当前选中的停靠点（还没选过就取最新的一颗）所在月 / 日；板子变了导致停靠点整份重建时，
///         只要用户正选中的那天还在，就把选中的那天留住，否则退回当前停靠点所在的那天。
///     </para>
///     <para>
///         订阅（<c>Stops.CollectionChanged</c> 与 <c>Replay.PropertyChanged</c>）挂在控件的
///         attached / detached 上：浮层的 Popup 一开一合对应挂上 / 摘掉，不会往单例上留处理器。
///     </para>
/// </summary>
public partial class ReplayCalendarView : UserControl
{
    /// <summary>一周七天。</summary>
    private const int DaysPerWeek = 7;

    /// <summary>月历固定画 6 行（任何月份都能装下，格子不会跳来跳去）。</summary>
    private const int WeeksShown = 6;

    /// <summary>日期格总数。</summary>
    private const int DayCellCount = DaysPerWeek * WeeksShown;

    /// <summary>42 颗日期按钮：XAML 里只留一个空 <c>UniformGrid</c>，内容在这里代码生成。</summary>
    private readonly Button?[] _dayButtons = new Button?[DayCellCount];

    private readonly TextBlock? _monthText;
    private readonly UniformGrid? _weekdayHeader;
    private readonly UniformGrid? _dayGrid;
    private readonly StackPanel? _dayStopsPanel;
    private readonly WrapPanel? _dayStopsHost;
    private readonly TextBlock? _pickDayHint;

    private BoardReplayService? _replay;
    private bool _isAttached;
    private bool _isHooked;
    private DateTime _visibleMonth;
    private DateTime? _selectedDate;

    public ReplayCalendarView()
    {
        InitializeComponent();

        _monthText = this.FindControl<TextBlock>("MonthText");
        _weekdayHeader = this.FindControl<UniformGrid>("WeekdayHeader");
        _dayGrid = this.FindControl<UniformGrid>("DayGrid");
        _dayStopsPanel = this.FindControl<StackPanel>("DayStopsPanel");
        _dayStopsHost = this.FindControl<WrapPanel>("DayStopsHost");
        _pickDayHint = this.FindControl<TextBlock>("PickDayHint");

        BuildWeekdayHeader();
        BuildDayCells();

        // 还没挂树 / 还没拿到回放状态之前，先把月份条写成「今年这个月」，
        // 免得浮层第一帧是一行空白（真正的月份在 SyncToSelection 里再定位）。
        var today = DateTime.Today;
        _visibleMonth = new DateTime(today.Year, today.Month, 1);
        RenderMonthTitle();
    }

    /// <summary>用户点了某一天的某个时刻：壳据此把浮层收起来（点完马上能看到那一刻的板子）。</summary>
    public event EventHandler? StopSelected;

    /// <summary>
    ///     回放状态宿主（与主界面壳共享同一个单例）。由壳在构造浮层内容时注入；
    ///     换人或置空时把旧订阅摘干净，挂树中的话再按新实例重新订阅 + 重画。
    /// </summary>
    public BoardReplayService? Replay
    {
        get => _replay;
        set
        {
            if (ReferenceEquals(_replay, value))
                return;

            Unhook();
            _replay = value;

            if (_isAttached)
            {
                Hook();
                SyncToSelection();
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _isAttached = true;
        Hook();
        SyncToSelection();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _isAttached = false;
        Unhook();
    }

    /// <summary>把订阅挂到回放服务上（只挂一次；浮层没打开的时候不挂）。</summary>
    private void Hook()
    {
        if (_isHooked || _replay is null)
            return;

        _replay.Stops.CollectionChanged += OnStopsChanged;
        _replay.PropertyChanged += OnReplayPropertyChanged;
        _isHooked = true;
    }

    /// <summary>摘掉订阅，别往单例上留处理器（浮层会被反复打开关闭）。</summary>
    private void Unhook()
    {
        if (!_isHooked || _replay is null)
            return;

        _replay.Stops.CollectionChanged -= OnStopsChanged;
        _replay.PropertyChanged -= OnReplayPropertyChanged;
        _isHooked = false;
    }

    /// <summary>停靠点整份重建（<c>Clear</c> + 逐条 <c>Add</c>）时刷新。</summary>
    private void OnStopsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 重建过程中用户正选中的那天会短暂地没有停靠点，那不算「那天没了」：
        // 只有新的一整份里确实找不到它，才退回当前停靠点所在的那天。
        if (_selectedDate is { } date && !BuildDayMap().ContainsKey(date))
        {
            SyncToSelection();
            return;
        }

        Render();
    }

    /// <summary>选中的停靠点变了（用户在浮层里点、或服务自己把选择复位到最新）：跟着定位。</summary>
    private void OnReplayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BoardReplayService.SelectedStop))
            SyncToSelection();
    }

    /// <summary>把当前选中的停靠点（没选就取最新的一颗）所在的月 / 日定位出来，再重画。</summary>
    private void SyncToSelection()
    {
        var target = _replay?.SelectedStop;
        if (target is null && _replay is { Stops.Count: > 0 })
            target = _replay.Stops[^1];

        if (target is { } stop)
        {
            var date = stop.At.LocalDateTime.Date;
            _selectedDate = date;
            _visibleMonth = new DateTime(date.Year, date.Month, 1);
        }

        Render();
    }

    /// <summary>整块重画：月份条、42 个日期格、下方的时刻胶囊。</summary>
    private void Render()
    {
        if (_replay is null)
            return;

        var dayMap = BuildDayMap();
        RenderMonthTitle();
        RenderDayCells(dayMap);
        RenderDayStops(dayMap);
    }

    /// <summary>
    ///     按本地日期把停靠点分组。<c>Stops</c> 本身按时刻升序，所以每组里也是时间顺序，
    ///     胶囊从左到右就是先后顺序。
    /// </summary>
    private Dictionary<DateTime, List<BoardReplayStopItem>> BuildDayMap()
    {
        var map = new Dictionary<DateTime, List<BoardReplayStopItem>>();
        if (_replay is null)
            return map;

        foreach (var stop in _replay.Stops)
        {
            var date = stop.At.LocalDateTime.Date;
            if (!map.TryGetValue(date, out var stops))
            {
                stops = [];
                map[date] = stops;
            }

            stops.Add(stop);
        }

        return map;
    }

    private void RenderMonthTitle()
    {
        if (_monthText is null)
            return;

        _monthText.Text = string.Format(
            CultureInfo.CurrentCulture,
            CR.Board_ReplayMonthFormat,
            _visibleMonth);
    }

    private void RenderDayCells(IReadOnlyDictionary<DateTime, List<BoardReplayStopItem>> dayMap)
    {
        // 周一为首列：列的序号 = (DayOfWeek + 6) % 7（周日 = 6）。
        var firstCell = _visibleMonth.AddDays(-MondayIndex(_visibleMonth));

        for (var i = 0; i < _dayButtons.Length; i++)
        {
            var button = _dayButtons[i];
            if (button is null)
                continue;

            var date = firstCell.AddDays(i);
            var inMonth = date.Year == _visibleMonth.Year && date.Month == _visibleMonth.Month;
            var hasStops = dayMap.ContainsKey(date);
            var selected = inMonth && _selectedDate == date;

            button.Tag = date;
            button.Content = date.Day.ToString(CultureInfo.CurrentCulture);
            // 只有「本月 + 有停靠点」的日子能点：不在本月的变淡，本月没停靠点的也没有可看的时刻。
            button.IsEnabled = inMonth && hasStops;

            SetClass(button, "outside", !inMonth);
            SetClass(button, "has-stops", hasStops);
            SetClass(button, "selected", selected);
        }
    }

    /// <summary>重画下方「当天的时刻」：选中那天有停靠点就排胶囊，否则显示「先选一个有作业的日期」。</summary>
    private void RenderDayStops(IReadOnlyDictionary<DateTime, List<BoardReplayStopItem>> dayMap)
    {
        List<BoardReplayStopItem>? stops = null;
        if (_selectedDate is { } date)
            dayMap.TryGetValue(date, out stops);

        if (stops is not { Count: > 0 })
        {
            if (_dayStopsPanel is not null)
                _dayStopsPanel.IsVisible = false;

            if (_pickDayHint is not null)
                _pickDayHint.IsVisible = true;

            return;
        }

        if (_pickDayHint is not null)
            _pickDayHint.IsVisible = false;

        if (_dayStopsPanel is not null)
            _dayStopsPanel.IsVisible = true;

        if (_dayStopsHost is null)
            return;

        _dayStopsHost.Children.Clear();
        foreach (var stop in stops)
            _dayStopsHost.Children.Add(CreateStopButton(stop));
    }

    /// <summary>
    ///     一颗时刻胶囊：文字是 HH:mm:ss，ToolTip 里放完整的停靠点描述（日期 + 条数），
    ///     正在看的那颗加 <c>.current</c> 高亮。
    /// </summary>
    private Button CreateStopButton(BoardReplayStopItem stop)
    {
        var button = new Button
        {
            Content = stop.At.LocalDateTime.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
            Tag = stop,
            Classes = { "stop" }
        };

        ToolTip.SetTip(button, stop.DisplayName);
        SetClass(button, "current", ReferenceEquals(_replay?.SelectedStop, stop));
        button.Click += StopButton_OnClick;
        return button;
    }

    /// <summary>点某颗时刻胶囊：把选择写回回放状态，再通知壳收起浮层。</summary>
    private void StopButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BoardReplayStopItem stop } || _replay is null)
            return;

        _replay.SelectedStop = stop;
        StopSelected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>点日期格：把选中日期挪过去，下面换成那天的时刻。</summary>
    private void DayButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DateTime date })
            return;

        _selectedDate = date;
        Render();
    }

    private void PrevMonthButton_OnClick(object? sender, RoutedEventArgs e) => ChangeMonth(-1);

    private void NextMonthButton_OnClick(object? sender, RoutedEventArgs e) => ChangeMonth(1);

    private void ChangeMonth(int offset)
    {
        _visibleMonth = _visibleMonth.AddMonths(offset);
        Render();
    }

    /// <summary>周一为首列时，<paramref name="date" /> 在那一周里排第几列。</summary>
    private static int MondayIndex(DateTime date) => ((int)date.DayOfWeek + 6) % 7;

    /// <summary>
    ///     星期头文案：<c>Board_Weekday_*</c> 在中文里是「星期一」（三个字，正好放得下），
    ///     在英文里是完整的 <c>Wednesday</c>（9 个字母，7 列头放不下），所以长的缩成前三个字母。
    /// </summary>
    private static string ShortenWeekday(string name) => name.Length > 3 ? name[..3] : name;

    /// <summary>
    ///     Avalonia 的 <c>Classes</c> 没有「按布尔置位」的公开方法，这里补一个，
    ///     免得每次刷新都 Add 出一堆重复类名。
    /// </summary>
    private static void SetClass(Control control, string name, bool value)
    {
        if (value)
        {
            if (!control.Classes.Contains(name))
                control.Classes.Add(name);
        }
        else
        {
            control.Classes.Remove(name);
        }
    }

    /// <summary>星期头：7 个 <c>TextBlock</c>，周一起（XAML 里只有一个空 UniformGrid）。</summary>
    private void BuildWeekdayHeader()
    {
        if (_weekdayHeader is null)
            return;

        string[] names =
        [
            CR.Board_Weekday_Monday,
            CR.Board_Weekday_Tuesday,
            CR.Board_Weekday_Wednesday,
            CR.Board_Weekday_Thursday,
            CR.Board_Weekday_Friday,
            CR.Board_Weekday_Saturday,
            CR.Board_Weekday_Sunday
        ];

        foreach (var name in names)
        {
            _weekdayHeader.Children.Add(new TextBlock
            {
                Text = ShortenWeekday(name),
                FontSize = 10,
                Opacity = 0.7,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            });
        }
    }

    /// <summary>日期格骨架：42 颗按钮先生成好，刷新时只改文字 / 样式 / 可点状态。</summary>
    private void BuildDayCells()
    {
        if (_dayGrid is null)
            return;

        for (var i = 0; i < DayCellCount; i++)
        {
            var button = new Button
            {
                Classes = { "day" }
            };
            button.Click += DayButton_OnClick;

            _dayButtons[i] = button;
            _dayGrid.Children.Add(button);
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
