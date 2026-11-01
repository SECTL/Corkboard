using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

    /// <summary>类别浮层（<see cref="Popup" />）的 x:Name，与 XAML 里的一致。</summary>
    private const string CategoryPopupKey = "QuoteCategoryPopup";

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

        // 这几个都是草稿属性（间隔、屏蔽词、附加查询串），离开页面时统一补一次提交。
        ViewModel?.CommitQuoteRefresh();
        ViewModel?.CommitWeatherRefresh();
        ViewModel?.CommitQuoteBlockedWordsCommand.Execute(null);
        ViewModel?.CommitQuoteExtraQuery();

        // 抽屉挂在设置壳上，离开这一页不能把它留在那儿盖住下一张页面。
        SettingsView.Current?.CloseDrawer();
    }

    private void OnQuoteExtraQueryLostFocus(object? sender, RoutedEventArgs e) => ViewModel?.CommitQuoteExtraQuery();

    private void OnQuoteRefreshLostFocus(object? sender, RoutedEventArgs e) => ViewModel?.CommitQuoteRefresh();

    private void OnWeatherRefreshLostFocus(object? sender, RoutedEventArgs e) => ViewModel?.CommitWeatherRefresh();

    /// <summary>
    ///     类别那颗按钮：点开 / 收起浮层里的复选框（和「窗口与收纳」页的九宫格同一个写法）。
    ///     ⚠️ 用 <see cref="Popup" /> 而不是 <c>Button.Flyout</c>：Flyout 的内容在独立弹窗里，
    ///     DataContext 不跟着按钮走，勾选框会写不进配置。
    /// </summary>
    private void OnQuoteCategoryPickerClick(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<Popup>(CategoryPopupKey) is { } popup)
            popup.IsOpen = !popup.IsOpen;
    }

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
