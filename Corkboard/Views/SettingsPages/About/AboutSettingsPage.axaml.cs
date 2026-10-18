using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Enums;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages;
using FluentAvalonia.UI.Controls;

namespace Corkboard.Views.SettingsPages.About;

// 第三个参数是 GroupId（本页不参与分组，所以传 null），第四个是导航位置：
// 关于页沉在设置导航栏底部（页脚区），而不是跟着上半区那几项排。
[PageInfo("settings.about", FluentIcons.InfoFilled, null, PageLocation.Bottom)]
public partial class AboutSettingsPage : UserControl
{
    public AboutSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<AboutSettingsPageViewModel>();

        // 页面是 transient 的：打开时查一次在线人数，离开时断开对单例服务的订阅。
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AboutSettingsPageViewModel viewModel)
            return;

        try
        {
            await viewModel.RefreshOnlineStatusAsync();
        }
        catch (Exception)
        {
            // 在线人数只是展示：查不到就保留占位符，不打扰用户。
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AboutSettingsPageViewModel viewModel)
            viewModel.Detach();
    }

    /// <summary>
    ///     整行可点的链接项（作者那两张卡）：地址写在 XAML 的 <c>CommandParameter</c> 上，
    ///     这里只是把它转交给 ViewModel 打开，页面自身不碰原生启动 API。
    /// </summary>
    private void SettingsItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is FASettingsExpanderItem { CommandParameter: string uri }
            && DataContext is AboutSettingsPageViewModel viewModel)
        {
            viewModel.OpenUriCommand.Execute(uri);
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
