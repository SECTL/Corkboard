using Corkboard.Core.Enums.Configs;

namespace Corkboard.ViewModels.SettingsPages.Board;

/// <summary>「拖动窗口」下拉项：三档枚举值 + 下拉里显示的名字。</summary>
/// <param name="Mode">这一项代表的档位。</param>
/// <param name="DisplayName">显示名取自资源，界面语言变了会跟着变。</param>
public sealed record WindowDragModeOption(WindowDragMode Mode, string DisplayName);

/// <summary>「锁定按钮做什么」下拉项：两档枚举值 + 下拉里显示的名字。</summary>
/// <param name="Mode">这一项代表的锁定行为。</param>
/// <param name="DisplayName">显示名取自资源，界面语言变了会跟着变。</param>
public sealed record WindowLockModeOption(WindowLockMode Mode, string DisplayName);
