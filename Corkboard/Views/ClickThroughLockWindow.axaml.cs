using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Corkboard.Views;

/// <summary>
///     锁定态下的「开锁」按钮：一颗贴在主窗口标题栏最右上角的小按钮。
///     <para>
///         为什么不能直接画在主窗口里：点击穿透是整窗 <c>WS_EX_TRANSPARENT</c>，
///         鼠标事件到不了窗口内任何控件——<b>连这颗按钮本身也点不动</b>。
///         而需求要求「就算开启了点击穿透，这个按钮也可以点击」，所以它必须待在另一个
///         不含穿透样式的窗口里。这与 <see cref="PageOverlayWindow" /> 是同一个套路：
///         主窗口收不到输入时，改用独立顶层窗口承载。
///     </para>
///     <para>
///         它只负责「显示 + 报告被点击」，不碰配置：关掉点击穿透由主窗口写
///         （见 <c>MainWindow</c> 的 <c>SyncLockBadge</c>），视图与配置写入分开。
///     </para>
///     <para>
///         刻意不做窗口级透明以外的事情：无边框、不进任务栏、不抢焦点、尺寸固定 28×28。
///         位置由 <see cref="PlaceAtTitleBarCorner" /> 按主窗口实时算出来——独立窗口不会
///         跟着主窗口走，移动/缩放时要重新摆一次。
///     </para>
/// </summary>
public partial class ClickThroughLockWindow : Window
{
    /// <summary>与主窗口标题栏内层 <c>Grid</c> 的 <c>Margin</c> 一致，锁定/解锁两态才在同一位置。</summary>
    private const double TitleBarRightMargin = 8;

    /// <summary>主窗口自绘标题栏高度（见 <c>MainWindow</c> 的 <c>TitleBar.Height</c>）。</summary>
    private const double TitleBarHeight = 48;

    public ClickThroughLockWindow()
    {
        InitializeComponent();

        // 在代码里赋值而不是写进 XAML：这个属性是 IReadOnlyList，XAML 那边转换规则不如直接写清楚。
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
    }

    /// <summary>用户点了这颗锁：请求关掉点击穿透。真正的配置写入与生效由主窗口负责。</summary>
    public event EventHandler? UnlockRequested;

    /// <summary>
    ///     把本窗口摆到主窗口标题栏最右上角，与标题栏里那颗按钮同位置同尺寸。
    ///     <para>
    ///         用 <see cref="VisualExtensions.PointToScreen" /> 而不是自己拿
    ///         <c>Window.Position</c> 加偏移算：主窗口「置底到桌面」时是桌面宿主的子窗口，
    ///         子窗口坐标不是屏幕坐标，只有原生那条换算路径能算对。
    ///     </para>
    /// </summary>
    public void PlaceAtTitleBarCorner(Window mainWindow)
    {
        var origin = mainWindow.PointToScreen(default);
        var scaling = mainWindow.RenderScaling;

        Position = new PixelPoint(
            origin.X + (int)Math.Round((mainWindow.ClientSize.Width - TitleBarRightMargin - Width) * scaling),
            origin.Y + (int)Math.Round((TitleBarHeight - Height) / 2 * scaling));
    }

    private void UnlockButton_OnClick(object? sender, RoutedEventArgs e) =>
        UnlockRequested?.Invoke(this, EventArgs.Empty);

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
