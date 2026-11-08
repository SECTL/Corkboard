namespace Corkboard.Core.DutyRoster;

/// <summary>
///     节假日 / 假期判定契约。值日表只问三个问题，不关心数据从哪来（联网缓存、内置数据、
///     用户手动覆盖最终都由 <c>HolidayService</c> 合成后实现本接口）。
/// </summary>
public interface IDutyHolidayCalendar
{
    /// <summary>法定节假日（含春节、国庆等放假日）：默认不值日。</summary>
    bool IsLegalHoliday(DateOnly date);

    /// <summary>调休补班日（<c>isOffDay = false</c>）：照常值日，哪怕落在周末。</summary>
    bool IsMakeupWorkday(DateOnly date);

    /// <summary>用户自建的假期区间（寒暑假等）：不值日。</summary>
    bool IsVacation(DateOnly date);
}

/// <summary>什么都不跳过的空实现：单元测试与「节假日模块未加载」时使用。</summary>
public sealed class NullDutyHolidayCalendar : IDutyHolidayCalendar
{
    public static readonly NullDutyHolidayCalendar Instance = new();

    public bool IsLegalHoliday(DateOnly date) => false;

    public bool IsMakeupWorkday(DateOnly date) => false;

    public bool IsVacation(DateOnly date) => false;
}
