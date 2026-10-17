using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Board;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views.SettingsPages.Board;

/// <summary>
///     「科目与分类」设置页：管理科目清单与每个科目下的二级分类。
///     <para>
///         科目用「输入框 + 添加」一个一个加，分类名绑草稿属性、失焦 / 回车时提交；
///         删除分类前用 <see cref="FAContentDialog" /> 确认一次（删的是定义，作业会退回科目下）。
///     </para>
/// </summary>
[PageInfo("settings.board.sections", FluentIcons.ArrowSortFilled, "settings.board.group")]
public partial class BoardSectionsSettingsPage : UserControl
{
    public BoardSectionsSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<BoardSectionsSettingsPageViewModel>();

        // 页面是 transient 的，离开可视树时把草稿提交掉并断开对单例服务的订阅。
        Unloaded += (_, _) =>
        {
            ViewModel?.Detach();
        };
    }

    private BoardSectionsSettingsPageViewModel? ViewModel => DataContext as BoardSectionsSettingsPageViewModel;

    /// <summary>新建科目输入框里按回车即添加，不用先去点按钮。</summary>
    private void OnNewSubjectKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        ViewModel?.AddSubject();
    }

    /// <summary>「添加」按钮：加成功会清空输入框、列表跟着重建。</summary>
    private void OnAddSubjectClick(object? sender, RoutedEventArgs e) => ViewModel?.AddSubject();

    /// <summary>
    ///     删除科目：先确认（会连带删掉它下面的分类定义）。已经布置的作业不会被删。
    /// </summary>
    private async void OnDeleteSubjectClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: BoardSectionsSubjectItem item }
            || ViewModel is not { } viewModel
            || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var dialog = new FAContentDialog
        {
            Title = CR.Settings_Board_Subjects_DeleteConfirmTitle,
            Content = CR.Settings_Board_Subjects_DeleteConfirmMessage,
            PrimaryButtonText = CR.Settings_Board_Subjects_Delete,
            CloseButtonText = CR.Common_Cancel
        };

        if (await dialog.ShowAsync(topLevel) == FAContentDialogResult.Primary)
            viewModel.RemoveSubject(item);
    }

    /// <summary>某一行分类名失焦：重命名提交，撞名时卡片顶部给提示。</summary>
    private void OnCategoryNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BoardSectionsCategoryItem item })
            ViewModel?.CommitCategoryRename(item);
    }

    /// <summary>分类名输入框里按回车即提交重命名。</summary>
    private void OnCategoryNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not Control { DataContext: BoardSectionsCategoryItem item })
            return;

        e.Handled = true;
        ViewModel?.CommitCategoryRename(item);
    }

    /// <summary>新建分类输入框里按回车即建。</summary>
    private void OnNewCategoryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not Control { DataContext: BoardSectionsSubjectItem item })
            return;

        e.Handled = true;
        ViewModel?.AddCategory(item);
    }

    /// <summary>「新建分类」按钮：新建成功会清空输入框、重建列表。</summary>
    private void OnAddCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BoardSectionsSubjectItem item })
            ViewModel?.AddCategory(item);
    }

    /// <summary>
    ///     删除分类：先确认。删的是分类定义，里面的作业由服务退回科目的「未分类」，
    ///     所以文案要说清「作业不会跟着没」。
    /// </summary>
    private async void OnDeleteCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: BoardSectionsCategoryItem item }
            || ViewModel is not { } viewModel
            || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var dialog = new FAContentDialog
        {
            Title = CR.Settings_Board_Sections_DeleteConfirmTitle,
            Content = CR.Settings_Board_Sections_DeleteConfirmMessage,
            PrimaryButtonText = CR.Settings_Board_Sections_DeleteCategory,
            CloseButtonText = CR.Common_Cancel
        };

        if (await dialog.ShowAsync(topLevel) == FAContentDialogResult.Primary)
            viewModel.RemoveCategory(item);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
