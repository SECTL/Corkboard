using Corkboard.Core.Homework;

namespace Corkboard.ViewModels.SettingsPages.Homework;

/// <summary>清理周期下拉项：枚举值加界面上的名字，形状同 <see cref="BoardLayoutOption" />。</summary>
public sealed record HomeworkCleanupFrequencyOption(HomeworkCleanupFrequency Frequency, string DisplayName);
