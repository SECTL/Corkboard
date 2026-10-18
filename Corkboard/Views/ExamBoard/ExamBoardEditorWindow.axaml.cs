using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Corkboard.Core.Abstraction;
using Corkboard.ViewModels.ExamBoard;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views.ExamBoard;

/// <summary>
///     「编辑看板」窗口：一块看板文件里的标题、副标题、颜色与全部考试都在这里改。
///     <para>
///         关窗不弹确认：标题、副标题与每一场考试的改动**当场就写进看板文件**了（表单点「保存」时写），
///         关掉不会丢东西，没必要拦一道。
///     </para>
///     <para>
///         ViewModel 是 transient、考试服务是单例：订阅与计时器必须在窗口关闭时一起收掉，
///         否则每开一次窗口就多挂一份处理器、多跑一个 15 秒的计时器。
///     </para>
/// </summary>
public partial class ExamBoardEditorWindow : Window
{
    public ExamBoardEditorWindow()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<ExamBoardEditorViewModel>();

        Opened += (_, _) => ViewModel?.Attach();
        Closed += (_, _) =>
        {
            // 关窗前把还压在草稿里的标题 / 副标题 / 颜色落一次盘，别让最后敲的那几个字丢掉。
            ViewModel?.CommitAll();
            ViewModel?.Detach();
        };
    }

    private ExamBoardEditorViewModel? ViewModel => DataContext as ExamBoardEditorViewModel;

    /// <summary>换了一块看板（窗口已经开着时由 <see cref="App.ShowExamBoardEditorWindow" /> 调）。</summary>
    public void ReloadBoard() => ViewModel?.ReloadBoard();

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

    /// <summary>标题 / 副标题输入框失焦就提交：看板文件整份重写，没必要每敲一个字符写一遍。</summary>
    private void OnEditorLostFocus(object? sender, RoutedEventArgs e) => ViewModel?.CommitAll();

    /// <summary>输入框里按回车立刻提交，不用先点别处。</summary>
    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        e.Handled = true;
        ViewModel?.CommitAll();
    }

    /// <summary>科目 / 备注输入框里按回车即保存，连着录几场更顺手。</summary>
    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        e.Handled = true;
        if (ViewModel?.SubmitCommand.CanExecute(null) == true)
            ViewModel.SubmitCommand.Execute(null);
    }

    /// <summary>「全屏看板」：先把手里的改动落盘，再开那块整屏的板子看效果。</summary>
    private void OnPreviewClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.CommitAll();
        App.ShowExamBoardWindow();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
