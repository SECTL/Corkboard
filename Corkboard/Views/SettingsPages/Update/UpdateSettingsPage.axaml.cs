using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Enums;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages;

namespace Corkboard.Views.SettingsPages.Update;

// 第三个参数是 GroupId（本页不参与分组，所以传 null），第四个是导航位置：
// 更新页和「关于」一样沉在设置导航栏底部（页脚区），日常设置不会被它挤到下面。
[PageInfo("settings.update", FluentIcons.ArrowSyncFilled, null, PageLocation.Bottom)]
public partial class UpdateSettingsPage : UserControl
{
    private readonly UpdateSettingsPageViewModel _viewModel;

    public UpdateSettingsPage()
    {
        InitializeComponent();

        _viewModel = IAppHost.GetService<UpdateSettingsPageViewModel>();
        DataContext = _viewModel;

        // 页面是键控 DI 造出来的 transient，离开可视树必须断开单例上的事件（见 VM 的 Detach）。
        Unloaded += (_, _) => _viewModel.Detach();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    ///     强制检查：不管当前版本是不是已经最新，都去当前通道要一份清单（用于回退到更低版本）。
    ///     整行点一下就走，和「关于」页那几个链接项一个写法。
    /// </summary>
    private void ForceCheck_OnClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.ForceCheckCommand.Execute(null);
    }
}
