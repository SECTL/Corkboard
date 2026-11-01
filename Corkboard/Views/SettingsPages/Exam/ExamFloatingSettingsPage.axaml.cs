using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Exam;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views.SettingsPages.Exam;

/// <summary>
///     「最小化与浮窗」设置页：考试中把左下角那颗退出按钮换成最小化（单击还是长按可选），
///     以及最小化之后屏幕上的那颗浮窗按钮——文字、大小、停在哪儿。
/// </summary>
[PageInfo("settings.exam.floating", FluentIcons.ArrowMinimizeFilled, "settings.exam.group")]
public partial class ExamFloatingSettingsPage : UserControl
{
    public ExamFloatingSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<ExamFloatingSettingsPageViewModel>();

        // ViewModel 是 transient、配置是单例：订阅必须在离开可视树时断开。
        Loaded += (_, _) => ViewModel?.Attach();
        Unloaded += (_, _) =>
        {
            ViewModel?.CommitAll();
            ViewModel?.Detach();
        };
    }

    private ExamFloatingSettingsPageViewModel? ViewModel =>
        DataContext as ExamFloatingSettingsPageViewModel;

    /// <summary>文字 / 数值输入框失焦就提交：配置没有防抖，双向绑等于每敲一个字符写一遍盘。</summary>
    private void OnEditorLostFocus(object? sender, RoutedEventArgs e) => ViewModel?.CommitAll();

    private void ResetButton_OnClick(object? sender, RoutedEventArgs e) => ViewModel?.ResetToDefault();

    /// <summary>点下拉样按钮：展开 / 收起九宫格浮层。</summary>
    private void OnAnchorPickerClick(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<Popup>("AnchorPopup") is { } popup)
            popup.IsOpen = !popup.IsOpen;
    }

    /// <summary>九宫格圆点：位置已经写进配置，点完顺手把浮层收起来。</summary>
    private void OnAnchorDotClick(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<Popup>("AnchorPopup") is { } popup)
            popup.IsOpen = false;
    }

    /// <summary>挑一张本地图片当浮窗图标。取消、选不了、图片坏了都只是什么都不发生，不弹框。</summary>
    private async void OnPickIconClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        try
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
                return;

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = CR.Settings_Exam_Floating_IconPick,
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType(CR.Settings_Exam_Floating_IconPick)
                    {
                        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.ico", "*.gif", "*.webp"]
                    }
                ]
            });

            if (files.Count == 0)
                return;

            if (files[0].TryGetLocalPath() is { Length: > 0 } path)
                viewModel.SetFloatingIconPath(path);
        }
        catch (Exception)
        {
            // 文件选择器在某些平台/沙箱里会抛异常；换图标失败不该把整个设置页带走。
        }
    }

    /// <summary>换回软件自带的那颗图标（自定义那张只是从配置里摘掉，文件本身不动）。</summary>
    private void OnClearIconClick(object? sender, RoutedEventArgs e) => ViewModel?.ClearFloatingIcon();
}
