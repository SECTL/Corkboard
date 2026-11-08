using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Exam;
using Corkboard.Services.Desktop;
using Corkboard.Services.Exam;
using Corkboard.ViewModels.ExamBoard;
using Corkboard.ViewModels.SettingsPages.Exam;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views.ExamBoard;

/// <summary>
///     「考试看板」面板窗口：一列看板卡片，每张卡上直接能放映——这是日常操作的那扇窗，
///     与「设置 → 考试安排」（配置语义）共用同一份数据和同一个行模型。
///     <para>
///         入口有三条：托盘菜单、设置页那几颗按钮之外的新窗口、以及应用启动参数
///         <c>--open-exam-board-panel</c>（<see cref="ExamBoardLaunchArguments.OpenPanelSwitch" />）。
///     </para>
///     <para>
///         ViewModel 是 transient、考试服务是单例：订阅与 15 秒计时器必须在窗口关闭时一起收掉，
///         否则每开一次窗口就多挂一份处理器。
///     </para>
/// </summary>
public partial class ExamBoardPanelWindow : FAAppWindow
{
    public ExamBoardPanelWindow()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<ExamBoardPanelViewModel>();

        // 这一扇窗在任务栏里有按钮，图标必须是 Corkboard 自己的（见 AppIcon）。
        ((Window)this).Icon = AppIcon.LoadWindowIcon();

        Opened += (_, _) => ViewModel?.Attach();
        Closed += (_, _) => ViewModel?.Detach();
    }

    private ExamBoardPanelViewModel? ViewModel => DataContext as ExamBoardPanelViewModel;

    /// <summary>「新建看板」：建一块空看板，并直接把编辑窗口开到它上面（和设置页那颗同一套语义）。</summary>
    private void OnCreateBoardClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.CreateBoard() is { } board)
            App.ShowExamBoardEditorWindow(board.Id);
    }

    /// <summary>「快速布置」：开向导，完成时**新建**一块看板。</summary>
    private void OnQuickPlanClick(object? sender, RoutedEventArgs e) =>
        App.ShowExamPlanWizardWindow(ExamPlanWizardMode.CreateBoard);

    /// <summary>「导入考试」：把 <c>.cbexam</c> 里的考试追加进当前这块看板（不覆盖原有排期）。</summary>
    private async void OnImportExamsClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        try
        {
            var exams = await IAppHost.GetService<ExamFileService>().ImportExamsAsync(topLevel);
            if (exams is null)
                return;

            if (exams.Count == 0)
            {
                await ShowNoticeAsync(topLevel, CR.Exam_File_ImportEmpty);
                return;
            }

            var added = viewModel.ImportExams(exams);
            await ShowNoticeAsync(topLevel, string.Format(CR.Exam_File_ImportDone, added));
        }
        catch (InvalidOperationException exception)
        {
            await ShowNoticeAsync(topLevel, exception.Message);
        }
    }

    /// <summary>
    ///     某一行点「放映」：先把这块设成当前，再铺全屏看板。
    ///     <para>
    ///         空看板不直接放——全屏上去就是一片空白，用户会以为坏了。改成先问一句「去布置」，
    ///         答应的就把这块设成当前、开向导往里追加。
    ///     </para>
    /// </summary>
    private async void OnPresentClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ExamBoardItem item
            || ViewModel is not { } viewModel
            || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        if (item.Board.Exams is { Count: > 0 })
        {
            viewModel.Present(item);
            return;
        }

        var dialog = new FAContentDialog
        {
            Title = CR.Exam_Panel_NoExamsTitle,
            Content = CR.Exam_Panel_NoExamsMessage,
            PrimaryButtonText = CR.Exam_Panel_NoExamsFill,
            CloseButtonText = CR.Common_Cancel
        };

        if (await dialog.ShowAsync(topLevel) != FAContentDialogResult.Primary)
            return;

        viewModel.SelectBoard(item);
        App.ShowExamPlanWizardWindow(ExamPlanWizardMode.AppendToBoard);
    }

    /// <summary>某一行点「编辑」：把这块设成当前，再开编辑窗口。</summary>
    private void OnEditBoardClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ExamBoardItem item)
            return;

        ViewModel?.SelectBoard(item);
        App.ShowExamBoardEditorWindow(item.Id);
    }

    /// <summary>某一行点「设为当前」：全屏看板换成这一块。</summary>
    private void OnActivateBoardClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ExamBoardItem item)
            ViewModel?.SelectBoard(item);
    }

    /// <summary>某一行点「导出」：把**那一行**看板的全部考试存成一份 <c>.cbexam</c>。</summary>
    private async void OnExportBoardClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ExamBoardItem item
            || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var board = item.Board;
        if (board.Exams is null || board.Exams.Count == 0)
        {
            await ShowNoticeAsync(topLevel, CR.Exam_File_ExportEmpty, CR.Exam_Panel_Export);
            return;
        }

        try
        {
            if (await IAppHost.GetService<ExamFileService>().ExportBoardAsync(board, topLevel))
                await ShowNoticeAsync(topLevel, string.Format(CR.Exam_File_ExportDone, board.Exams.Count),
                    CR.Exam_Panel_Export);
        }
        catch (InvalidOperationException exception)
        {
            await ShowNoticeAsync(topLevel, exception.Message, CR.Exam_Panel_Export);
        }
    }

    /// <summary>某一行点「删除」：连里面的考试一起删，先确认一次（只剩一块时按钮是灰的）。</summary>
    private async void OnDeleteBoardClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ExamBoardItem item
            || ViewModel is not { } viewModel
            || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var dialog = new FAContentDialog
        {
            Title = CR.Exam_Panel_DeleteTitle,
            Content = string.Format(CR.Exam_Panel_DeleteMessage, item.Title),
            PrimaryButtonText = CR.Exam_Panel_Delete,
            CloseButtonText = CR.Common_Cancel
        };

        if (await dialog.ShowAsync(topLevel) == FAContentDialogResult.Primary)
            viewModel.DeleteBoard(item);
    }

    /// <summary>底栏那颗「关闭放映」：收掉全屏看板，这扇窗留着（用户可能接着换一块放）。</summary>
    private void OnStopPresentClick(object? sender, RoutedEventArgs e) => App.CloseExamBoardWindow();

    /// <summary>一句话提示（只有「知道了」一个按钮）。</summary>
    private async Task ShowNoticeAsync(TopLevel topLevel, string message, string? title = null)
    {
        var dialog = new FAContentDialog
        {
            Title = title ?? CR.Exam_Panel_Import,
            Content = message,
            CloseButtonText = CR.Common_Close
        };

        await dialog.ShowAsync(topLevel);
    }
}
