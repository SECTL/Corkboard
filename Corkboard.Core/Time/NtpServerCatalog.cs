namespace Corkboard.Core.Time;

/// <summary>
///     默认 NTP 源清单与输入的清洗规则（纯逻辑，不碰网络也不碰配置）。
///     <para>
///         默认源全部是<b>国内的公共 NTP 服务</b>：考试看板要「开箱就能校时」，
///         国内源既不用穿墙、延迟也低（跨境源一次往返动辄几百毫秒，偏差算出来更不准）。
///         顺序按「阿里 → 腾讯 → 公共池 → 教育网」排，前面挂了自动往下试。
///     </para>
///     <para>
///         清洗规则单独放在这里而不是塞进设置页：设置页要校验用户输入、同步服务要读配置，
///         两边对「什么算合法的一行」必须完全一致，所以只有这一份实现。
///     </para>
/// </summary>
public static class NtpServerCatalog
{
    /// <summary>最多同时保留几个源：再多也没有收益，反而让「全部失败」的等待时间成倍变长。</summary>
    public const int MaxServers = 8;

    /// <summary>主机名（DNS 名）的最大长度，RFC 1035 规定的上限。</summary>
    private const int MaxHostLength = 253;

    /// <summary>阿里云公共 NTP（国内多线，延迟最低的一档）。</summary>
    private const string Aliyun = "ntp.aliyun.com";

    /// <summary>阿里云公共 NTP 的第二台，第一台解析失败或超时时接着试。</summary>
    private const string Aliyun1 = "ntp1.aliyun.com";

    /// <summary>腾讯云公共 NTP。</summary>
    private const string Tencent1 = "time1.cloud.tencent.com";

    private const string Tencent2 = "time2.cloud.tencent.com";

    /// <summary>NTP Pool 的中国区（由多台志愿者服务器轮转，单台挂了不影响）。</summary>
    private const string ChinaPool = "cn.pool.ntp.org";

    /// <summary>中国国家授时中心体系下的公共源。</summary>
    private const string ChinaNtp = "cn.ntp.org.cn";

    /// <summary>上海交大的教育网源，校园网里通常很快。</summary>
    private const string Sjtu = "ntp.sjtu.edu.cn";

    /// <summary>教育网公共源，作为最后的兜底。</summary>
    private const string EduCn = "time.edu.cn";

    /// <summary>默认源：国内公共 NTP 服务，按优先级排。</summary>
    public static IReadOnlyList<string> Defaults { get; } =
    [
        Aliyun,
        Aliyun1,
        Tencent1,
        Tencent2,
        ChinaPool,
        ChinaNtp,
        Sjtu,
        EduCn
    ];

    /// <summary>
    ///     把配置里读出来的一串源收拾干净：去首尾空白、丢掉空项、大小写不敏感去重、最多 <see cref="MaxServers" /> 个。
    ///     <para>
    ///         <b>结果为空时返回一份 <see cref="Defaults" /> 的副本</b>，而不是空表：空表意味着
    ///         「一个源都不用试」，用户只要把输入框清空一次，看板就永远校不上时，而且界面上看不出原因。
    ///         返回副本是因为调用方会就地改这个 List（比如把失败源往后挪），不能让它改到静态清单。
    ///     </para>
    /// </summary>
    public static List<string> Normalize(IEnumerable<string?>? servers)
    {
        var result = new List<string>();
        if (servers is null) return [.. Defaults];

        // OrdinalIgnoreCase：主机名大小写不敏感，`NTP.Aliyun.com` 与 `ntp.aliyun.com` 是同一台。
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in servers)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;

            var trimmed = candidate.Trim();
            if (trimmed.Length == 0) continue;
            if (!seen.Add(trimmed)) continue;

            result.Add(trimmed);
            if (result.Count >= MaxServers) break;
        }

        return result.Count == 0 ? [.. Defaults] : result;
    }

    /// <summary>
    ///     设置页用的单个源校验：判断这一行像不像一个主机名。
    ///     <para>
    ///         故意只做「形状」校验，不查 DNS——设置页在用户敲字时不该发网络请求，
    ///         真解析不了的情况由校时失败文案去说。
    ///         允许 IPv6 写成方括号形式（<c>[::1]</c>），因为那是 URI 里的通行写法，用户会照着填。
    ///     </para>
    /// </summary>
    public static bool IsValidHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Length > MaxHostLength) return false;

        // 空格与斜杠一出现就肯定不是主机名（斜杠多半是有人把 `https://` 整个粘进来了）。
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) || c == '/') return false;
        }

        var bracketed = value[0] == '[' && value[^1] == ']' && value.Length > 2;
        var core = bracketed ? value[1..^1] : value;
        if (core.Length == 0) return false;

        foreach (var c in core)
        {
            // 方括号形式里只允许 IPv6 会出现的字符；普通形式按主机名放宽到字母数字与 - . :
            if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '.' && c != ':') return false;
        }

        // 首尾的 `.` 或 `-` 是明确的非法（`-foo.com` 这种连 DNS 都不认），中间的不深究。
        var first = core[0];
        var last = core[^1];
        if (first is '.' or '-') return false;
        if (last is '.' or '-') return false;

        return true;
    }
}
