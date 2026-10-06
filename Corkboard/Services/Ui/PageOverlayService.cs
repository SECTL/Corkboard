using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Views;

namespace Corkboard.Services.Ui;

/// <summary>
///     页面级弹层宿主：页面把要显示的内容塞进来，由宿主把它画出来。
///     <para>
///         有两条路：
///         <list type="bullet">
///             <item>
///                 <b>壳内自绘</b>（默认）：<c>MainView</c> 在窗口内容最上层画遮罩与内容。
///                 放在壳这一层而不是页面里，遮罩才能盖住自绘标题栏——模态要真的是"全窗"，
///                 只盖内容区会显得标题栏还亮着、模态不成立。
///             </item>
///             <item>
///                 <b>独立顶层窗口</b>（<see cref="UseSeparateWindow" /> 为真时）：
///                 主窗口「置底到桌面」后是被嵌进 <c>Progman</c> 的子窗口，
///                 Windows 不会给它键盘焦点，表单在壳里<b>一个字都打不进去</b>
///                 （详见 <see cref="PageOverlayWindow" /> 的说明）。
///                 所以这种形态下改用独立窗口承载表单，外观仍按弹层做。
///             </item>
///         </list>
///     </para>
///     <para>
///         自绘而不是开系统窗口的理由（没有标题栏、不依赖桌面端多窗口能力、手机端同样能用）
///         在独立窗口这条路上靠 <c>SystemDecorations=None</c> 等设置维持同样的观感。
///     </para>
/// </summary>
public partial class PageOverlayService : ObservableObject
{
    private PageOverlayWindow? _window;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private object? _content;

    /// <summary>
    ///     是否需要把弹层摊到独立顶层窗口里。由主窗口在应用/解除「置底到桌面」时写入，
    ///     因为只有它知道该能力是不是真的生效了（平台可能报不支持）。
    /// </summary>
    public bool UseSeparateWindow { get; set; }

    /// <summary>显示一层弹层。内容通常是某个表单控件。</summary>
    public void Show(object content)
    {
        if (UseSeparateWindow)
        {
            ShowInSeparateWindow(content);
            return;
        }

        Content = content;
        IsOpen = true;
    }

    public void Close()
    {
        CloseSeparateWindow();

        IsOpen = false;
        Content = null;
    }

    /// <summary>
    ///     独立窗口这条路：置底形态下主窗口拿不到键盘焦点，必须由这个普通顶层窗口来承载输入。
    ///     窗口置顶显示，保证贴在桌面上的挂件不会把表单挡在后面。
    /// </summary>
    private void ShowInSeparateWindow(object content)
    {
        // 壳内那条路要收干净，否则两条路会同时画一份内容。
        IsOpen = false;
        Content = null;

        if (_window is null)
        {
            _window = new PageOverlayWindow { Topmost = true };
            _window.Closed += OnSeparateWindowClosed;
        }

        _window.OverlayContent = content;

        if (!_window.IsVisible)
            _window.Show();

        // 置底的主窗口拿不到前台，表单要自己争取一下，否则用户点进输入框才生效。
        _window.Activate();
    }

    private void CloseSeparateWindow()
    {
        if (_window is not { } window)
            return;

        _window = null;
        window.Closed -= OnSeparateWindowClosed;
        window.Close();
    }

    private void OnSeparateWindowClosed(object? sender, EventArgs e)
    {
        if (_window is not { } window)
            return;

        _window = null;
        window.Closed -= OnSeparateWindowClosed;
        window.OverlayContent = null;
    }
}
