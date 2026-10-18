using Corkboard.Core.Enums.Configs;

namespace Corkboard.ViewModels.SettingsPages;

/// <summary>在线状态下拉项：配置值 + 本地化显示名（与 <see cref="LanguageOption" /> 同一种做法）。</summary>
public sealed record OnlineStatusOption(OnlineStatusMode Mode, string DisplayName);
