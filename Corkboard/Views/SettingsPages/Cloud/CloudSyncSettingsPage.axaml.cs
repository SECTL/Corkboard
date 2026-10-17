using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.Services.Ui;
using Corkboard.ViewModels.SettingsPages;
using FluentAvalonia.UI.Controls;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views.SettingsPages.Cloud;

/// <summary>
///     云同步设置页：把作业板与设置备份到思拓创联云存储，需要时再从云端恢复。
/// </summary>
/// <remarks>
///     <c>isHide: true</c>：暂不对外开放入口——页面不进设置侧边栏，但注册仍在
///     （键控 DI 里按 Id 仍可取到），以后要开放时把这个参数去掉即可。
/// </remarks>
[PageInfo("settings.cloud", FluentIcons.CloudFilled, isHide: true)]
public partial class CloudSyncSettingsPage : UserControl
{
    private readonly CloudSyncSettingsPageViewModel _viewModel;

    public CloudSyncSettingsPage()
    {
        InitializeComponent();

        _viewModel = IAppHost.GetService<CloudSyncSettingsPageViewModel>();
        DataContext = _viewModel;

        // 页面是键控 DI 造出来的 transient，离开可视树必须断开单例上的事件（见 VM 的 Detach）。
        Unloaded += (_, _) => _viewModel.Detach();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    ///     从云端恢复：这一步会覆盖本机数据、丢掉还没传上去的改动，先弹一句确认。
    /// </summary>
    private async void Restore_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;

        var dialog = new FAContentDialog
        {
            Title = CR.Settings_Cloud_RestoreConfirmTitle,
            Content = CR.Settings_Cloud_RestoreConfirmMessage,
            PrimaryButtonText = CR.Settings_Cloud_Restore,
            CloseButtonText = CR.Common_Cancel,
            DefaultButton = FAContentDialogButton.Close
        };

        // 主窗口可能设了不透明度，对话框期间要让位给 1，否则框自己也是半透明的。
        using var modalOpacity = ModalOpacityScope.Begin(this);
        if (await dialog.ShowAsync(topLevel) != FAContentDialogResult.Primary) return;

        await _viewModel.RestoreCommand.ExecuteAsync(null);

        // 设置那一份是整棵配置换掉再重新加载的，界面上的语言、字体这类改动要重启才全部生效。
        if (_viewModel.SettingsRestored)
            SettingsView.Current?.RequestRestartApp();
    }

    /// <summary>
    ///     清空云端：云端的备份会被全删、删了拿不回来，先弹一句确认。
    /// </summary>
    private async void ClearCloud_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;

        var dialog = new FAContentDialog
        {
            Title = CR.Settings_Cloud_ClearCloudConfirmTitle,
            Content = CR.Settings_Cloud_ClearCloudConfirmMessage,
            PrimaryButtonText = CR.Settings_Cloud_ClearCloud,
            CloseButtonText = CR.Common_Cancel,
            DefaultButton = FAContentDialogButton.Close
        };

        // 主窗口可能设了不透明度，对话框期间要让位给 1，否则框自己也是半透明的。
        using var modalOpacity = ModalOpacityScope.Begin(this);
        if (await dialog.ShowAsync(topLevel) != FAContentDialogResult.Primary) return;

        await _viewModel.ClearCloudCommand.ExecuteAsync(null);
    }
}
