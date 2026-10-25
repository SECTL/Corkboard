using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.Services.Ui;
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

        // ViewModel 是 transient、设置窗口是长期存在的，离开可视树时必须摘掉订阅。
        // （不透明度滑杆已经搬到「作业板 → 窗口与收纳」，这里不再需要补交那份草稿。）
        Unloaded += (_, _) =>
        {
            viewModel.LanguageChangeRequested -= OnLanguageChangeRequested;
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

        // 对话框显示期间让窗口不透明：整窗 alpha 会把对话框一起变透明（见 ModalOpacityScope）。
        // 这个窗口不登记不透明度能力，所以这里实际上是空操作，写上是为了不漏掉任何一处对话框。
        using var modalOpacity = ModalOpacityScope.Begin(this);
        await new FAContentDialog
        {
            Title = CR.Settings_Basic_Autostart,
            Content = string.Format(CR.Settings_DesktopIntegrationFailed, CR.Settings_Basic_Autostart, error),
            CloseButtonText = CR.Common_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(topLevel);
    }

    /// <summary>
    ///     重新走一遍首次设置。引导本身是独立窗口，这里只负责把它叫出来——
    ///     已经在开会自己激活，不会再叠第二个。
    ///     <para>
    ///     这里 <c>exitOnCancel: false</c>：用户是主动重跑的，中途关掉引导只关那个窗口，
    ///     不能把整个程序（连同他正在调设置的这个窗口）一起退掉。
    ///     </para>
    /// </summary>
    private void OnReopenOobeClick(object? sender, RoutedEventArgs e) => App.ShowOobeWindow(exitOnCancel: false);

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
