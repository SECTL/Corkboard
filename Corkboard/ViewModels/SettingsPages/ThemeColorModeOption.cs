using Corkboard.Core.Enums.Configs;

namespace Corkboard.ViewModels.SettingsPages;

/// <summary>主题色来源下拉项：落盘的枚举值 + 下拉里显示的名字。</summary>
/// <param name="Mode">写进配置的枚举值（<see cref="ThemeColorMode" />）。</param>
/// <param name="DisplayName">显示名取自资源，界面语言变了会跟着变。</param>
public sealed record ThemeColorModeOption(ThemeColorMode Mode, string DisplayName);
