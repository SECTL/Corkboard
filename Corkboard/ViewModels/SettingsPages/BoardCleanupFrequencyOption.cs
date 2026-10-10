using Corkboard.Core.Enums.Configs;

namespace Corkboard.ViewModels.SettingsPages;

/// <summary>清理周期下拉项：枚举值加界面上的名字，形状同 <see cref="BoardLayoutOption" />。</summary>
public sealed record BoardCleanupFrequencyOption(BoardCleanupFrequency Frequency, string DisplayName);
