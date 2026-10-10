namespace Corkboard.Core.Enums.Configs;

/// <summary>
///     自动清理的动手频率：每隔多久清一次。
///     <para>
///         频率只决定「什么时候动手」，决定「哪些作业算过期」的是保留天数与截止日期宽限，
///         两组设置互不影响。
///     </para>
///     <para>
///         枚举按数字落盘（见 <c>ConfigServiceBase.JsonOptions</c>），只能在<b>末尾</b>增删成员。
///     </para>
/// </summary>
public enum BoardCleanupFrequency
{
    /// <summary>每天在指定时刻动手一次。</summary>
    Daily = 0,

    /// <summary>每周在指定星期几的指定时刻动手一次。</summary>
    Weekly = 1
}
