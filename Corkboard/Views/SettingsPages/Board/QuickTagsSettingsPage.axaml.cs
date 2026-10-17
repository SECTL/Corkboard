using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Board;

namespace Corkboard.Views.SettingsPages.Board;

/// <summary>「快捷标签」设置页：布置作业时点一下就能插进内容的标签。</summary>
[PageInfo("settings.board.tags", FluentIcons.NoteFilled, "settings.board.group")]
public partial class QuickTagsSettingsPage : UserControl
{
    private readonly QuickTagsSettingsPageViewModel _viewModel;

    public QuickTagsSettingsPage()
    {
        InitializeComponent();
        _viewModel = IAppHost.GetService<QuickTagsSettingsPageViewModel>();
        DataContext = _viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>回车等于点「新增」，连着录标签更顺手。</summary>
    private void OnTagDraftKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        e.Handled = true;
        if (_viewModel.AddTagCommand.CanExecute(null))
            _viewModel.AddTagCommand.Execute(null);
    }

    /// <summary>
    ///     删除某一行。集合元素是纯字符串，按行对象回传，同色板那套做法
    ///     （<c>BoardPage.OnDeletePaletteColorClick</c>）。
    /// </summary>
    private void OnDeleteTagClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is QuickTagItem item)
            _viewModel.RemoveTag(item);
    }
}
