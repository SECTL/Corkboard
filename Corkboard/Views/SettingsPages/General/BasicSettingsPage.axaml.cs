using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
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
    ///     「链接调用」每行那颗复制按钮：把这一行自己的地址写进剪贴板，再冒一句「已复制」。
    ///     <para>
    ///         剪贴板挂在顶层窗口上（<c>TopLevel.Clipboard</c>），所以这一步留在页面这边，
    ///         ViewModel 只管反馈状态（见 <see cref="UrlProtocolEntry.MarkCopiedAsync" />）。
    ///     </para>
    /// </summary>
    private async void OnCopyUrlClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: UrlProtocolEntry entry })
            return;

        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;

        try
        {
            // Avalonia 12 的 IClipboard 只剩 SetDataAsync / SetValueAsync 这一套，
            // 老的 SetTextAsync 已经删了——写一段文本就是往 DataFormat.Text 里放一个值。
            await clipboard.SetValueAsync(DataFormat.Text, entry.Url);

            // Windows 上剪贴板内容默认随本进程一起没：先 Flush 一下，复制完直接关掉程序也还贴得出来。
            // 别的平台这个方法什么都不做，不用分平台判。
            await clipboard.FlushAsync();
        }
        catch (Exception)
        {
            // 剪贴板可能正被别的程序占着：吞掉就好，别为了复制一个地址把异常抛到界面上。
            return;
        }

        await entry.MarkCopiedAsync(TimeSpan.FromSeconds(1.6));
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
