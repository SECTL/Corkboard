using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages;

namespace Corkboard.Views.SettingsPages.General;

[PageInfo("settings.general.basic", FluentIcons.HomeFilled, groupId: "settings.general")]
public partial class BasicSettingsPage : UserControl
{
    public BasicSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<BasicSettingsPageViewModel>();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
