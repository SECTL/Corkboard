using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Corkboard.Views;

/// <summary>
///     锁定态下的右下角「布置作业」浮动按钮：一颗贴在主窗口右下角的大号圆角按钮。
///     <para>
///         为什么不能直接画在主窗口里：点击穿透是整窗 <c>WS_EX_TRANSPARENT</c>，
///         鼠标事件到不了窗口内任何控件——<b>连按钮本身也点不动</b>。
///         而「锁定时也能添加作业」要求穿透状态下仍能点布置作业，所以按钮必须待在另一个
///         不含穿透样式的窗口里。与 <see cref="ClickThroughLockWindow" /> 同一个套路。
///     </para>
///     <para>
///         它只负责「显示 + 报告被点击」，不碰配置：关掉点击穿透 / 打开表单由主窗口负责。
///     </para>
///     <para>
///         尺寸 / 样式与主窗口右下角那颗保持一致（56×56、圆角 16、accent），
///         切换锁定态时两态按钮无缝衔接、不会跳。
///         位置由 <see cref="PlaceAtBottomRightCorner" /> 按主窗口实时算出来。
///     </para>
/// </summary>
public partial class CornerAssignFloatingWindow : Window
{
    /// <summary>按钮尺寸（宽=高），与主窗口右下角那颗一致。</summary>
    private const double ButtonSize = 56;

    /// <summary>
    ///     右边距，与主窗口右下角按钮外层 Panel 的 <c>Margin="0 0 20 20"</c> 一致。
    /// </summary>
    private const double RightMargin = 20;

    /// <summary>
    ///     下边距，与主窗口右下角按钮外层 Panel 的 <c>Margin="0 0 20 20"</c> 一致。
    /// </summary>
    private const double BottomMargin = 20;

    public CornerAssignFloatingWindow()
    {
        InitializeComponent();

        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
    }

    /// <summary>用户点了这颗按钮：请求打开布置作业表单。具体逻辑由主窗口负责。</summary>
    public event EventHandler? AssignRequested;

    /// <summary>
    ///     把本窗口摆到主窗口右下角，与主窗口内那颗布置作业按钮同位置同尺寸。
    ///     <para>
    ///         用 <see cref="VisualExtensions.PointToScreen" /> 而不是自己拿
    ///         <c>Window.Position</c> 加偏移算：主窗口「置底到桌面」时是桌面宿主的子窗口，
    ///         子窗口坐标不是屏幕坐标，只有原生那条换算路径能算对。
    ///     </para>
    /// </summary>
    public void PlaceAtBottomRightCorner(Window mainWindow)
    {
        var origin = mainWindow.PointToScreen(default);
        var scaling = mainWindow.RenderScaling;

        Position = new PixelPoint(
            origin.X + (int)Math.Round((mainWindow.ClientSize.Width - RightMargin - ButtonSize) * scaling),
            origin.Y + (int)Math.Round((mainWindow.ClientSize.Height - BottomMargin - ButtonSize) * scaling));
    }

    private void AssignButton_OnClick(object? sender, RoutedEventArgs e) =>
        AssignRequested?.Invoke(this, EventArgs.Empty);

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
