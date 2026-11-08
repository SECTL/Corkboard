using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Corkboard.Core.Abstraction;
using Corkboard.Services.Desktop;
using Corkboard.Services.Ui;
using Corkboard.ViewModels.Actions;
using Corkboard.ViewModels.SettingsPages.Display;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using LR = Corkboard.Langs.Action.Resources;

namespace Corkboard.Views.Actions;

/// <summary>
///     行动窗口（日程中心）：独立工具窗，普通系统标题栏。
///     <para>
///         编辑器与系统日程详情走窗口自己的 <see cref="PageOverlayService" /> 宿主
///         （<see cref="PageOverlayService.DialogOnlyHostPriority" />）：主窗口 / 设置窗口的宿主
///         优先级更高，不指名的话表单会被画到别的窗口上去。
///     </para>
///     <para>
///         ViewModel 是 transient、规则存储与执行器是单例：订阅必须在关闭时一起收掉，
///         否则每开一次窗口就多挂一份处理器。
///     </para>
/// </summary>
public partial class ActionWindow : FAAppWindow
{
    private readonly ActionWindowViewModel _viewModel;
    private readonly PageOverlayService _overlay;
    private readonly PageOverlayHost _overlayHost;

    public ActionWindow()
    {
        InitializeComponent();

        _viewModel = IAppHost.GetService<ActionWindowViewModel>();
        DataContext = _viewModel;

        // 这扇窗在任务栏里有按钮：标题栏用位图，任务栏仍要走真 ICO（见 AppIcon）。
        Icon = AppIcon.LoadBitmap();
        ((Window)this).Icon = AppIcon.LoadWindowIcon();

        _overlay = IAppHost.GetService<PageOverlayService>();
        _overlayHost = _overlay.CreateHost(this, PageOverlayService.DialogOnlyHostPriority);
        _overlayHost.PropertyChanged += OnOverlayHostChanged;
        _overlay.Closed += OnOverlayClosed;

        _viewModel.EditorRequested += OnEditorRequested;
        _viewModel.DetailRequested += OnDetailRequested;

        Opened += (_, _) => _viewModel.Attach();
        Closed += OnClosed;
        KeyDown += OnWindowKeyDown;
    }

    private void OnOverlayHostChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (null or nameof(PageOverlayHost.IsOpen))) return;

        OverlayScrim.IsVisible = _overlayHost.IsOpen;
        OverlayContentHost.Content = _overlayHost.Content;
    }

    private void OnEditorRequested(object? sender, ActionEditorViewModel editor)
    {
        var view = new ActionEditorView { DataContext = editor };

        editor.Saved += (_, _) => _overlay.Close();
        editor.Cancelled += (_, _) => _overlay.Close();
        editor.DeleteRequested += async (_, _) => await ConfirmDeleteAsync(editor);

        _overlay.Show(view, this);
        EnsureOverlayVisible(view);
    }

    private void OnDetailRequested(object? sender, SystemScheduleDetailViewModel detail)
    {
        detail.CloseRequested += (_, _) => _overlay.Close();

        // 详情卡片的模板挂在窗口资源里（x:Key），这里包一层 ContentControl 再交给弹层宿主。
        // 设置项直接在卡片里改（写配置后立刻落盘），不再跳设置页。
        var content = new ContentControl
        {
            Content = detail,
            ContentTemplate = this.TryFindResource("SystemScheduleDetailTemplate", out var template)
                ? template as Avalonia.Markup.Xaml.Templates.DataTemplate
                : null
        };

        _overlay.Show(content, this);
        EnsureOverlayVisible(content);
    }

    /// <summary>
    ///     兜底同步遮罩：正常路径上 <see cref="PageOverlayService" /> 会把宿主标记为打开、
    ///     再经 <see cref="OnOverlayHostChanged" /> 推过来；这里再显式同步一次，
    ///     避免宿主事件没到时表现为「点了没反应」。
    /// </summary>
    private void EnsureOverlayVisible(object content)
    {
        if (_overlayHost.IsOpen)
        {
            OverlayScrim.IsVisible = true;
            OverlayContentHost.Content = _overlayHost.Content ?? content;
            return;
        }

        OverlayScrim.IsVisible = true;
        OverlayContentHost.Content = content;
    }

    /// <summary>弹层关掉时清掉自己这一层的遮罩（宿主没打开说明内容已经撤了）。</summary>
    private void OnOverlayClosed(object? sender, EventArgs e)
    {
        if (_overlayHost.IsOpen) return;

        OverlayScrim.IsVisible = false;
        OverlayContentHost.Content = null;
    }

    /// <summary>
    ///     时段编辑器里的时刻选择器失焦时提交（<see cref="DisplaySlotItem" /> 的草稿要先 CommitTimes 才写回时段）。
    /// </summary>
    private void OnSlotTimeLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: DisplaySlotItem slot })
            slot.CommitTimes();
    }

    private async Task ConfirmDeleteAsync(ActionEditorViewModel editor)
    {
        var dialog = new FAContentDialog
        {
            Title = LR.Action_DeleteConfirmTitle,
            Content = LR.Action_DeleteConfirmBody,
            PrimaryButtonText = LR.Action_Delete,
            CloseButtonText = LR.Action_Cancel
        };

        if (await dialog.ShowAsync(this) == FAContentDialogResult.Primary)
            editor.Delete();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _overlayHost.IsOpen)
        {
            _overlay.Close();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.N && e.KeyModifiers.HasFlag(KeyModifiers.Control) && !_viewModel.IsReadOnly)
        {
            _viewModel.NewActionCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.EditorRequested -= OnEditorRequested;
        _viewModel.DetailRequested -= OnDetailRequested;
        _viewModel.Detach();

        _overlayHost.PropertyChanged -= OnOverlayHostChanged;
        _overlay.Closed -= OnOverlayClosed;
        _overlay.ReleaseHost(_overlayHost);
    }
}
