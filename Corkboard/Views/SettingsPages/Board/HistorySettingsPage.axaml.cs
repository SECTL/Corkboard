using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Board;

namespace Corkboard.Views.SettingsPages.Board;

/// <summary>「历史与回放」设置页：历史的保存粒度、保留天数、容量上限，以及当前占用与「立即清理」。</summary>
[PageInfo("settings.board.history", FluentIcons.HistoryFilled, "settings.board.group")]
public partial class HistorySettingsPage : UserControl
{
    public HistorySettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<HistorySettingsPageViewModel>();

        // 占用统计在页面出现时现读一次，别拿上一次留下的数字糊弄人。
        Loaded += (_, _) => ViewModel?.RefreshStatistics();

        // 三项都是草稿式的：用户可能没让输入框失焦就关了页面，离开时补交一次。
        Unloaded += (_, _) => ViewModel?.CommitAll();
    }

    private HistorySettingsPageViewModel? ViewModel => DataContext as HistorySettingsPageViewModel;

    /// <summary>数值框失焦就把草稿提交掉（理由同其它设置页的输入框）。</summary>
    private void OnEditorLostFocus(object? sender, RoutedEventArgs e) => ViewModel?.CommitAll();

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
