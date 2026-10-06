using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views.SettingsPages.General;

[PageInfo("settings.general.basic", FluentIcons.HomeFilled)]
public partial class BasicSettingsPage : UserControl
{
    /// <summary>程序化回滚开关时的闸门，避免回滚本身再触一次系统集成。</summary>
    private bool _isApplyingProgrammaticChange;

    public BasicSettingsPage()
    {
        InitializeComponent();

        var viewModel = IAppHost.GetService<BasicSettingsPageViewModel>();
        DataContext = viewModel;
        viewModel.LanguageChangeRequested += OnLanguageChangeRequested;

        // ViewModel 是 transient、设置窗口是长期存在的，离开可视树时必须摘掉订阅，
        // 并把滑杆草稿落一次盘（拖完马上关页面时最后一次拖动不能丢）。
        Unloaded += (_, _) =>
        {
            viewModel.LanguageChangeRequested -= OnLanguageChangeRequested;
            viewModel.FlushOpacityDraft();
        };
    }

    private BasicSettingsPageViewModel? ViewModel => DataContext as BasicSettingsPageViewModel;

    private static void OnLanguageChangeRequested(object? sender, EventArgs e)
    {
        // 语言要重启才对全部界面生效，标题栏右侧亮出「需要重启」提示（不直接重启）。
        SettingsView.Current?.RequestRestartApp();
    }

    /// <summary>
    ///     开机自启是系统集成而不是纯配置：开关一动就立刻写注册表 / 桌面项 / LaunchAgent，
    ///     写入失败就把开关拨回去并告知原因（不留假成功）。
    /// </summary>
    private async void OnAutostartCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_isApplyingProgrammaticChange || ViewModel is not { } viewModel)
            return;

        var enabled = (sender as ToggleSwitch)?.IsChecked == true;
        if (viewModel.TryApplyAutostart(enabled, out var error))
            return;

        _isApplyingProgrammaticChange = true;
        (sender as ToggleSwitch)!.IsChecked = viewModel.Basic.Autostart;
        _isApplyingProgrammaticChange = false;

        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        await new FAContentDialog
        {
            Title = CR.Settings_Basic_Autostart,
            Content = string.Format(CR.Settings_DesktopIntegrationFailed, CR.Settings_Basic_Autostart, error),
            CloseButtonText = CR.Common_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(topLevel);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
