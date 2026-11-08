namespace Corkboard.Core.Holidays;

/// <summary>一条可尝试的节假日数据源地址。</summary>
public sealed class HolidaySourceEndpoint
{
    /// <summary>数据源名（holiday-cn / chinese-days），解析器会写进 <see cref="HolidayYearData.SourceName" />。</summary>
    public string SourceName { get; init; } = HolidaySourceCatalog.HolidayCnName;

    /// <summary>数据源仓库地址，界面上「来源」要能点开。</summary>
    public string SourceUrl { get; init; } = HolidaySourceCatalog.HolidayCnRepoUrl;

    public string License { get; init; } = "MIT";

    /// <summary>这一年的 JSON 地址。</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>是不是镜像（主源的第一档是 SECTL 反代，失败才直连）。</summary>
    public bool IsMirror { get; init; }

    /// <summary>是不是备用源（主源全挂才用）。</summary>
    public bool IsBackup { get; init; }
}

/// <summary>
///     节假日数据源目录：只拼 URL、只描述来源，不联网。
///     <para>
///         主源 <c>NateScarlet/holiday-cn</c>（MIT © 2019 NateScarlet，2007–2027）；
///         大陆直连 raw.githubusercontent 不稳，所以第一档走 <c>ghproxy.sectl.cn</c> 反代，再直连、再 jsDelivr。
///         备源 <c>vsme/chinese-days</c>（MIT © 2024 Yawei sun）只在主源全部失败时兜底。
///     </para>
///     <para>
///         地址与优先级来自实测笔记 <c>.tmp/duty-research/holiday-source.md</c>，改之前先看那份笔记。
///     </para>
/// </summary>
public static class HolidaySourceCatalog
{
    public const string HolidayCnName = "holiday-cn";

    public const string ChineseDaysName = "chinese-days";

    /// <summary>随包内置数据的来源名（实际内容仍是 holiday-cn 的数据，只是不联网）。</summary>
    public const string BuiltinName = "built-in";

    public const string HolidayCnRepoUrl = "https://github.com/NateScarlet/holiday-cn";

    public const string ChineseDaysRepoUrl = "https://github.com/vsme/chinese-days";

    public const string HolidayCnLicense = "MIT";

    public const string ChineseDaysLicense = "MIT";

    /// <summary>国务院公告的兜底入口（数据里没有 <c>papers</c> 时界面上给这个）。</summary>
    public const string GovernmentNoticeUrl = "https://www.gov.cn/zhengce/";

    // 主源的三档地址。{0} 是四位年份。
    private const string HolidayCnMirrorPattern =
        "https://ghproxy.sectl.cn/https://raw.githubusercontent.com/NateScarlet/holiday-cn/master/{0}.json";

    private const string HolidayCnRawPattern =
        "https://raw.githubusercontent.com/NateScarlet/holiday-cn/master/{0}.json";

    private const string HolidayCnJsDelivrPattern =
        "https://fastly.jsdelivr.net/gh/NateScarlet/holiday-cn@master/{0}.json";

    // 备源：chinese-days 按年发布的 dist 文件。
    private const string ChineseDaysPattern =
        "https://fastly.jsdelivr.net/npm/chinese-days/dist/years/{0}.json";

    public static string HolidayCnMirrorUrl(int year) => string.Format(HolidayCnMirrorPattern, year);

    public static string HolidayCnRawUrl(int year) => string.Format(HolidayCnRawPattern, year);

    public static string HolidayCnJsDelivrUrl(int year) => string.Format(HolidayCnJsDelivrPattern, year);

    public static string ChineseDaysUrl(int year) => string.Format(ChineseDaysPattern, year);

    /// <summary>主源的地址链（按尝试顺序）。</summary>
    public static IReadOnlyList<HolidaySourceEndpoint> HolidayCnEndpoints(int year) =>
    [
        Create(HolidayCnName, HolidayCnRepoUrl, HolidayCnLicense, HolidayCnMirrorUrl(year), isMirror: true),
        Create(HolidayCnName, HolidayCnRepoUrl, HolidayCnLicense, HolidayCnRawUrl(year)),
        Create(HolidayCnName, HolidayCnRepoUrl, HolidayCnLicense, HolidayCnJsDelivrUrl(year))
    ];

    /// <summary>备源地址（只有一档）。</summary>
    public static IReadOnlyList<HolidaySourceEndpoint> ChineseDaysEndpoints(int year) =>
    [
        Create(ChineseDaysName, ChineseDaysRepoUrl, ChineseDaysLicense, ChineseDaysUrl(year), isBackup: true)
    ];

    /// <summary>某一年的全部候选地址：主源三档 + 备源一档，按尝试顺序。</summary>
    public static IReadOnlyList<HolidaySourceEndpoint> Candidates(int year)
    {
        var candidates = new List<HolidaySourceEndpoint>(4);
        candidates.AddRange(HolidayCnEndpoints(year));
        candidates.AddRange(ChineseDaysEndpoints(year));
        return candidates;
    }

    /// <summary>随包内置数据的来源标注（内置文件与在线源同格式，署名仍算 holiday-cn）。</summary>
    public static HolidayAttribution BuiltinAttribution(
        int year,
        IReadOnlyList<HolidayDay>? days = null,
        DateTimeOffset? fetchedAt = null)
    {
        var list = days ?? [];
        return new HolidayAttribution
        {
            SourceName = BuiltinName,
            SourceUrl = HolidayCnRepoUrl,
            License = HolidayCnLicense,
            Year = year,
            DayCount = list.Count,
            OffDayCount = list.Count(day => day.IsOffDay),
            FetchedAt = fetchedAt ?? default,
            Papers = []
        };
    }

    private static HolidaySourceEndpoint Create(
        string sourceName,
        string sourceUrl,
        string license,
        string url,
        bool isMirror = false,
        bool isBackup = false) => new()
    {
        SourceName = sourceName,
        SourceUrl = sourceUrl,
        License = license,
        Url = url,
        IsMirror = isMirror,
        IsBackup = isBackup
    };
}
