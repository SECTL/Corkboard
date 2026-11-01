using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Enums.Configs;

namespace Corkboard.Core.Display;

/// <summary>
///     自动进出的一段时段：哪几天、几点进、几点出。
///     <para>
///         「进」「出」两个时刻支持跨零点（22:00 进、02:00 出＝今晚到明晨那一段），
///         判据只是 <c>enter &gt; exit</c>，与 <see cref="DisplaySchedulePolicy" /> 同一套口径。
///     </para>
///     <para>
///         ⚠️ 它是配置对象图里的一层集合元素，<c>ConfigHandlerBase</c> 的递归订阅走不到集合元素，
///         所以设置页改完一个时段要显式 <c>MainConfigHandler.Save()</c> 一次，
///         不要把「改了自然会落盘」当成理所当然。
///     </para>
/// </summary>
public partial class DisplaySlot : ObservableObject
{
    /// <summary>最多能配几段（再多设置页也放不下，看门狗每拍都要扫一遍）。</summary>
    public const int MaxCount = 8;

    /// <summary>这一段参不参加（关掉之后留着不动，但不会再自动进出）。默认参加。</summary>
    [ObservableProperty] private bool _enabled = true;

    /// <summary>哪几天生效。默认每天。</summary>
    [ObservableProperty] private DisplayDayFlags _days = DisplayDayFlags.EveryDay;

    /// <summary>自动进入的小时（0–23）。</summary>
    [ObservableProperty] private int _enterHour = DisplaySchedulePolicy.DefaultEnterTime.Hour;

    /// <summary>自动进入的分钟（0–59）。</summary>
    [ObservableProperty] private int _enterMinute = DisplaySchedulePolicy.DefaultEnterTime.Minute;

    /// <summary>自动退出的小时（0–23）。小于进入时刻时视为跨零点。</summary>
    [ObservableProperty] private int _exitHour = DisplaySchedulePolicy.DefaultExitTime.Hour;

    /// <summary>自动退出的分钟（0–59）。</summary>
    [ObservableProperty] private int _exitMinute = DisplaySchedulePolicy.DefaultExitTime.Minute;

    /// <summary>进入时刻（由两个数拼出来，不落盘）。</summary>
    [JsonIgnore]
    public TimeOnly EnterTime => DisplaySchedulePolicy.ResolveTime(EnterHour, EnterMinute);

    /// <summary>退出时刻（由两个数拼出来，不落盘）。</summary>
    [JsonIgnore]
    public TimeOnly ExitTime => DisplaySchedulePolicy.ResolveTime(ExitHour, ExitMinute);

    /// <summary>这一段是不是零长度（进出同一时刻——那样永远进不去）。</summary>
    [JsonIgnore]
    public bool IsEmpty => EnterTime == ExitTime;

    /// <summary>按默认时刻新建一段。</summary>
    public static DisplaySlot CreateDefault() => new();

    /// <summary>把时刻夹进合法区间、星期折成合法掩码。</summary>
    public void Normalize()
    {
        EnterHour = Math.Clamp(EnterHour, 0, 23);
        EnterMinute = Math.Clamp(EnterMinute, 0, 59);
        ExitHour = Math.Clamp(ExitHour, 0, 23);
        ExitMinute = Math.Clamp(ExitMinute, 0, 59);
        Days = DisplayDayFlagsExtensions.Normalize(Days);
    }

    /// <summary>复制一份（URI 临时时段要在副本上改，不能动用户存下来的配置）。</summary>
    public DisplaySlot Clone() => new()
    {
        Enabled = Enabled,
        Days = Days,
        EnterHour = EnterHour,
        EnterMinute = EnterMinute,
        ExitHour = ExitHour,
        ExitMinute = ExitMinute
    };
}
