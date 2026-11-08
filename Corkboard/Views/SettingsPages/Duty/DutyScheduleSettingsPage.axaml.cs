using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Duty;

namespace Corkboard.Views.SettingsPages.Duty;

/// <summary>
///     「排班规则」设置页：接管「打开值日表 / 去配小组 / 打开节假日设置」三个跳转，
///     其余表单与保存都在 ViewModel 里。
/// </summary>
[PageInfo("settings.duty.schedule", FluentIcons.CalendarFilled, "settings.duty.group")]
public partial class DutyScheduleSettingsPage : UserControl
{
    public DutyScheduleSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<DutyScheduleSettingsPageViewModel>();

        // ViewModel 是 transient、配置 handler 与领域服务是单例：订阅必须在离开可视树时断开。
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private DutyScheduleSettingsPageViewModel? ViewModel =>
        DataContext as DutyScheduleSettingsPageViewModel;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        viewModel.Attach();
        viewModel.RosterRequested += OnRosterRequested;
        viewModel.GroupsRequested += OnGroupsRequested;
        viewModel.HolidaysRequested += OnHolidaysRequested;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        viewModel.Detach();
        viewModel.RosterRequested -= OnRosterRequested;
        viewModel.GroupsRequested -= OnGroupsRequested;
        viewModel.HolidaysRequested -= OnHolidaysRequested;
    }

    private void OnRosterRequested() => App.ShowDutyRosterWindow();

    private void OnGroupsRequested() => App.ShowSettingsWindow(AppConsts.SettingsDutyGroupsPageId);

    /// <summary>节假日是独立窗口：它要联网、要按年缓存、要手工增减，不适合塞进设置页。</summary>
    private void OnHolidaysRequested() => App.ShowDutyHolidayWindow();

    /// <summary>「重新读取」：把页面上的控件重新按落盘数据铺一遍，丢弃还没保存的改动。</summary>
    private void OnRefreshClick(object? sender, RoutedEventArgs e) => ViewModel?.Reload();
}
