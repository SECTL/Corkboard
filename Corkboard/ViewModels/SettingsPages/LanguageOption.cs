using Corkboard.Core.Enums.Configs;

namespace Corkboard.ViewModels.SettingsPages;

/// <summary>语言下拉项：落盘的枚举值 + 下拉里显示的名字。</summary>
/// <param name="Mode">写进配置的枚举值。</param>
/// <param name="DisplayName">显示名固定用该语言自身的写法，界面语言变了也不会变成枚举名。</param>
public sealed record LanguageOption(LanguageMode Mode, string DisplayName);
