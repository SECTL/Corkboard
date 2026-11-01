using Corkboard.Core.Enums.Configs;

namespace Corkboard.ViewModels.SettingsPages;

/// <summary>崩溃恢复下拉项：配置值 + 本地化显示名（与 <see cref="OnlineStatusOption" /> 同一种做法）。</summary>
public sealed record CrashRecoveryModeOption(CrashRecoveryMode Mode, string DisplayName);
