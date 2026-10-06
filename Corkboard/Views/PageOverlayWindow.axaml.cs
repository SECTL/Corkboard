using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Corkboard.Views;

/// <summary>
///     弹层的独立顶层窗口。
///     <para>
///         为什么需要它：主窗口「置底到桌面」时被 <c>SetParent</c> 嵌进 <c>Progman</c>，
///         成了桌面宿主的子窗口。Windows 只把键盘焦点给前台线程的激活窗口，而桌面宿主不会把激活
///         状态正常传给嵌进来的子窗口 —— 实测该窗口可以成为前台（<c>hwndActive</c> 有值），
///         但 <c>hwndFocus</c> 恒为 <c>0</c>，于是 <c>WM_CHAR</c> 没有投递目标，
///         <b>窗口里一个字都打不进去</b>。这是该形态的固有限制，改代码绕不过去。
///     </para>
///     <para>
///         所以置底形态下，需要键盘输入的表单改由本窗口承载：它是普通顶层窗口，
///         焦点链完整，打字正常；主窗口继续贴在桌面上。非置底形态仍走壳内自绘弹层
///         （见 <c>PageOverlayService</c>），保持「不开系统窗口」的默认做法。
///     </para>
///     <para>
///         外观上仍按自绘弹层的观感来：无标题栏、无边框、可缩放关掉、不进任务栏，
///         内容自己带 <c>Border.sheet</c> 卡片。所以它读起来仍是「弹层」而不是「另一个窗口」。
///     </para>
/// </summary>
public partial class PageOverlayWindow : Window
{
    public PageOverlayWindow()
    {
        InitializeComponent();
    }

    /// <summary>要在窗口里显示的内容，通常是某个表单控件。</summary>
    public object? OverlayContent
    {
        get => ContentHost.Content;
        set => ContentHost.Content = value;
    }

    private ContentControl ContentHost => this.FindControl<ContentControl>("OverlayHost")!;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
