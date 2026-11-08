using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using Corkboard.Core.Abstraction;
using Corkboard.Core.DutyRoster;
using Corkboard.Services.Desktop;
using Corkboard.Services.Ui;
using Corkboard.ViewModels.DutyRoster;

namespace Corkboard.Views.Duty;

/// <summary>
///     「值日表」独立窗口：周视图 / 月视图的排班表与排班调整表单。
///     <para>
///         入口：托盘菜单的「值日表」，以及设置 → 值日表那一组（页内还会加一颗「打开值日表」按钮）。
///         单实例：窗口已经开着时由 <see cref="App.ShowDutyRosterWindow" /> 拉到前面，不另开一个。
///     </para>
///     <para>
///         这扇窗在任务栏里有按钮，图标必须是 Corkboard 自己的多尺寸 ICO（见 <see cref="AppIcon" />）。
///     </para>
///     <para>
///         ⚠️ 弹层宿主必须注册在这扇窗上：弹层服务只有一个，默认画在作业板主窗口上，
///         用户眼前（这扇窗）就什么都看不见了。
///     </para>
///     <para>
///         ViewModel 在这里创建（键控 DI）：窗口关闭时 <c>Detach</c> 退订单例的 Changed 事件，
///         transient 的 VM 才收得掉。
///     </para>
/// </summary>
public partial class DutyRosterWindow : FAAppWindow
{
    private readonly PageOverlayService _overlay;
    private readonly PageOverlayHost _overlayHost;
    private readonly DutyRosterViewModel _viewModel;

    public DutyRosterWindow()
    {
        InitializeComponent();

        ((Window)this).Icon = AppIcon.LoadWindowIcon();

        // 排班数据是业务数据（data/duty/duty.json），VM 走 DI 拿单例服务。
        _viewModel = IAppHost.GetService<DutyRosterViewModel>();
        _viewModel.HolidayCalendar = IAppHost.TryGetService<Core.DutyRoster.IDutyHolidayCalendar>();
        DataContext = _viewModel;

        // 弹层画在**这扇窗**里（层级压过壳），见类注释。
        _overlay = IAppHost.GetService<PageOverlayService>();
        _overlayHost = _overlay.CreateHost(this, PageOverlayService.FullScreenHostPriority);
        _overlayHost.PropertyChanged += OnOverlayHostChanged;

        // DataGrid 的七列列头是「星期 + 月/日」，绑定做不了这种拼装：VM 每次重算表格后由这里赋一次。
        _viewModel.WeekHeaders.CollectionChanged += OnTableColumnsChanged;
        _viewModel.MonthDayNames.CollectionChanged += OnTableColumnsChanged;
        UpdateGridHeaders();

        Closed += OnClosed;
    }

    /// <summary>宿主那份状态变了就把遮罩与内容贴上去（只有最上面那个宿主会收到 IsOpen）。</summary>
    private void OnOverlayHostChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (null or nameof(PageOverlayHost.IsOpen)))
            return;

        OverlayScrim.IsVisible = _overlayHost.IsOpen;
        OverlayContentHost.Content = _overlayHost.Content;
    }

    /// <summary>点周表格 / 月历的某一天：打开右侧日详情（点空白处不算）。</summary>
    private void OnDayTapped(object? sender, TappedEventArgs e)
    {
        var date = (sender as Control)?.DataContext switch
        {
            DutyWeekCell cell => cell.Date,
            DutyMonthCell monthCell => monthCell.Date,
            _ => default
        };

        if (date != default)
            _viewModel.OpenDay(date);
    }

    /// <summary>打开设置里的「小组与职务」页：组和职务是排班的前置条件。</summary>
    private void OnOpenSettingsClick(object? sender, RoutedEventArgs e) =>
        App.ShowSettingsWindow(AppConsts.SettingsDutyGroupsPageId);

    /// <summary>打开「节假日」独立窗口：跳不跳节假日、哪几天补班都在那边配。</summary>
    private void OnOpenHolidaysClick(object? sender, RoutedEventArgs e) => App.ShowDutyHolidayWindow();

    /// <summary>VM 重算完一屏表格（清空 + 重新填充）后，把两套列头重新赋一遍。</summary>
    private void OnTableColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(UpdateGridHeaders, DispatcherPriority.Background);

    /// <summary>
    ///     周视图的列头写成「周一 11/10」，月视图的列头写成「周一」——两者都跟「周起始日」配置走，
    ///     所以不在 XAML 里写死，统一由这里按 VM 算好的顺序赋给 <c>DataGridColumn.Header</c>。
    /// </summary>
    private void UpdateGridHeaders()
    {
        for (var i = 0; i < 7; i++)
        {
            var columnIndex = i + 1; // 周视图第 0 列是「职务 / 星期」行标题
            if (columnIndex < WeekGrid.Columns.Count)
            {
                var header = i < _viewModel.WeekHeaders.Count ? _viewModel.WeekHeaders[i] : null;
                WeekGrid.Columns[columnIndex].Header = header is null
                    ? string.Empty
                    : $"{header.DayName} {header.DateText}";
            }

            if (i < MonthGrid.Columns.Count)
                MonthGrid.Columns[i].Header = i < _viewModel.MonthDayNames.Count
                    ? _viewModel.MonthDayNames[i]
                    : string.Empty;
        }
    }

    // ── 临时层：指定 / 对调 / 加罚 / 个人兑换 ──────────────────────────────────

    private void OnToggleDayOffClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.ToggleDayOverride();
    }

    private async void OnAssignGroupClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.GroupChoices.Count == 0)
        {
            await ShowInfoAsync("还没有小组", "先去设置里建小组，再回来指定。");
            return;
        }

        var groupCombo = ChoiceCombo(_viewModel.GroupChoices);
        var permanent = new CheckBox { Content = "永久（从这一天起一直生效）" };
        var panel = new StackPanel
        {
            Spacing = 8,
            Width = 360,
            Children = { TextBlock("这一天由哪个小组打扫"), groupCombo, permanent }
        };

        if (!await ShowFormAsync("指定小组", panel))
            return;

        if (groupCombo.SelectedItem is DutyChoice choice)
            _viewModel.ApplyGroupAssign(choice.Id, permanent.IsChecked == true);
    }

    private async void OnGroupSwapClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.GroupChoices.Count < 2)
        {
            await ShowInfoAsync("小组不够", "对调至少要两个小组。");
            return;
        }

        var first = ChoiceCombo(_viewModel.GroupChoices);
        var second = ChoiceCombo(_viewModel.GroupChoices, selectedIndex: 1);
        var permanent = new CheckBox { Content = "永久（从这一天起一直生效）" };
        var panel = new StackPanel
        {
            Spacing = 8,
            Width = 360,
            Children =
            {
                TextBlock("把这两个小组这一天的值日对调"),
                first,
                TextBlock("换成"),
                second,
                permanent
            }
        };

        if (!await ShowFormAsync("小组对调", panel))
            return;

        if (first.SelectedItem is DutyChoice left && second.SelectedItem is DutyChoice right)
            _viewModel.ApplyGroupSwap(left.Id, right.Id, permanent.IsChecked == true);
    }

    private async void OnGroupPenaltyClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.GroupChoices.Count == 0)
        {
            await ShowInfoAsync("还没有小组", "先去设置里建小组，再回来加罚。");
            return;
        }

        var groupCombo = ChoiceCombo(_viewModel.GroupChoices);
        var days = new NumericUpDown { Minimum = 1, Maximum = 14, Value = 1, Width = 140 };
        var modeCombo = new ComboBox
        {
            ItemsSource = new[] { "吞掉：占用后面小组的日子", "顺延：后面所有小组整体往后推" },
            SelectedIndex = 0,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            IsEnabled = _viewModel.DocumentModeIsRotation
        };
        var permanent = new CheckBox { Content = "永久（从这一天起一直生效）" };
        var panel = new StackPanel
        {
            Spacing = 8,
            Width = 380,
            Children =
            {
                TextBlock("哪个小组要加罚"),
                groupCombo,
                TextBlock("延长几个值日天"),
                days,
                TextBlock("后面小组怎么处理（固定周几模式只有吞掉）"),
                modeCombo,
                permanent
            }
        };

        if (!await ShowFormAsync("小组加罚", panel))
            return;

        if (groupCombo.SelectedItem is DutyChoice choice)
        {
            var mode = modeCombo.SelectedIndex == 1 ? DutyPenaltyMode.PushBack : DutyPenaltyMode.Swallow;
            _viewModel.ApplyGroupPenalty(choice.Id, (int)(days.Value ?? 1), mode, permanent.IsChecked == true);
        }
    }

    private async void OnPersonSwapClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.PostChoices.Count == 0 || _viewModel.MemberChoices.Count == 0)
        {
            await ShowInfoAsync("还缺前置数据", "先配好职务和名单，再回来兑换。");
            return;
        }

        var postCombo = ChoiceCombo(_viewModel.PostChoices);
        var fromChoices = new List<DutyChoice> { new() { Id = Guid.Empty, Name = "（不指定，直接换成下面的人）" } };
        fromChoices.AddRange(_viewModel.MemberChoices);
        var fromCombo = ChoiceCombo(fromChoices);
        var toCombo = ChoiceCombo(_viewModel.MemberChoices);
        var permanent = new CheckBox { Content = "永久（从这一天起一直生效）" };
        var panel = new StackPanel
        {
            Spacing = 8,
            Width = 380,
            Children =
            {
                TextBlock("哪个职务"),
                postCombo,
                TextBlock("把谁换下来（可选）"),
                fromCombo,
                TextBlock("换成谁"),
                toCombo,
                permanent
            }
        };

        if (!await ShowFormAsync("个人兑换", panel))
            return;

        if (postCombo.SelectedItem is DutyChoice post
            && fromCombo.SelectedItem is DutyChoice from
            && toCombo.SelectedItem is DutyChoice to)
        {
            _viewModel.ApplyPersonSwap(post.Id, from.Id, to.Id, permanent.IsChecked == true);
        }
    }

    private async void OnPersonAddClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.PostChoices.Count == 0 || _viewModel.MemberChoices.Count == 0)
        {
            await ShowInfoAsync("还缺前置数据", "先配好职务和名单，再回来增设。");
            return;
        }

        var postCombo = ChoiceCombo(_viewModel.PostChoices);
        var memberCombo = ChoiceCombo(_viewModel.MemberChoices);
        var permanent = new CheckBox { Content = "永久（从这一天起一直生效）" };
        var panel = new StackPanel
        {
            Spacing = 8,
            Width = 360,
            Children =
            {
                TextBlock("哪个职务要加人"),
                postCombo,
                TextBlock("加谁"),
                memberCombo,
                permanent
            }
        };

        if (!await ShowFormAsync("个人增设", panel))
            return;

        if (postCombo.SelectedItem is DutyChoice post && memberCombo.SelectedItem is DutyChoice member)
            _viewModel.ApplyPersonAdd(post.Id, member.Id, permanent.IsChecked == true);
    }

    private static TextBlock TextBlock(string text) => new() { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap };

    private static ComboBox ChoiceCombo(IReadOnlyList<DutyChoice> choices, int selectedIndex = 0) => new()
    {
        ItemsSource = choices,
        SelectedIndex = selectedIndex,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
    };

    private async Task<bool> ShowFormAsync(string title, Control content, string primaryText = "确定")
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return false;

        var dialog = new FAContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primaryText,
            CloseButtonText = "取消"
        };

        return await dialog.ShowAsync(topLevel) == FAContentDialogResult.Primary;
    }

    private async Task ShowInfoAsync(string title, string message)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var dialog = new FAContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "知道了"
        };

        await dialog.ShowAsync(topLevel);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.WeekHeaders.CollectionChanged -= OnTableColumnsChanged;
        _viewModel.MonthDayNames.CollectionChanged -= OnTableColumnsChanged;
        _viewModel.Detach();
        _overlayHost.PropertyChanged -= OnOverlayHostChanged;
        _overlay.ReleaseHost(_overlayHost);
    }
}
