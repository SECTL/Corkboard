namespace Corkboard.Core.Time;

/// <summary>
///     一次校时的结果：成功时带着偏差与时延，失败时只带着原因，绝不为「失败」抛异常。
///     <para>
///         <see cref="Offset" /> 的语义是「<b>服务器 UTC − 本机 UTC</b>」：正数表示本机慢了，
///         界面时钟要往后拨 <see cref="Offset" /> 才对得上。反过来定义会让调用方每处都得记得取负。
///     </para>
///     <para>
///         <see cref="RoundTrip" /> 是这次请求的往返时延（T4 − T1 再扣掉服务器处理时间），
///         主要给界面显示「延迟 23 ms」，也是判断这次结果可不可信的依据。
///     </para>
/// </summary>
/// <param name="Succeeded">这次校时成不成功；失败时其它数值字段为零。</param>
/// <param name="Server">请求用的服务器名（原样回填，方便在界面上说清是哪台失败了）。</param>
/// <param name="Offset">服务器 UTC 减本机 UTC，正数表示本机慢。</param>
/// <param name="RoundTrip">往返时延。</param>
/// <param name="SyncedAtUtc">这次结果对应的本机时刻（成功时是收到回应的那一刻）。</param>
/// <param name="Stratum">服务器报的层级：1 是原子钟，数字越大离基准越远。</param>
/// <param name="Error">失败原因，成功时为 <c>null</c>。</param>
public sealed record NtpSyncResult(
    bool Succeeded,
    string Server,
    TimeSpan Offset,
    TimeSpan RoundTrip,
    DateTimeOffset SyncedAtUtc,
    int Stratum,
    string? Error)
{
    /// <summary>成功：偏差、时延、收到回应的本机时刻与服务器层级。</summary>
    public static NtpSyncResult Success(string server, TimeSpan offset, TimeSpan roundTrip,
        DateTimeOffset syncedAtUtc, int stratum)
        => new(true, server, offset, roundTrip, syncedAtUtc, stratum, null);

    /// <summary>
    ///     失败：数值字段一律为零，时间戳取「现在」，方便调用方统一按同一种形状处理
    ///     （不用先判 <see cref="Succeeded" /> 才敢读字段）。
    /// </summary>
    public static NtpSyncResult Failure(string server, string error)
        => new(false, server, TimeSpan.Zero, TimeSpan.Zero, DateTimeOffset.UtcNow, 0, error);
}
