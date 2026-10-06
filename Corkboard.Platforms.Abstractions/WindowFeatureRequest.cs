namespace Corkboard.Platforms.Abstractions;

[Flags]
public enum WindowFeatures
{
    None = 0,
    Topmost = 1 << 0,
    ToolWindow = 1 << 1,
    SkipTaskSwitcher = 1 << 2,
    NoActivate = 1 << 3,
    ClickThrough = 1 << 4,
    ExcludeFromCapture = 1 << 5,

    /// <summary>
    ///     把窗口嵌进桌面层（Windows 上是承载桌面图标的 <c>Progman</c> / <c>WorkerW</c>），
    ///     让它成为桌面窗口树的一部分：被所有普通窗口盖住，并且不随「显示桌面」一起被隐藏。
    ///     <para>
    ///         这与「普通的窗口压到 Z 序最底」不是一回事；退化行为由平台实现决定，并通过
    ///     <see cref="WindowFeatureApplyResult" /> 反馈给调用方。
    ///     </para>
    /// </summary>
    DesktopBottom = 1 << 6,

    /// <summary>
    ///     窗口整体不透明度。这是唯一带**标量**的能力：具体值取
    ///     <see cref="WindowFeatureRequest.Opacity" />，<see cref="WindowFeatureRequest.Enabled" />
    ///     为 false 时按 1.0（完全不透明）处理。
    /// </summary>
    WindowOpacity = 1 << 7
}

/// <summary>
///     窗口能力请求。
/// </summary>
/// <param name="Features">要请求的能力位；未实现的位会由平台报成「不支持」。</param>
/// <param name="Enabled">按位请求；false 表示取消该能力（对 <see cref="WindowFeatures.WindowOpacity" /> 是恢复完全不透明）。</param>
/// <param name="Opacity">
///     窗口不透明度，只在请求 <see cref="WindowFeatures.WindowOpacity" /> 时有意义：0–1，1 为完全不透明。
///     平台实现会自己夹到安全区间（不允许低到窗口彻底看不见）。
/// </param>
public readonly record struct WindowFeatureRequest(WindowFeatures Features, bool Enabled, double Opacity = 1.0);
