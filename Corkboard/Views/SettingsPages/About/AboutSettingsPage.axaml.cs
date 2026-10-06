using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages;

namespace Corkboard.Views.SettingsPages.About;

[PageInfo("settings.about", FluentIcons.InfoFilled, groupId: "settings.about")]
public partial class AboutSettingsPage : UserControl
{
    public AboutSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<AboutSettingsPageViewModel>();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
