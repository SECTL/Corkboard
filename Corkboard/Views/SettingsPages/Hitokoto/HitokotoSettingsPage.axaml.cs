using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages;

namespace Corkboard.Views.SettingsPages.Hitokoto;

[PageInfo("settings.hitokoto", FluentIcons.NoteFilled)]
public partial class HitokotoSettingsPage : UserControl
{
    public HitokotoSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<HitokotoSettingsPageViewModel>();

        // 没按保存就离开页面时，也要把尚未提交的输入草稿写回配置。
        Unloaded += (_, _) => ViewModel?.SaveDrafts();
    }

    private HitokotoSettingsPageViewModel? ViewModel => DataContext as HitokotoSettingsPageViewModel;

    private void OnRefreshMinutesLostFocus(object? sender, RoutedEventArgs e)
    {
        ViewModel?.CommitRefreshMinutes();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
