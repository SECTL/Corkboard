using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using Corkboard.Services.Desktop;
using Corkboard.Services.Ui;
using Corkboard.ViewModels;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views;

/// <summary>
///     首次运行引导（OOBE）窗口。六个步骤的内容与状态都在 <see cref="FirstRunOobeViewModel" /> 里，
///     这里只负责三件窗口才有的事：点「下一步」推进 / 收尾并通知 App、语言改了要重启、
///     以及点 × 时先问一句「真的先不设了？」。
/// </summary>
/// <remarks>
///     <para>
///         收尾成功后抛 <see cref="SetupCompleted" />，由 <c>App</c> 去决定接下来建不建主窗口
///         ——窗口自己不该知道主窗口的存在。
///     </para>
///     <para>
///         ⚠️ <b>点 × 不能直接关</b>：引导没走完就退出，下次启动还是同一套引导。中途退出不写任何
///         「已完成」标记，所以「退出」必然等于「下次重来」，这一点要在确认框里讲明白。
///     </para>
/// </remarks>
public partial class FirstRunOobeWindow : FAAppWindow
{
    private readonly FirstRunOobeViewModel? _viewModel;

    /// <summary>已经收尾（或用户确认了退出）——此时再关窗口就不该拦了。</summary>
    private bool _canClose;

    /// <summary>
    ///     点 × 之后要不要把整个程序一起退掉。
    ///     <para>
    ///         首次启动时是<b>要</b>：引导没走完的半套配置留在后台没有意义，托盘图标也是凭空多出来的，
    ///         用户下次启动本来也会看到同一个窗口。但从设置里「重新运行首次设置」打开时<b>不能</b>退——
    ///         那是用户主动再走一遍，退出程序会把他正在做的事一起关掉。
    ///     </para>
    /// </summary>
    private readonly bool _exitOnCancel;

    /// <summary>XAML 设计器用的空构造。</summary>
    public FirstRunOobeWindow()
    {
        InitializeComponent();
    }

    public FirstRunOobeWindow(FirstRunOobeViewModel viewModel, bool exitOnCancel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _exitOnCancel = exitOnCancel;
        DataContext = viewModel;

        ((Window)this).Icon = AppIcon.LoadWindowIcon();

        // 语言改了立刻重启：引导还没走完（GuideCompleted 仍为假），重启后同一个窗口会以新语言
        // 从第一步重开。此时还没配过任何东西，重来一遍没有代价，比留着一半旧语言的界面省事。
        viewModel.LanguageChangeRequested += OnLanguageChangeRequested;

        Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>引导走完了。订阅方负责决定接下来显示什么（<c>App</c> 里接主窗口）。</summary>
    public event EventHandler? SetupCompleted;

    private void OnLanguageChangeRequested(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.LanguageChangeRequested -= OnLanguageChangeRequested;

        App.Current.Restart();
    }

    /// <summary>
    ///     「下一步」/「开始使用」：前几步交给命令推进，最后一步才真正收尾。
    ///     <para>
    ///         按钮上挂 <c>Click</c> 而不是 <c>Command</c>，因为最后一步要做「写盘 + 关窗口 + 通知 App」
    ///         这一串，光靠命令没法让窗口收尾（与考试快速布置向导同一套写法）。
    ///     </para>
    /// </summary>
    private void OnNextClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
            return;

        if (!_viewModel.IsLastStep)
        {
            _viewModel.NextCommand.Execute(null);
            return;
        }

        if (!_viewModel.TryFinish())
            return;

        _canClose = true;
        Close();
        SetupCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     点 × ：先问一句。确认退出就整个进程退出（托盘图标也不留）——引导没走完的半套配置
    ///     留在后台没有意义，用户下次启动本来也会看到同一个窗口。
    ///     从设置里再打开时不退程序，只关掉这个窗口（见 <see cref="_exitOnCancel" />）。
    /// </summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_canClose)
            return;

        // 系统关机 / 应用整体退出（比如托盘菜单里的「退出程序」）不再拦，否则关不掉。
        if (e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
            return;

        e.Cancel = true;

        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var dialog = new FAContentDialog
        {
            Title = CR.Oobe_ExitTitle,
            Content = _exitOnCancel ? CR.Oobe_ExitDescription : CR.Oobe_ExitWindowDescription,
            PrimaryButtonText = _exitOnCancel ? CR.Oobe_Exit : CR.Oobe_ExitWindow,
            CloseButtonText = CR.Oobe_ContinueSetup,
            DefaultButton = FAContentDialogButton.Close
        };

        using var scope = ModalOpacityScope.Begin(this);

        if (await dialog.ShowAsync(topLevel) != FAContentDialogResult.Primary)
            return;

        _canClose = true;

        if (_exitOnCancel)
        {
            _ = App.Current.StopAsync();
            return;
        }

        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.LanguageChangeRequested -= OnLanguageChangeRequested;

        Closing -= OnClosing;
        Closed -= OnClosed;
    }
}
