using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Exam;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views.SettingsPages.Exam;

/// <summary>
///     「考试安排」设置页：全屏看板入口 + 新增 / 编辑表单 + 考试清单。
///     <para>
///         表单字段全部绑草稿，点「保存」才写进考试服务；删除前用 <see cref="FAContentDialog" />
///         确认一次（删的是一条考试安排，没有撤销）。
///     </para>
/// </summary>
[PageInfo("settings.exam.schedule", FluentIcons.CalendarFilled, "settings.exam.group")]
public partial class ExamScheduleSettingsPage : UserControl
{
    public ExamScheduleSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<ExamScheduleSettingsPageViewModel>();

        // ViewModel 是 transient、考试服务是单例：订阅必须在离开可视树时断开，
        // 否则每开一次这个页面就往服务上多挂一个处理器。
        Loaded += (_, _) => ViewModel?.Attach();
        Unloaded += (_, _) => ViewModel?.Detach();
    }

    private ExamScheduleSettingsPageViewModel? ViewModel => DataContext as ExamScheduleSettingsPageViewModel;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>「全屏看板」：开一块整屏的时钟 + 考试表。</summary>
    private void OnOpenBoardClick(object? sender, RoutedEventArgs e) => App.ShowExamBoardWindow();

    /// <summary>某一行点「编辑」：把那场考试填进表单。</summary>
    private void OnEditExamClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ExamScheduleItem item)
            ViewModel?.BeginEdit(item);
    }

    /// <summary>某一行点「删除」：先确认，再删。</summary>
    private async void OnDeleteExamClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ExamScheduleItem item
            || ViewModel is not { } viewModel
            || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var dialog = new FAContentDialog
        {
            Title = CR.Settings_Exam_Schedule_DeleteTitle,
            Content = string.Format(CR.Settings_Exam_Schedule_DeleteBody, item.Subject),
            PrimaryButtonText = CR.Settings_Exam_Schedule_Delete,
            CloseButtonText = CR.Common_Cancel
        };

        if (await dialog.ShowAsync(topLevel) == FAContentDialogResult.Primary)
            viewModel.Remove(item);
    }

    /// <summary>「取消」：表单退回新增态。</summary>
    private void OnCancelEditClick(object? sender, RoutedEventArgs e) => ViewModel?.CancelEdit();

    /// <summary>科目 / 备注输入框里按回车即保存，连着录几场更顺手。</summary>
    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        e.Handled = true;
        if (ViewModel?.SubmitCommand.CanExecute(null) == true)
            ViewModel.SubmitCommand.Execute(null);
    }
}
