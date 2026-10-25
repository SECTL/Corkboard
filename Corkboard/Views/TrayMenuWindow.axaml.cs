using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Corkboard.Platforms.Abstractions;
using Corkboard.Services.Desktop;

namespace Corkboard.Views;

/// <summary>
///     托盘菜单（<b>左键</b>那份）：贴着鼠标弹出来的一张小菜单，条目与右键的原生菜单完全一致。
/// </summary>
/// <remarks>
///     <para>
///         为什么左键要自己画：Avalonia 的 Win32 托盘只在右键时把 <c>NativeMenu</c> 弹出来
///         （<c>WM_TRAYMOUSE</c> 的 lParam 514 → <c>Clicked</c>、517 → 弹菜单），左键只报一次点击。
///         用户要的是<b>左右键都能弹出菜单</b>，所以左键这条路得应用自己补一个菜单。
///         平台原生托盘本来就能左键出菜单的（macOS / Linux）不会走到这里，见
///         <see cref="PlatformCapabilities.SupportsTrayMenuOnLeftClick" />。
///     </para>
///     <para>
///         为什么是独立窗口：主窗口可能根本看不见（收纳成浮钮、或「启动时显示主窗口」关着），
///         挂在主窗口视觉树里的弹层/菜单会跟着一起没掉。窗口本身透明、无边框、不进任务栏、置顶，
///         菜单卡片由 <c>MenuFlyoutPresenter</c> 自己的模板画——与右键那份原生菜单同一套样式。
///     </para>
///     <para>
///         位置：从鼠标右下方展开；越过屏幕工作区就往回翻（底部任务栏上的托盘在右下角，
///         翻过来正好朝左上开，与系统菜单的习惯一致）。拿不到鼠标位置时退回主屏工作区右下角。
///         点任意一项、按 Esc、或者焦点被别的窗口拿走时自己关掉。
///     </para>
/// </remarks>
public partial class TrayMenuWindow : Window
{
    /// <summary>菜单与鼠标之间的留白（设备无关像素），免得菜单正好压在指针上。</summary>
    private const double CursorGapDip = 4;

    /// <summary>菜单里应用图标的边长（设备无关像素），按 Fluent 菜单的行高给。</summary>
    private const double MenuIconSize = 16;

    private bool _closing;

    /// <summary>
    ///     铺菜单条目的那个 <c>MenuFlyoutPresenter</c>。用 <c>FindControl</c> 取而不是吃 <c>x:Name</c>
    ///     生成的字段：本类自己写了 <see cref="InitializeComponent" />，生成的赋值语句不会跑，字段恒为 null。
    /// </summary>
    private MenuFlyoutPresenter MenuPresenter => this.FindControl<MenuFlyoutPresenter>("MenuHost")!;
    public TrayMenuWindow()
    {
        InitializeComponent();

        // 透明窗口 + 透明背景：不这样的话，无边框窗口的方块底色会在菜单卡片的圆角外面露出来。
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Deactivated += OnDeactivated;
    }

    /// <summary>
    ///     铺好条目并在鼠标处弹出。<paramref name="anchor" /> 为 <see langword="null" /> 时退回主屏右下角。
    /// </summary>
    /// <remarks>必须在 UI 线程上调用；同一个窗口实例只用来弹一次，关掉就丢。</remarks>
    public void ShowMenu(IReadOnlyList<TrayMenuItem> items, ScreenPoint? anchor)
    {
        FillMenu(items);

        // 先按锚点摆一次，别让它从屏幕左上角跳过来；真实大小要等布局跑完才知道，见 AlignToAnchor。
        if (anchor is { } point)
            Position = new PixelPoint(point.X, point.Y);

        Show();
        AlignToAnchor(anchor);
        Activate();
    }

    /// <summary>按 Esc 关掉菜单（没有选任何一项）。</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseMenu();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void OnDeactivated(object? sender, EventArgs e) => CloseMenu();

    /// <summary>关菜单（幂等）：失去焦点、按 Esc、选中一项都会走到这里。</summary>
    private void CloseMenu()
    {
        if (_closing)
            return;

        _closing = true;
        Close();
    }

    private void FillMenu(IReadOnlyList<TrayMenuItem> items)
    {
        var host = MenuPresenter;
        host.Items.Clear();

        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                host.Items.Add(new Separator());
                continue;
            }

            var action = item.Action;
            var menuItem = new MenuItem { Header = item.Header };

            // 顶上那项「图标 + 软件名」：图标用和窗口/托盘同一个母图，缩到菜单行高那么大。
            if (item.ShowApplicationIcon)
                menuItem.Icon = CreateMenuIcon();

            // 先关菜单再做事：退出/重启这类动作会把整个应用带走，窗口留着也没意义。
            menuItem.Click += (_, _) =>
            {
                CloseMenu();
                action?.Invoke();
            };

            host.Items.Add(menuItem);
        }
    }

    /// <summary>
    ///     菜单里那个应用图标。<see cref="MenuItem.Icon" /> 收的是控件内容，所以包一层 <see cref="Image" />；
    ///     尺寸按菜单行高给（16 DIP），再大只会把行撑高。
    /// </summary>
    private static Image CreateMenuIcon() => new()
    {
        Source = AppIcon.LoadBitmap(),
        Width = MenuIconSize,
        Height = MenuIconSize,
        Stretch = Stretch.Uniform
    };

    /// <summary>
    ///     把菜单摆到锚点旁边并夹进屏幕工作区。<b>必须在 <see cref="Window.Show" /> 之后调</b>：
    ///     <c>SizeToContent</c> 的真实尺寸要等这一轮布局跑完才拿得到。
    /// </summary>
    private void AlignToAnchor(ScreenPoint? anchor)
    {
        var scaling = RenderScaling;
        var gap = (int)Math.Round(CursorGapDip * scaling);
        var size = new PixelSize(
            (int)Math.Ceiling(ClientSize.Width * scaling),
            (int)Math.Ceiling(ClientSize.Height * scaling));

        var point = anchor ?? FallbackAnchor();
        var screen = Screens.ScreenFromPoint(new PixelPoint(point.X, point.Y)) ?? Screens.Primary;
        var area = screen?.WorkingArea ?? new PixelRect(new PixelPoint(point.X, point.Y), size);

        var x = point.X + gap;
        var y = point.Y + gap;

        // 放不下就翻到鼠标的另一侧（左上），这正是系统托盘菜单的样子。
        if (x + size.Width > area.Right)
            x = point.X - size.Width - gap;
        if (y + size.Height > area.Bottom)
            y = point.Y - size.Height - gap;

        x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - size.Width));
        y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - size.Height));

        Position = new PixelPoint(x, y);
    }

    /// <summary>拿不到鼠标位置时的兜底锚点：主屏工作区右下角（托盘通常就在那儿附近）。</summary>
    private ScreenPoint FallbackAnchor()
    {
        if (Screens.Primary?.WorkingArea is not { } area)
            return new ScreenPoint(0, 0);

        return new ScreenPoint(area.Right, area.Bottom);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
