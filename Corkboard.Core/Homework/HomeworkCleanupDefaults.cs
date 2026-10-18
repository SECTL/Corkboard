

namespace Corkboard.Core.Homework;

/// <summary>
///     自动清理的默认值与取值范围。
///     <para>
///         设置页控件的上下限、清理器读配置时的夹取都取自这里：两处各写一份的话，
///         很容易出现「界面允许填 25 点、判定时又被夹回 23」这种对不上的情况。
///         手改过 settings.json 的脏数据也会被夹进合法区间，不需要额外的迁移步骤。
///     </para>
/// </summary>
public static class HomeworkCleanupDefaults
{
    /// <summary>默认每天 8 点动手：早上一开始，板子上就只剩还没过截止日期的作业。</summary>
    public const int DefaultHour = 8;

    public const int DefaultMinute = 0;

    public static int ClampHour(int hour) => Math.Clamp(hour, 0, 23);

    public static int ClampMinute(int minute) => Math.Clamp(minute, 0, 59);
}
