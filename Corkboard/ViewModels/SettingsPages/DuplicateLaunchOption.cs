using Corkboard.Core.Enums.Configs;

namespace Corkboard.ViewModels.SettingsPages;

/// <summary>「再次启动时打开什么」下拉项：配置值 + 本地化显示名（与 <see cref="CrashRecoveryModeOption" /> 同一种做法）。</summary>
public sealed record DuplicateLaunchOption(DuplicateLaunchMode Mode, string DisplayName);
