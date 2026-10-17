using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Corkboard.Views;

/// <summary>
///     作业板收起之后留在屏幕上的那颗展开按钮：一扇无边框、透明、置顶、不进任务栏的小窗口，
///     里面只有一颗圆按钮，点一下就把作业板展开回来。
///     <para>
///         为什么是独立窗口：主窗口此时已经 <see cref="Window.Hide" /> 掉了，按钮不能在它里面；
///         而且主窗口可能正开着点击穿透 / 不透明度 / 置底，这些能力是整窗的，
///         浮钮需要自己的一扇干净窗口才点得动、看得清。
///     </para>
///     <para>
///         窗口不负责状态与位置：显隐由 <c>BoardDockService</c> 管，
///         位置与大小也由它按配置算好后调用 <see cref="SetButtonDiameter" /> 与 <c>Position</c> 写进来。
///         这样「配置 → 浮钮」只有一条路径，不会两处各自摆一遍。
///     </para>
/// </summary>
public partial class BoardDockWindow : Window
{
    private Button? _expandButton;
    private TextBlock? _expandIcon;

    public BoardDockWindow()
    {
        InitializeComponent();

        // 在代码里赋值而不是写进 XAML：这个属性是 IReadOnlyList，XAML 那边转换规则不如直接写清楚
        // （与 ClickThroughLockWindow 同一个理由）。窗口背景必须是透明的，否则圆按钮四角会露出方块底色。
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        _expandButton = this.FindControl<Button>("ExpandButton");
        _expandIcon = this.FindControl<TextBlock>("ExpandIcon");
    }

    /// <summary>用户点了浮钮：请求展开作业板。</summary>
    public event EventHandler? ExpandRequested;

    /// <summary>
    ///     按「设置 → 作业板 → 收纳 → 按钮大小」把窗口与按钮一起缩放。
    ///     窗口尺寸就是按钮尺寸（窗口刚好包住它），圆角与图标字号按同一比例跟，
    ///     这样 60% 与 200% 看起来是同一颗按钮，而不是一颗被裁掉或缩在角落的按钮。
    /// </summary>
    public void SetButtonDiameter(double diameter)
    {
        var size = Math.Max(1, diameter);

        Width = size;
        Height = size;

        if (_expandButton is { } button)
        {
            button.Width = size;
            button.Height = size;
            button.CornerRadius = new CornerRadius(size / 2);
        }

        if (_expandIcon is { } icon)
            icon.FontSize = Math.Max(10, size * 0.42);
    }

    private void ExpandButton_OnClick(object? sender, RoutedEventArgs e) =>
        ExpandRequested?.Invoke(this, EventArgs.Empty);

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
