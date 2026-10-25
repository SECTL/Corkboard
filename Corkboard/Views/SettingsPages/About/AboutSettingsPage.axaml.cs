using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using Corkboard.Core;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Enums;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages;
using Corkboard.Views;

namespace Corkboard.Views.SettingsPages.About;

/// <summary>
///     关于页：页头海报（先空着）+ 应用信息 / 作者 / 鸣谢 / 版本信息几张卡。
/// </summary>
/// <remarks>
///     版式照上游 SecRandom-C 的关于页（含会随滚动收起的页头、整行可点的作者链接、
///     右下角滑出的贡献人员名单）。和上游不同的地方：
///     · 上游那个「连点组织图标 20 次开内幕设置」的后门没搬（Corkboard 没有 toast，也没这套调试页）；
///     · 贡献人员拉的是 Corkboard 自己的仓库。
/// </remarks>
[PageInfo("settings.about", FluentIcons.InfoFilled, null, PageLocation.Bottom)]
public partial class AboutSettingsPage : UserControl
{
    private const string ContributorsDrawerKey = "ContributorsDrawer";

    private readonly AboutSettingsPageViewModel _viewModel;

    public AboutSettingsPage()
    {
        InitializeComponent();

        _viewModel = IAppHost.GetService<AboutSettingsPageViewModel>();
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.RefreshOnlineStatusAsync();
        }
        catch (Exception)
        {
            // 在线人数拿不到只是少一个数字，页面本身照常用。
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _viewModel.Detach();

        // 页面离开时抽屉不该留在壳上：否则从关于页翻走之后，那块名单还盖在下一张页面上。
        SettingsView.Current?.CloseDrawer();
    }

    /// <summary>作者卡上的 bilibili / 爱发电那两行：<c>CommandParameter</c> 就是地址。</summary>
    private void SettingsItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is FASettingsExpanderItem { CommandParameter: string uri })
            _viewModel.OpenUriCommand.Execute(uri);
    }

    /// <summary>「贡献人员」：把名单塞进设置壳的右侧抽屉并拉一次数据。</summary>
    private void Contributors_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Resources[ContributorsDrawerKey] is not Control drawer)
            return;

        drawer.DataContext = _viewModel;
        SettingsView.Current?.OpenDrawer(drawer);

        _viewModel.RefreshContributorsCommand.Execute(null);
    }

    private void RefreshContributors_OnClick(object? sender, RoutedEventArgs e) =>
        _viewModel.RefreshContributorsCommand.Execute(null);

    private void CloseDrawer_OnClick(object? sender, RoutedEventArgs e) =>
        SettingsView.Current?.CloseDrawer();

    /// <summary>抽屉里点某个人：打开他的 GitHub 主页。</summary>
    private void OpenContributorProfile_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: string profileUrl })
            _viewModel.OpenUriCommand.Execute(profileUrl);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
