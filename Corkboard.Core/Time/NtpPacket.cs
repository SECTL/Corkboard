using System.Buffers.Binary;

namespace Corkboard.Core.Time;

/// <summary>
///     SNTP/NTP v4 报文（48 字节）的构造与解析，以及由四个时间戳算偏差的公式。
///     <para>
///         这里<b>不碰网络</b>：造字节、解字节、算数，全是纯逻辑，所以能直接对着合成报文单测。
///         发 UDP 的事在 <see cref="NtpClient" /> 里，两边分开的好处是「报文错在哪」不用靠抓包看。
///     </para>
///     <para>
///         四个时间戳的约定（RFC 5905 的叫法）：
///         <list type="bullet">
///             <item>T1 = 本机发出请求的时刻（报文里没有，调用方自己记）；</item>
///             <item>T2 = 服务器收到请求的时刻，来自 <see cref="ReceiveTimestamp" />；</item>
///             <item>T3 = 服务器发出回应的时刻，来自 <see cref="TransmitTimestamp" />；</item>
///             <item>T4 = 本机收到回应的时刻（同样由调用方记）。</item>
///         </list>
///     </para>
/// </summary>
public sealed class NtpPacket
{
    /// <summary>SNTP/NTP 报文固定长度：头 48 字节，一个字节都不能少。</summary>
    public const int PacketSize = 48;

    /// <summary>客户端模式（Mode 3）：本机发给服务器的请求。</summary>
    public const int ModeClient = 3;

    /// <summary>服务器模式（Mode 4）：服务器对请求的回应。</summary>
    public const int ModeServer = 4;

    /// <summary>广播模式（Mode 5）：服务器主动推送的时间。</summary>
    public const int ModeBroadcast = 5;

    /// <summary>本实现只发 v4 报文。</summary>
    public const int Version = 4;

    /// <summary>闰秒指示（2 位）：3 表示服务器自己也不知道时间，遇到就别信它的偏差。</summary>
    public int LeapIndicator { get; init; }

    /// <summary>协议版本（3 位）。</summary>
    public int VersionNumber { get; init; }

    /// <summary>模式（3 位）：3 客户端、4 服务器、5 广播。</summary>
    public int Mode { get; init; }

    /// <summary>层级：0 是 Kiss-o'-Death / 无效，1 是原子钟，越大离基准越远。</summary>
    public int Stratum { get; init; }

    /// <summary>轮询间隔（2 的幂次秒）。客户端用不到，保留原始值。</summary>
    public sbyte PollInterval { get; init; }

    /// <summary>时钟精度（2 的幂次秒）。客户端用不到，保留原始值。</summary>
    public sbyte Precision { get; init; }

    /// <summary>到基准的总时延（报文里是 16.16 定点数，这里已换算成时间）。</summary>
    public TimeSpan RootDelay { get; init; }

    /// <summary>到基准的总离散度（报文里是 16.16 定点数，这里已换算成时间）。</summary>
    public TimeSpan RootDispersion { get; init; }

    /// <summary>参考源标识（原始 4 字节，含义随 stratum 变：上层地址 / 时钟代号）。</summary>
    public uint ReferenceId { get; init; }

    /// <summary>服务器上次校准自己的时刻。</summary>
    public DateTimeOffset ReferenceTimestamp { get; init; }

    /// <summary>T1 的回显：本机发请求时写进去的时间戳，服务器原样抄回来，用来配对请求与回应。</summary>
    public DateTimeOffset OriginTimestamp { get; init; }

    /// <summary>T2：服务器收到请求的时刻。</summary>
    public DateTimeOffset ReceiveTimestamp { get; init; }

    /// <summary>T3：服务器发出回应的时刻。</summary>
    public DateTimeOffset TransmitTimestamp { get; init; }

    /// <summary>
    ///     造一个客户端请求（48 字节）：首字节 LI=0 / VN=4 / Mode=3，其余全零，
    ///     最后 8 字节写发出时刻。
    ///     <para>
    ///         其余字段留零是故意的：客户端不填 stratum、精度那些，服务器也不看；
    ///         <see cref="OriginTimestamp" /> 留零则由服务器在回应里回填（RFC 5905 允许这样配对）。
    ///     </para>
    /// </summary>
    public static byte[] CreateRequest(DateTimeOffset transmitUtc)
    {
        var buffer = new byte[PacketSize];
        buffer[0] = (byte)((0 << 6) | (Version << 3) | ModeClient);
        WriteTimestamp(buffer, 40, transmitUtc);
        return buffer;
    }

    /// <summary>
    ///     解析一个回应报文。长度不足 48、或 Mode 不是 4/5 时返回 <c>false</c>——
    ///     UDP 端口上什么都可能收到（自己的请求回显、扫描包、全零的垃圾），
    ///     解析失败只能算「这不是我要的包」，<b>不能抛异常</b>把整次校时带崩。
    /// </summary>
    public static bool TryParse(byte[] buffer, out NtpPacket? packet)
    {
        packet = null;
        if (buffer is null || buffer.Length < PacketSize) return false;

        var mode = buffer[0] & 0b111;
        if (mode != ModeServer && mode != ModeBroadcast) return false;

        // 四个时间戳用同一个「现在」解释回卷，免得跨 2036 那条线时同一份报文里出现两个周期的值。
        var now = DateTimeOffset.UtcNow;

        packet = new NtpPacket
        {
            LeapIndicator = (buffer[0] >> 6) & 0b11,
            VersionNumber = (buffer[0] >> 3) & 0b111,
            Mode = mode,
            Stratum = buffer[1],
            PollInterval = (sbyte)buffer[2],
            Precision = (sbyte)buffer[3],
            RootDelay = ToTimeSpan(BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(4))),
            RootDispersion = ToTimeSpan(BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(8))),
            ReferenceId = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(12)),
            ReferenceTimestamp = ReadTimestamp(buffer, 16, now),
            OriginTimestamp = ReadTimestamp(buffer, 24, now),
            ReceiveTimestamp = ReadTimestamp(buffer, 32, now),
            TransmitTimestamp = ReadTimestamp(buffer, 40, now)
        };
        return true;
    }

    /// <summary>
    ///     由四个时间戳算偏差与往返时延：
    ///     <c>offset = ((T2 − T1) + (T3 − T4)) / 2</c>、<c>roundTrip = (T4 − T1) − (T3 − T2)</c>。
    ///     <para>
    ///         两条「别信」的判据：往返时延为负（本机时钟在请求途中被改过、或报文对不上），
    ///         以及偏差超过一天（那基本是把 1900 年当成了现在，或者是台坏掉的服务器）——
    ///         这种结果拿去做时钟会直接把看板拨到离谱的时间，宁可当失败。
    ///     </para>
    /// </summary>
    public static bool TryComputeOffset(NtpPacket response, DateTimeOffset requestSentUtc,
        DateTimeOffset responseReceivedUtc, out TimeSpan offset, out TimeSpan roundTrip)
    {
        offset = TimeSpan.Zero;
        roundTrip = TimeSpan.Zero;
        if (response is null) return false;

        var t2 = response.ReceiveTimestamp;
        var t3 = response.TransmitTimestamp;

        // 按 tick 一次算完再折成 TimeSpan：TimeSpan 相减本身没问题，但 /2 会丢 1 tick，先合并能少丢一次。
        var offsetTicks = ((t2 - requestSentUtc).Ticks + (t3 - responseReceivedUtc).Ticks) / 2;
        var roundTripTicks = (responseReceivedUtc - requestSentUtc).Ticks - (t3 - t2).Ticks;

        if (roundTripTicks < 0) return false;
        if (Math.Abs(offsetTicks) > TimeSpan.TicksPerDay) return false;

        offset = TimeSpan.FromTicks(offsetTicks);
        roundTrip = TimeSpan.FromTicks(roundTripTicks);
        return true;
    }

    /// <summary>把 16.16 定点数（有符号，时延可能是负的）折成时间。</summary>
    private static TimeSpan ToTimeSpan(uint raw)
    {
        var ticks = (long)(int)raw * TimeSpan.TicksPerSecond / 65536;
        return TimeSpan.FromTicks(ticks);
    }

    /// <summary>读一个 8 字节时间戳（秒 + 小数），按最近周期解释。</summary>
    private static DateTimeOffset ReadTimestamp(byte[] buffer, int offset, DateTimeOffset now)
        => NtpTimestamp.ToDateTimeOffset(
            BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset)),
            BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset + 4)),
            now);

    /// <summary>写一个 8 字节时间戳（秒 + 小数），网络字节序。</summary>
    private static void WriteTimestamp(byte[] buffer, int offset, DateTimeOffset value)
    {
        var (seconds, fraction) = NtpTimestamp.FromDateTimeOffset(value);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), seconds);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset + 4), fraction);
    }
}
