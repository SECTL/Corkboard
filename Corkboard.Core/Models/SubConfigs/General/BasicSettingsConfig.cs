using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Attributes;
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
    [property: LocalOnly]
    [ObservableProperty] private bool _autostart;

    /// <summary>
    ///     启动时显示作业板。<b>默认开</b>：正常启动与开机自启都以「显示窗口」为默认，
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
    ///     锁定窗口：位置与大小都锁住——标题栏拖不动、四边也拉不动，板子照常查看与编辑。
    ///     <b>默认锁着</b>，作业板平时就该待在原地。
    ///     <para>
    ///         解锁（置假）是「调整窗口」态：标题栏可以拖动、四边可以缩放，但内容区会盖一层遮罩，
    ///         遮罩期间不能编辑作业、也不能布置作业（挪窗口时最不想误删一张便签）。
    ///         这一项是标题栏那颗锁的<b>唯一</b>状态来源，键名冻结。
    ///     </para>
    /// </summary>
    [property: LocalOnly]
    [ObservableProperty] private bool _windowFixed = true;

    /// <summary>
    ///     主窗口整体不透明度：1 完全不透明，越小越透（下限见设置页与平台实现）。
    ///     它是**窗口**的属性而不是页面画刷的透明度——整块窗口（含作业内容）一起淡下去，
    ///     由平台实现写进原生窗口（Windows 走分层窗口的 alpha）。用户可见文案见
    ///     <c>Settings_Board_MainWindowOpacity</c>。
    /// </summary>
    [property: LocalOnly]
    [ObservableProperty] private double _mainWindowOpacity = 1.0;

    /// <summary>
    ///     窗口圆角：开着时主窗口四角按固定半径收圆，默认关（用户要的是「能开」）。
    ///     <para>
    ///         主窗口是 <c>WindowDecorations.None</c>，系统不会替它画圆角；置底到桌面后它还是桌面子窗口，
    ///         DWM 也不再给这块单独合成圆角。所以落地在平台侧：
    ///         普通窗口走 <c>DWMWA_WINDOW_CORNERPREFERENCE</c>，置底成子窗口走
    ///         <c>CreateRoundRectRgn</c> + <c>SetWindowRgn</c>（见 <c>WindowsWindowFeatureService</c>）。
    ///     </para>
    ///     用户可见文案见 <c>Settings_Board_WindowCorners</c>。
    /// </summary>
    [property: LocalOnly]
    [ObservableProperty] private bool _mainWindowRoundedCorners;

    /// <summary>
    ///     右下角布置作业按钮：在主窗口右下角浮一颗圆角按钮，点一下就能开布置作业表单。
    ///     默认关。用户可见文案见 <c>Settings_Board_CornerAssignButton</c>。
    /// </summary>
    [ObservableProperty] private bool _showCornerAssignButton;

    // 已经删掉的键：click_through / window_lock_mode / allow_add_note_when_locked / hold_to_drag_window。
    // 「点击穿透」与「长按拖动」两个功能整体下过架，老配置里这些键读进来会被 JSON 反序列化静默忽略，
    // 不需要迁移；**也不要**为了兼容把它们加回来。

    // 与用户开关分开存放：关掉「记住窗口尺寸」时要保留上一次的尺寸。
    [property: LocalOnly]
    [ObservableProperty] private double _mainWindowWidth = 1200;
    [property: LocalOnly]
    [ObservableProperty] private double _mainWindowHeight = 800;
    [property: LocalOnly]
    [ObservableProperty] private bool _mainWindowMaximized;
    [property: LocalOnly]
    [ObservableProperty] private double _settingsWindowWidth = 1000;
    [property: LocalOnly]
    [ObservableProperty] private double _settingsWindowHeight = 720;
    [property: LocalOnly]
    [ObservableProperty] private bool _settingsWindowMaximized;

    /// <summary>
    ///     最大化时的实际大小（0 = 还没记过）。它跟上面的普通宽高是两套键：最大化由系统定尺寸，
    ///     和用户自己拖出来的普通尺寸没有可比性，共有一套键的话，还原之后普通尺寸就被顶掉了。
    ///     <para>
    ///         记录点在 <c>MainWindow</c>：窗口此刻真的最大化着才写（最小化时 Bounds 已经挪到屏幕外）。
    ///         平时不读它——最大化与否由系统的可用区决定，套用旧值反而会让窗口跑到屏幕外面去。
    ///     </para>
    /// </summary>
    [property: LocalOnly]
    [ObservableProperty] private double _mainWindowMaximizedWidth;

    /// <inheritdoc cref="_mainWindowMaximizedWidth" />
    [property: LocalOnly]
    [ObservableProperty] private double _mainWindowMaximizedHeight;

    /// <inheritdoc cref="_mainWindowMaximizedWidth" />
    [property: LocalOnly]
    [ObservableProperty] private double _settingsWindowMaximizedWidth;

    /// <inheritdoc cref="_mainWindowMaximizedWidth" />
    [property: LocalOnly]
    [ObservableProperty] private double _settingsWindowMaximizedHeight;

    // 主窗口的位置记忆。与尺寸同理，跟开关分开存放：关掉「记住窗口尺寸」时保留上一次的值。
    // 位置换算与夹取在 Corkboard.Core.Helpers.WindowPositionMemory 里（纯计算，有单测）。

    /// <summary>
    ///     上次主窗口左上角相对主屏工作区左上角的偏移（设备像素），
    ///     <see cref="WindowPositionMemory.Unset" /> 表示还没记过。
    /// </summary>
    [property: LocalOnly]
    [ObservableProperty] private int _mainWindowPositionX = WindowPositionMemory.Unset;

    /// <inheritdoc cref="_mainWindowPositionX" />
    [property: LocalOnly]
    [ObservableProperty] private int _mainWindowPositionY = WindowPositionMemory.Unset;

    /// <summary>
    ///     记下位置那一刻的主屏工作区大小（0 = 还没记过）。
    ///     屏幕尺寸变化时用它算缩放比例，把偏移等比缩到新工作区上（见 <c>WindowPositionMemory.Resolve</c>）。
    /// </summary>
    [property: LocalOnly]
    [ObservableProperty] private int _mainWindowScreenWidth;

    /// <inheritdoc cref="_mainWindowScreenWidth" />
    [property: LocalOnly]
    [ObservableProperty] private int _mainWindowScreenHeight;

    // 隐藏配置项：首次运行引导与协议确认状态。
    //
    // 这几个键只由引导窗口写（见 Corkboard.Core.Services.FirstRun.FirstRunOobeService）：
    // GuideCompleted 为假 = 这一份配置从没走过引导，启动时先把引导窗口铺出来；
    // 两个版本号用来在协议改版后只补「法律」那一步，不重跑整段引导。

    /// <summary>引导是否已经走完。配置备份里带着它，恢复备份后不会重跑引导。</summary>
    [ObservableProperty] private bool _guideCompleted;

    /// <summary>用户确认过的用户协议版本（0 = 没确认过）。</summary>
    [ObservableProperty] private int _acceptedEulaVersion;

    /// <summary>用户确认过的隐私政策版本（0 = 没确认过）。</summary>
    [ObservableProperty] private int _acceptedPrivacyPolicyVersion;
}
