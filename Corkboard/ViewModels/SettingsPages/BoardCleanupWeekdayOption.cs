namespace Corkboard.ViewModels.SettingsPages;

/// <summary>「星期几」下拉项，只在按周清理时露出来。</summary>
public sealed record BoardCleanupWeekdayOption(DayOfWeek Weekday, string DisplayName);
