namespace Corkboard.Core.Time;

/// <summary>
///     NTP 时间戳（64 位定点数）与 <see cref="DateTimeOffset" /> 之间的换算。
///     <para>
///         高 32 位是从 <see cref="Epoch" />（1900-01-01T00:00:00Z）起数的<b>无符号</b>秒数，
///         低 32 位是这一秒里的小数部分（1/2^32 秒 ≈ 233 皮秒，比 .NET 的 tick 还细）。
///     </para>
///     <para>
///         ⚠️ 秒数字段只有 32 位，最多装 2^32 秒 ≈ 136 年，于是 2036-02-07 之后它会回卷成 0 重新数
///         （NTP 的「era」问题）。报文里没有任何字段说明这是第几个周期，所以解释方式只能是
///         「<b>离现在最近的那个周期</b>」：先把秒数当成 1900 起的值算出一个时间，如果它比现在早了
///         半个周期（≈ 68 年）以上，就再加一个周期。这样 2026 年的客户端收到秒数 0 会解释成 2036 年，
///         而不是 1900 年。
///     </para>
///     <para>
///         也正因为要判「离现在最近」，纯函数里需要一个「现在」——<see cref="ToDateTimeOffset(uint, uint)" />
///         用 <see cref="DateTimeOffset.UtcNow" />，带 <c>now</c> 参数的重载留给单测注入固定时刻，
///         否则跨过 2036 年那条线用例就会自己失效。
///     </para>
/// </summary>
public static class NtpTimestamp
{
    /// <summary>NTP 纪元起点：1900-01-01T00:00:00Z（NTP 的秒数从这里开始数，不是 Unix 的 1970）。</summary>
    public static readonly DateTimeOffset Epoch = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>一个周期有多少秒：2^32 ≈ 136 年，也就是秒数字段回卷一圈的长度。</summary>
    private const long EraSeconds = 1L << 32;

    /// <summary>半个周期（≈ 68 年），判定「这个秒数该落在哪个周期」时用的界限。</summary>
    private const long HalfEraSeconds = EraSeconds / 2;

    /// <summary>小数部分的刻度数：2^32 等分一秒。</summary>
    private const ulong FractionScale = 1UL << 32;

    /// <summary>
    ///     把 NTP 时间戳换算成 UTC 时间，按「离 <see cref="DateTimeOffset.UtcNow" /> 最近的周期」解释回卷。
    /// </summary>
    public static DateTimeOffset ToDateTimeOffset(uint seconds, uint fraction)
        => ToDateTimeOffset(seconds, fraction, DateTimeOffset.UtcNow);

    /// <summary>
    ///     把 NTP 时间戳换算成 UTC 时间，按「离 <paramref name="now" /> 最近的周期」解释回卷。
    ///     <para>
    ///         显式传 <paramref name="now" /> 只为了可测：回卷判定本身依赖「当前是第几个周期」，
    ///         写死真实时钟的话用例过了 2036 年就会静默变红。
    ///     </para>
    /// </summary>
    public static DateTimeOffset ToDateTimeOffset(uint seconds, uint fraction, DateTimeOffset now)
    {
        var value = Epoch.AddTicks((long)seconds * TimeSpan.TicksPerSecond + FractionToTicks(fraction));

        // 半个周期以前的时间一定属于下一个 era：秒数回卷过，但报文里不会告诉你。
        var threshold = now.ToUniversalTime().AddTicks(-HalfEraSeconds * TimeSpan.TicksPerSecond);
        while (value < threshold) value = value.AddTicks(EraSeconds * TimeSpan.TicksPerSecond);

        return value;
    }

    /// <summary>
    ///     把 UTC 时间换算成 NTP 时间戳。早于 <see cref="Epoch" /> 的时间无法表示，退回 (0, 0)。
    ///     <para>
    ///         小数部分向下取整写入，读回时由 <see cref="FractionToTicks" /> 按四舍五入还原：
    ///         1/2^32 秒比一个 tick（100ns）细得多，两头都用取整会让「写进去再读出来」差 1 tick，
    ///         四舍五入能精确回到原来的 tick。
    ///     </para>
    /// </summary>
    public static (uint Seconds, uint Fraction) FromDateTimeOffset(DateTimeOffset value)
    {
        var deltaTicks = value.UtcTicks - Epoch.UtcTicks;
        if (deltaTicks <= 0) return (0u, 0u);

        var wholeSeconds = (ulong)(deltaTicks / TimeSpan.TicksPerSecond);
        var remainderTicks = (ulong)(deltaTicks % TimeSpan.TicksPerSecond);
        var fraction = (uint)(remainderTicks * FractionScale / (ulong)TimeSpan.TicksPerSecond);

        // 秒数只留低 32 位：2036 之后写出去的就是回卷后的值，与真实报文一致。
        return ((uint)(wholeSeconds & (EraSeconds - 1)), fraction);
    }

    /// <summary>
    ///     把小数部分（2^32 等分一秒）折成 tick 数，四舍五入到最近的 tick。
    ///     <para>
    ///         写入时是向下取整，所以这里必须四舍五入而不是继续向下取整，否则往返会稳定少 1 tick。
    ///     </para>
    /// </summary>
    private static long FractionToTicks(uint fraction)
        => (long)(((ulong)fraction * (ulong)TimeSpan.TicksPerSecond + FractionScale / 2) / FractionScale);
}
