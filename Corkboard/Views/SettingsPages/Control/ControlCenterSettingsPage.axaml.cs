using Avalonia.Controls;
using Avalonia.Interactivity;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.Services.Ui;
using Corkboard.ViewModels.SettingsPages.Control;
using FluentAvalonia.UI.Controls;
using CR = Corkboard.Core.Langs.Common.Resources;

// 命名空间不能叫 ...SettingsPages.Control：那会变成同父命名空间下的一个子命名空间，
// 把所有 SettingsPages.* 文件里的简单名 Control（Avalonia.Controls.Control）遮蔽掉。
namespace Corkboard.Views.SettingsPages.ControlCenter;

/// <summary>
///     「设置 → 集控」页：开关、连接状态、设备信息、接入与班级占用。
///     <para>
///         页面只做壳该做的事：取 ViewModel、在进出可视树时 <c>Attach</c> / <c>Detach</c>、
///         把输入框的失焦转发成一次提交、以及接「取消接入」的确认框。状态展示与所有服务调用都在
///         ViewModel 里。
///     </para>
/// </summary>
[PageInfo("settings.control", FluentIcons.LinkFilled)]
public partial class ControlCenterSettingsPage : UserControl
{
    public ControlCenterSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<ControlCenterSettingsPageViewModel>();

        // ViewModel 是 transient、集控服务是单例：订阅必须在离开可视树时断开（见 Detach）。
        Loaded += (_, _) => ViewModel?.Attach();
        Unloaded += (_, _) =>
        {
            ViewModel?.CommitAll();
            ViewModel?.Detach();
        };
    }

    private ControlCenterSettingsPageViewModel? ViewModel =>
        DataContext as ControlCenterSettingsPageViewModel;

    /// <summary>设备名 / 站点地址 / 地点 ID / 配对码输入框失焦就提交一次草稿。</summary>
    private void OnEditorLostFocus(object? sender, RoutedEventArgs e) => ViewModel?.CommitAll();

    /// <summary>
    ///     「取消接入」：先弹一句确认框，确认后才执行 <see cref="ControlCenterSettingsPageViewModel.ForgetCommand" />。
    ///     对话框显示期间让窗口不透明，免得整窗 alpha 把对话框一起变淡（见 <see cref="ModalOpacityScope" />）。
    /// </summary>
    private async void OnForgetClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var dialog = new FAContentDialog
        {
            Title = CR.Settings_Control_Forget,
            Content = CR.Settings_Control_ForgetConfirm,
            PrimaryButtonText = CR.Settings_Control_Forget,
            CloseButtonText = CR.Common_Cancel
        };

        using var modalOpacity = ModalOpacityScope.Begin(this);
        if (await dialog.ShowAsync(topLevel) != FAContentDialogResult.Primary)
            return;

        if (viewModel.ForgetCommand.CanExecute(null))
            viewModel.ForgetCommand.Execute(null);
    }
}