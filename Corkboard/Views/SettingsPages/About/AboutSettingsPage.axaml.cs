using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages;
using FluentAvalonia.UI.Controls;

namespace Corkboard.Views.SettingsPages.About;

[PageInfo("settings.about", FluentIcons.InfoFilled)]
public partial class AboutSettingsPage : UserControl
{
    public AboutSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<AboutSettingsPageViewModel>();
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
