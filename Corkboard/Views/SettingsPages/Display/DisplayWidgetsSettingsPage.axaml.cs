using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Display;
using Corkboard.Views;

namespace Corkboard.Views.SettingsPages.Display;

/// <summary>
///     「设置 → 看板模式 → 一言与天气」页。
///     <para>
///         一言只接官方 v1、天气只走小米，两行都只出现在看板模式这块全屏上。
///         城市搜索是异步的，失败只在页面上写一句话，不弹窗。
///     </para>
/// </summary>
[PageInfo("settings.display.widgets", FluentIcons.CloudFilled, "settings.display.group")]
public partial class DisplayWidgetsSettingsPage : UserControl
{
    /// <summary>
    ///     换城市抽屉的内容放在 <see cref="UserControl.Resources" /> 里（键名与 XAML 里的一致）。
    /// </summary>
    private const string CityPickerDrawerKey = "CityPickerDrawer";

    public DisplayWidgetsSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<DisplayWidgetsSettingsPageViewModel>();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private DisplayWidgetsSettingsPageViewModel? ViewModel => DataContext as DisplayWidgetsSettingsPageViewModel;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // 每次进来重读配置：别的页（或别的窗口）可能刚改过城市与间隔。
        ViewModel?.Reload();

        if (ViewModel is not { } viewModel)
            return;

        // 挑定城市之后由 VM 说一声，页面负责把抽屉收起来（Esc / 点遮罩由宿主自己收）。
        viewModel.CityPicked -= OnCityPicked;
        viewModel.CityPicked += OnCityPicked;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
            viewModel.CityPicked -= OnCityPicked;

        // 附加查询串是草稿属性，离开页面时补一次提交（失焦时也会提一次）。
        ViewModel?.CommitQuoteExtraQuery();

        // 抽屉挂在设置壳上，离开这一页不能把它留在那儿盖住下一张页面。
        SettingsView.Current?.CloseDrawer();
    }

    private void OnQuoteExtraQueryLostFocus(object? sender, RoutedEventArgs e) => ViewModel?.CommitQuoteExtraQuery();

    /// <summary>「换城市」：把城市搜索塞进设置壳的右侧抽屉（与「关于」页的贡献人员名单同一个宿主）。</summary>
    private void ChangeCity_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || Resources[CityPickerDrawerKey] is not Control drawer)
            return;

        viewModel.BeginCityPick();

        // 抽屉内容在页面资源里，DataContext 得自己给（它被搬到壳上，不再继承本页的 DataContext）。
        drawer.DataContext = viewModel;
        SettingsView.Current?.OpenDrawer(drawer);
    }

    private void CloseDrawer_OnClick(object? sender, RoutedEventArgs e) => SettingsView.Current?.CloseDrawer();

    private void OnCityPicked(object? sender, EventArgs e) => SettingsView.Current?.CloseDrawer();
}
