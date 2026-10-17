namespace Corkboard.ViewModels.SettingsPages.Homework;

/// <summary>「星期几」下拉项，只在按周清理时露出来。</summary>
public sealed record HomeworkCleanupWeekdayOption(DayOfWeek Weekday, string DisplayName);
