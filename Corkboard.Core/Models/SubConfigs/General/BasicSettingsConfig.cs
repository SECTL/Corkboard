using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Helpers;

namespace Corkboard.Core.Models.SubConfigs.General;

/// <summary>基础设置：语言、启动行为、窗口尺寸记忆。</summary>
public partial class BasicSettingsConfig : ObservableObject
{
    [ObservableProperty] private LanguageMode _language = LanguageMode.ChineseSimplified;

    /// <summary>
    ///     开机自启。配置里只保存用户的意愿，真正的系统集成（注册表 / 桌面项 / LaunchAgent）
    ///     由 <c>IAutostartService</c> 完成；写入失败时设置页会把它回滚，绝不留下假成功。
    /// </summary>
    [ObservableProperty] private bool _autostart;

    /// <summary>
    ///     启动时显示主窗口。<b>默认开</b>：正常启动与开机自启都以「显示窗口」为默认，
    ///     只有用户主动关掉才改为只常驻托盘（托盘图标点击仍能唤出）。
    /// </summary>
    [ObservableProperty] private bool _showStartupWindow = true;

    /// <summary>记住窗口尺寸：关掉后不再写入，但已保存的尺寸保留（见 MainWindow）。</summary>
    [ObservableProperty] private bool _autoSaveWindowSize = true;

    [ObservableProperty] private bool _urlProtocol;

    /// <summary>
    ///     置底到桌面：把主窗口嵌进桌面层，被所有普通窗口盖住（只有 Windows 实现了这个能力，
    ///     其余平台会把它报成不支持，设置页据此禁用开关）。
    /// </summary>
    [ObservableProperty] private bool _pinToDesktop;

    /// <summary>
    ///     长按拖动：按住主窗口<b>任意位置</b>（含按钮、输入框、列表）约半秒即可拖动窗口；
    ///     短按与拖动方向的手势仍归控件。关闭后只有顶部标题栏能拖动。
    ///     用户可见文案见 <c>Settings_Basic_HoldToDragWindow</c>。
    /// </summary>
    [ObservableProperty] private bool _holdToDragWindow = true;

    /// <summary>
    ///     主窗口整体不透明度：1 完全不透明，越小越透（下限见设置页与平台实现）。
    ///     它是**窗口**的属性而不是页面画刷的透明度——整块窗口（含作业内容）一起淡下去，
    ///     由平台实现写进原生窗口（Windows 走分层窗口的 alpha）。用户可见文案见
    ///     <c>Settings_Basic_MainWindowOpacity</c>。
    /// </summary>
    [ObservableProperty] private double _mainWindowOpacity = 1.0;

    /// <summary>
    ///     点击穿透：主窗口不再接收鼠标与触摸（连按钮、输入框都点不动），
    ///     只剩托盘菜单这个入口能把设置窗口再叫出来。默认关。
    ///     用户可见文案见 <c>Settings_Basic_ClickThrough</c>。
    /// </summary>
    [ObservableProperty] private bool _clickThrough;

    /// <summary>
    ///     右下角布置作业按钮：在主窗口右下角浮一颗圆角按钮，点一下就能开布置作业表单。
    ///     默认关。用户可见文案见 <c>Settings_Basic_CornerAssignButton</c>。
    /// </summary>
    [ObservableProperty] private bool _showCornerAssignButton;

    // 与用户开关分开存放：关掉「记住窗口尺寸」时要保留上一次的尺寸。
    [ObservableProperty] private double _mainWindowWidth = 1200;
    [ObservableProperty] private double _mainWindowHeight = 800;
    [ObservableProperty] private bool _mainWindowMaximized;
    [ObservableProperty] private double _settingsWindowWidth = 1000;
    [ObservableProperty] private double _settingsWindowHeight = 720;
    [ObservableProperty] private bool _settingsWindowMaximized;

    /// <summary>
    ///     最大化时的实际大小（0 = 还没记过）。它跟上面的普通宽高是两套键：最大化由系统定尺寸，
    ///     和用户自己拖出来的普通尺寸没有可比性，共有一套键的话，还原之后普通尺寸就被顶掉了。
    ///     <para>
    ///         记录点在 <c>MainWindow</c>：窗口此刻真的最大化着才写（最小化时 Bounds 已经挪到屏幕外）。
    ///         平时不读它——最大化与否由系统的可用区决定，套用旧值反而会让窗口跑到屏幕外面去。
    ///     </para>
    /// </summary>
    [ObservableProperty] private double _mainWindowMaximizedWidth;

    /// <inheritdoc cref="_mainWindowMaximizedWidth" />
    [ObservableProperty] private double _mainWindowMaximizedHeight;

    /// <inheritdoc cref="_mainWindowMaximizedWidth" />
    [ObservableProperty] private double _settingsWindowMaximizedWidth;

    /// <inheritdoc cref="_mainWindowMaximizedWidth" />
    [ObservableProperty] private double _settingsWindowMaximizedHeight;

    // 主窗口的位置记忆。与尺寸同理，跟开关分开存放：关掉「记住窗口尺寸」时保留上一次的值。
    // 位置换算与夹取在 Corkboard.Core.Helpers.WindowPositionMemory 里（纯计算，有单测）。

    /// <summary>
    ///     上次主窗口左上角相对主屏工作区左上角的偏移（设备像素），
    ///     <see cref="WindowPositionMemory.Unset" /> 表示还没记过。
    /// </summary>
    [ObservableProperty] private int _mainWindowPositionX = WindowPositionMemory.Unset;

    /// <inheritdoc cref="_mainWindowPositionX" />
    [ObservableProperty] private int _mainWindowPositionY = WindowPositionMemory.Unset;

    /// <summary>
    ///     记下位置那一刻的主屏工作区大小（0 = 还没记过）。
    ///     屏幕尺寸变化时用它算缩放比例，把偏移等比缩到新工作区上（见 <c>WindowPositionMemory.Resolve</c>）。
    /// </summary>
    [ObservableProperty] private int _mainWindowScreenWidth;

    /// <inheritdoc cref="_mainWindowScreenWidth" />
    [ObservableProperty] private int _mainWindowScreenHeight;

    // 隐藏配置项：首次运行引导与协议确认状态。
    [ObservableProperty] private bool _guideCompleted;
    [ObservableProperty] private int _acceptedEulaVersion;
}
