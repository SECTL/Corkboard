using System.Buffers.Binary;
using Corkboard.Core.Time;

namespace Corkboard.Core.Tests;

/// <summary>
///     NTP 报文的构造与解析：48 字节的头、LI/VN/Mode 三个位段、以及四个时间戳的读写。
///     用例里全部用合成报文，**不发一个包**——报文层本来就是纯逻辑，联网的验证留给真机。
/// </summary>
public class NtpPacketTests
{
    private static readonly DateTimeOffset Base = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateRequest_Is48BytesWithClientHeader()
    {
        var request = NtpPacket.CreateRequest(Base);

        Assert.Equal(48, request.Length);

        // LI=0、VN=4、Mode=3 拼成首字节：0b000_100_11 = 0x23。
        Assert.Equal(0x23, (int)request[0]);
        Assert.Equal(0, (request[0] >> 6) & 0b11);
        Assert.Equal(4, (request[0] >> 3) & 0b111);
        Assert.Equal(3, request[0] & 0b111);

        // 除首字节与末尾 8 字节的时间戳，其余字段必须留零（客户端不填 stratum/精度）。
        for (var i = 1; i < 40; i++) Assert.Equal(0, (int)request[i]);

        var seconds = BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(40));
        var fraction = BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(44));
        Assert.Equal(Base, NtpTimestamp.ToDateTimeOffset(seconds, fraction, Base));
    }

    [Fact]
    public void TryParse_RejectsTooShortBuffer()
    {
        // 47 字节：头都不完整，不能当成报文。
        Assert.False(NtpPacket.TryParse(new byte[47], out var packet));
        Assert.Null(packet);
    }

    [Fact]
    public void TryParse_RejectsClientMode()
    {
        // Mode=3 是本机自己的请求（端口上可能收到回显），不是服务器回应。
        var buffer = new byte[48];
        buffer[0] = (byte)((0 << 6) | (4 << 3) | 3);

        Assert.False(NtpPacket.TryParse(buffer, out var packet));
        Assert.Null(packet);
    }

    [Fact]
    public void TryParse_RejectsAllZeroBuffer()
    {
        // 全零的 Mode=0，UDP 上确实会收到这种垃圾包。
        Assert.False(NtpPacket.TryParse(new byte[48], out var packet));
        Assert.Null(packet);
    }

    [Fact]
    public void TryParse_ReadsServerResponseFields()
    {
        var reference = Base.AddMinutes(-1);
        var origin = Base.AddSeconds(-2);
        var receive = Base.AddSeconds(-1);
        var transmit = Base;
        var buffer = BuildResponse(NtpPacket.ModeServer, stratum: 2, reference, origin, receive, transmit,
            referenceId: 0x0A000001);

        Assert.True(NtpPacket.TryParse(buffer, out var packet));
        Assert.NotNull(packet);

        Assert.Equal(4, packet.VersionNumber);
        Assert.Equal(NtpPacket.ModeServer, packet.Mode);
        Assert.Equal(2, packet.Stratum);
        Assert.Equal(0x0A000001u, packet.ReferenceId);

        // 时间戳只保留到 tick，回读应当精确相等（小数部分向下取整，往返不漂）。
        Assert.Equal(reference, packet.ReferenceTimestamp);
        Assert.Equal(origin, packet.OriginTimestamp);
        Assert.Equal(receive, packet.ReceiveTimestamp);
        Assert.Equal(transmit, packet.TransmitTimestamp);
    }

    [Fact]
    public void TryParse_AcceptsBroadcastMode()
    {
        var buffer = BuildResponse(NtpPacket.ModeBroadcast, 1, Base, Base, Base, Base);

        Assert.True(NtpPacket.TryParse(buffer, out var packet));
        Assert.Equal(NtpPacket.ModeBroadcast, packet!.Mode);
    }

    /// <summary>按 RFC 5905 的布局拼一份回应报文（只填用例关心的字段）。</summary>
    private static byte[] BuildResponse(int mode, int stratum, DateTimeOffset reference, DateTimeOffset origin,
        DateTimeOffset receive, DateTimeOffset transmit, uint referenceId = 0)
    {
        var buffer = new byte[48];
        buffer[0] = (byte)((0 << 6) | (4 << 3) | mode);
        buffer[1] = (byte)stratum;
        buffer[2] = 4; // poll
        buffer[3] = unchecked((byte)(sbyte)-20); // precision
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(12), referenceId);
        WriteTimestamp(buffer, 16, reference);
        WriteTimestamp(buffer, 24, origin);
        WriteTimestamp(buffer, 32, receive);
        WriteTimestamp(buffer, 40, transmit);
        return buffer;
    }

    private static void WriteTimestamp(byte[] buffer, int offset, DateTimeOffset value)
    {
        var (seconds, fraction) = NtpTimestamp.FromDateTimeOffset(value);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), seconds);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset + 4), fraction);
    }
}

/// <summary>
///     偏差公式 <c>offset = ((T2 − T1) + (T3 − T4)) / 2</c>、<c>roundTrip = (T4 − T1) − (T3 − T2)</c>，
///     以及两条「别信这次结果」的判据。
/// </summary>
public class NtpOffsetTests
{
    private static readonly DateTimeOffset Base = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryComputeOffset_SymmetricLatencyYieldsZeroOffset()
    {
        // 去程与回程各 500ms、服务器处理 0ms：T1=0 T2=500 T3=500 T4=1000。
        var response = new NtpPacket
        {
            Mode = NtpPacket.ModeServer,
            ReceiveTimestamp = Base.AddMilliseconds(500),
            TransmitTimestamp = Base.AddMilliseconds(500)
        };

        Assert.True(NtpPacket.TryComputeOffset(response, Base, Base.AddMilliseconds(1000),
            out var offset, out var roundTrip));

        Assert.Equal(TimeSpan.Zero, offset);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), roundTrip);
    }

    [Fact]
    public void TryComputeOffset_ServerAheadByFiveSeconds()
    {
        // 本机慢了 5 秒：服务器那两个时间戳都比本机的同一时刻多 5s，时延仍是 1000ms。
        var response = new NtpPacket
        {
            Mode = NtpPacket.ModeServer,
            ReceiveTimestamp = Base.AddMilliseconds(5500),
            TransmitTimestamp = Base.AddMilliseconds(5500)
        };

        Assert.True(NtpPacket.TryComputeOffset(response, Base, Base.AddMilliseconds(1000),
            out var offset, out var roundTrip));

        // 正数 = 服务器比本机快 = 本机慢了，界面要把时钟往前拨。
        Assert.Equal(TimeSpan.FromSeconds(5), offset);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), roundTrip);
    }

    [Fact]
    public void TryComputeOffset_AccountsForServerProcessingTime()
    {
        // 服务器自己耗了 200ms（T3 − T2），这 200ms 不能算进往返时延里。
        var response = new NtpPacket
        {
            Mode = NtpPacket.ModeServer,
            ReceiveTimestamp = Base.AddMilliseconds(500),
            TransmitTimestamp = Base.AddMilliseconds(700)
        };

        Assert.True(NtpPacket.TryComputeOffset(response, Base, Base.AddMilliseconds(1200),
            out var offset, out var roundTrip));

        Assert.Equal(TimeSpan.Zero, offset);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), roundTrip);
    }

    [Fact]
    public void TryComputeOffset_RejectsNegativeRoundTrip()
    {
        // 服务器「处理」的时间比整趟往返还长：报文对不上（或者本机时钟在途中被改过）。
        var response = new NtpPacket
        {
            Mode = NtpPacket.ModeServer,
            ReceiveTimestamp = Base.AddMilliseconds(500),
            TransmitTimestamp = Base.AddMilliseconds(700)
        };

        Assert.False(NtpPacket.TryComputeOffset(response, Base, Base.AddMilliseconds(100),
            out var offset, out var roundTrip));
        Assert.Equal(TimeSpan.Zero, offset);
        Assert.Equal(TimeSpan.Zero, roundTrip);
    }

    [Fact]
    public void TryComputeOffset_RejectsOffsetBeyondOneDay()
    {
        // 偏差 3 天：这种结果拿去做时钟会把看板拨到离谱的时间，宁可算失败。
        var response = new NtpPacket
        {
            Mode = NtpPacket.ModeServer,
            ReceiveTimestamp = Base.AddDays(3),
            TransmitTimestamp = Base.AddDays(3)
        };

        Assert.False(NtpPacket.TryComputeOffset(response, Base, Base.AddSeconds(1),
            out _, out _));
    }
}

/// <summary>
///     NTP 时间戳与 <see cref="DateTimeOffset" /> 的互换，重点是 2036 年的 era 回卷。
///     回卷判定依赖「现在」，所以用例一律把 <c>now</c> 显式传进去，不读真实时钟。
/// </summary>
public class NtpTimestampTests
{
    [Theory]
    [InlineData(2026, 10, 18, 12, 0, 0)]
    [InlineData(1970, 1, 1, 0, 0, 0)]
    [InlineData(2036, 2, 7, 6, 28, 15)]
    [InlineData(2035, 12, 31, 23, 59, 59)]
    public void RoundTrip_PreservesValue(int year, int month, int day, int hour, int minute, int second)
    {
        var value = new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero);

        var (seconds, fraction) = NtpTimestamp.FromDateTimeOffset(value);

        Assert.Equal(value, NtpTimestamp.ToDateTimeOffset(seconds, fraction, value));
    }

    [Fact]
    public void RoundTrip_PreservesSubSecondTicks()
    {
        // 小数部分有 32 位（≈233 皮秒），比 tick 细，所以往返必须精确到 tick。
        var value = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero).AddTicks(1_234_567);

        var (seconds, fraction) = NtpTimestamp.FromDateTimeOffset(value);

        Assert.Equal(value, NtpTimestamp.ToDateTimeOffset(seconds, fraction, value));
    }

    [Fact]
    public void EpochIsNtpEraZero()
    {
        Assert.Equal(new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero), NtpTimestamp.Epoch);
        Assert.Equal((0u, 0u), NtpTimestamp.FromDateTimeOffset(NtpTimestamp.Epoch));
    }

    [Fact]
    public void ToDateTimeOffset_ResolvesRolloverToNearestEra()
    {
        // 判定依据：秒数只有 32 位，2036-02-07T06:28:16Z 之后回卷成 0。
        // 从 2026 年看，秒数 0 只能是「下一个周期」的起点（2036），不可能是 1900。
        var now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        var eraOneStart = new DateTimeOffset(2036, 2, 7, 6, 28, 16, TimeSpan.Zero);

        Assert.Equal(eraOneStart, NtpTimestamp.ToDateTimeOffset(0, 0, now));
        Assert.Equal(eraOneStart.AddSeconds(100), NtpTimestamp.ToDateTimeOffset(100, 0, now));
        Assert.Equal(eraOneStart.AddSeconds(3600), NtpTimestamp.ToDateTimeOffset(3600, 0, now));
    }

    [Fact]
    public void ToDateTimeOffset_LeavesCurrentEraUntouched()
    {
        // 当前周期的值（2026 的秒数还没回卷）不能被平白加一个 136 年。
        var now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        var value = new DateTimeOffset(2026, 10, 18, 11, 0, 0, TimeSpan.Zero);
        var (seconds, _) = NtpTimestamp.FromDateTimeOffset(value);

        Assert.Equal(value, NtpTimestamp.ToDateTimeOffset(seconds, 0, now));
    }

    [Fact]
    public void ToDateTimeOffset_KeepsEraOneValuesAfterRollover()
    {
        // 2036 年之后的客户端收到秒数 0 时不该再加一个周期（否则会跳到 2172 年）。
        var now = new DateTimeOffset(2036, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var eraOneStart = new DateTimeOffset(2036, 2, 7, 6, 28, 16, TimeSpan.Zero);

        Assert.Equal(eraOneStart, NtpTimestamp.ToDateTimeOffset(0, 0, now));
    }

    [Fact]
    public void FromDateTimeOffset_ClampsPreEpochToZero()
    {
        // NTP 表示不了 1900 以前的时间，退回 (0, 0) 而不是抛异常或溢出。
        Assert.Equal((0u, 0u), NtpTimestamp.FromDateTimeOffset(new DateTimeOffset(1899, 12, 31, 0, 0, 0, TimeSpan.Zero)));
    }
}

/// <summary>
///     默认源清单与输入清洗。默认源全是国内公共 NTP 服务，空表必须回退到它——
///     否则用户把输入框清空一次，看板就永远校不上时。
/// </summary>
public class NtpServerCatalogTests
{
    [Fact]
    public void Defaults_AreDomesticPublicServersInOrder()
    {
        Assert.Equal(8, NtpServerCatalog.Defaults.Count);
        Assert.Equal(
            new[]
            {
                "ntp.aliyun.com",
                "ntp1.aliyun.com",
                "time1.cloud.tencent.com",
                "time2.cloud.tencent.com",
                "cn.pool.ntp.org",
                "cn.ntp.org.cn",
                "ntp.sjtu.edu.cn",
                "time.edu.cn"
            },
            NtpServerCatalog.Defaults);
    }

    [Fact]
    public void Normalize_FallsBackToDefaultsWhenEmpty()
    {
        var inputs = new IEnumerable<string?>?[]
        {
            null,
            Array.Empty<string?>(),
            new string?[] { "", "   ", null }
        };

        foreach (var input in inputs)
        {
            var result = NtpServerCatalog.Normalize(input);
            Assert.Equal(NtpServerCatalog.Defaults, result);
        }
    }

    [Fact]
    public void Normalize_FallbackIsACopyNotTheStaticList()
    {
        // 调用方会就地改这个 List（把失败源挪到末尾），不能改到静态清单上。
        var result = NtpServerCatalog.Normalize(Array.Empty<string?>());
        result.Clear();

        Assert.Equal(8, NtpServerCatalog.Defaults.Count);
    }

    [Fact]
    public void Normalize_TrimsAndDropsBlanks()
    {
        var result = NtpServerCatalog.Normalize(new[] { "  ntp.aliyun.com  ", "", "   ", "time.edu.cn\t" });

        Assert.Equal(new[] { "ntp.aliyun.com", "time.edu.cn" }, result);
    }

    [Fact]
    public void Normalize_DeduplicatesCaseInsensitively()
    {
        var result = NtpServerCatalog.Normalize(new[] { "ntp.aliyun.com", "NTP.Aliyun.com", "ntp.aliyun.com " });

        Assert.Equal(new[] { "ntp.aliyun.com" }, result);
    }

    [Fact]
    public void Normalize_KeepsAtMostMaxServers()
    {
        var input = Enumerable.Range(1, 12).Select(i => $"ntp{i}.example.com").ToArray();

        var result = NtpServerCatalog.Normalize(input);

        Assert.Equal(NtpServerCatalog.MaxServers, result.Count);
        Assert.Equal("ntp1.example.com", result[0]);
        Assert.Equal("ntp8.example.com", result[^1]);
    }

    [Theory]
    [InlineData("ntp.aliyun.com", true)]
    [InlineData("cn.pool.ntp.org", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("time1.cloud.tencent.com:123", true)]
    [InlineData("[::1]", true)]
    [InlineData("[2400:3200::1]", true)]
    [InlineData("::1", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("ntp aliyun com", false)]
    [InlineData("https://ntp.aliyun.com", false)]
    [InlineData("ntp.aliyun.com/", false)]
    [InlineData(".aliyun.com", false)]
    [InlineData("-aliyun.com", false)]
    [InlineData("aliyun.com.", false)]
    [InlineData("aliyun.com-", false)]
    [InlineData("ntp@aliyun.com", false)]
    public void IsValidHost_ValidatesShape(string? value, bool expected)
        => Assert.Equal(expected, NtpServerCatalog.IsValidHost(value));

    [Fact]
    public void IsValidHost_RejectsOverlyLongName()
    {
        // 253 是 DNS 名字的上限，254 个字符肯定不是合法主机名。
        Assert.True(NtpServerCatalog.IsValidHost(new string('a', 253)));
        Assert.False(NtpServerCatalog.IsValidHost(new string('a', 254)));
    }
}
