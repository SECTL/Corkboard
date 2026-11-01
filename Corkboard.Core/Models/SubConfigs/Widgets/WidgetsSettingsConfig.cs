using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Widgets.Quote;

namespace Corkboard.Core.Models.SubConfigs.Widgets;

/// <summary>
///     「一言」与「天气」两行字的配置（<c>settings.json</c> 的 <c>widgets</c> 段）。
///     <para>
///         它们只出现在<b>看板模式</b>那块全屏上（作业板与考试看板都不放），所以界面上
///         这两组卡片挂在「设置 → 看板模式」里，但配置单独占一段：数据来源与那块屏的
///         版式、进出时段没关系，混在一个类里只会让两件事看起来有关。
///     </para>
///     <para>
///         两个字都有开关、都默认开；关掉之后整行不画，也不会去请求接口。
///     </para>
/// </summary>
public partial class WidgetsSettingsConfig : ObservableObject
{
    /// <summary>一言刷新间隔的下限（分钟）：接口国内节点 QPS 2，别把屏幕当成压测工具。</summary>
    public const int MinRefreshMinutes = 5;

    /// <summary>刷新间隔的上限（分钟）：一天一换。</summary>
    public const int MaxRefreshMinutes = 1440;

    /// <summary>一言的默认刷新间隔（分钟）。</summary>
    public const int DefaultQuoteRefreshMinutes = 30;

    /// <summary>天气的默认刷新间隔（分钟）：气象站自己也就十来分钟一更。</summary>
    public const int DefaultWeatherRefreshMinutes = 30;

    /// <summary>默认城市名（用户没选过城市时用它，也用它去搜经纬度）。</summary>
    public const string DefaultCityName = "北京";

    /// <summary>默认城市纬度（北京）。</summary>
    public const double DefaultLatitude = 39.9042;

    /// <summary>默认城市经度（北京）。</summary>
    public const double DefaultLongitude = 116.4074;

    // ── 一言 ────────────────────────────────────────────────

    /// <summary>在看板模式里显示一言。默认开。</summary>
    [ObservableProperty] private bool _quoteEnabled = true;

    /// <summary>要哪些类别（服务端 <c>c</c> 参数的位集合）。默认全都要。</summary>
    [ObservableProperty] private QuoteCategory _quoteCategories = QuoteCategory.All;

    /// <summary>最短字数（0 = 不限）。</summary>
    [ObservableProperty] private int _quoteMinLength;

    /// <summary>最长字数（默认 30，与服务端默认一致）。</summary>
    [ObservableProperty] private int _quoteMaxLength = HitokotoQuery.DefaultMaxLength;

    /// <summary>
    ///     附加查询串（可留空）。只有官方参数里<b>我们没管</b>的那几个会生效，
    ///     <c>encode</c> / <c>charset</c> / <c>callback</c> 这类会被忽略 —— 否则响应就不是 JSON 了。
    /// </summary>
    [ObservableProperty] private string _quoteExtraQuery = "";

    /// <summary>走海外端点（<c>international.v1.hitokoto.cn</c>，QPS 20）。默认走国内。</summary>
    [ObservableProperty] private bool _quoteUseInternationalEndpoint;

    /// <summary>
    ///     一言多久换一条（分钟）。<c>0</c> 表示「每次打开看板模式都换一条」。
    /// </summary>
    [ObservableProperty] private int _quoteRefreshMinutes = DefaultQuoteRefreshMinutes;

    /// <summary>
    ///     屏蔽词：<b>每行一个</b>，正文、出处、作者任意一处包含就整句不要（换一条再试）。
    ///     <para>
    ///         官方 v1 没有「排除关键词」的参数，只能拿回来自己判；判掉之后的处理在
    ///         <c>QuoteService</c>（重取有限次，拿不到就继续显示上一条），
    ///         拆词与匹配规则在 <see cref="QuoteBlocklist" />（纯逻辑、有单测）。
    ///     </para>
    /// </summary>
    [ObservableProperty] private string _quoteBlockedWords = "";

    /// <summary>在一言下面显示出处（<c>from</c>）。默认开。</summary>
    [ObservableProperty] private bool _quoteShowSource = true;

    /// <summary>在一言下面显示作者（<c>from_who</c>）。默认开。</summary>
    [ObservableProperty] private bool _quoteShowAuthor = true;

    // ── 天气 ────────────────────────────────────────────────

    /// <summary>在看板模式里显示天气。默认开。</summary>
    [ObservableProperty] private bool _weatherEnabled = true;

    /// <summary>选中城市名（只用于显示与再次搜索；定位靠下面的经纬度）。</summary>
    [ObservableProperty] private string _weatherCityName = DefaultCityName;

    /// <summary>城市纬度。</summary>
    [ObservableProperty] private double _weatherLatitude = DefaultLatitude;

    /// <summary>城市经度。</summary>
    [ObservableProperty] private double _weatherLongitude = DefaultLongitude;

    /// <summary>服务端的城市标识（<c>weathercn:101010100</c>），留作排查用。</summary>
    [ObservableProperty] private string _weatherLocationKey = "";

    /// <summary>天气多久刷新一次（分钟）。</summary>
    [ObservableProperty] private int _weatherRefreshMinutes = DefaultWeatherRefreshMinutes;

    // ── 天气要显示哪几块（每块自己一个开关，勾掉就不占位置） ──────────

    /// <summary>显示天气图标（随昼夜换日月）。默认开。</summary>
    [ObservableProperty] private bool _weatherShowIcon = true;

    /// <summary>显示当前温度（那个大字）。默认开。</summary>
    [ObservableProperty] private bool _weatherShowTemperature = true;

    /// <summary>显示天气现象（晴、多云……）。默认开。</summary>
    [ObservableProperty] private bool _weatherShowCondition = true;

    /// <summary>显示城市名。默认开。</summary>
    [ObservableProperty] private bool _weatherShowCity = true;

    /// <summary>显示今天最高 / 最低温度。默认开。</summary>
    [ObservableProperty] private bool _weatherShowRange = true;

    /// <summary>显示湿度。默认开。</summary>
    [ObservableProperty] private bool _weatherShowHumidity = true;

    /// <summary>显示空气质量。默认开。</summary>
    [ObservableProperty] private bool _weatherShowAqi = true;

    /// <summary>显示体感温度。默认关：不是每个人都关心这两个数的差别。</summary>
    [ObservableProperty] private bool _weatherShowFeelsLike;

    /// <summary>显示明天的天气与温度。默认关，看板越小越显得挤。</summary>
    [ObservableProperty] private bool _weatherShowTomorrow;

    /// <summary>读盘后把两边都收拾一遍（字数、间隔、经纬度、类别位）。</summary>
    public void Normalize()
    {
        QuoteCategories &= QuoteCategory.All | QuoteCategory.NeteaseMusic;

        QuoteMaxLength = Math.Clamp(QuoteMaxLength, 0, HitokotoQuery.MaxAllowedLength);
        QuoteMinLength = QuoteMinLength <= 0 ? 0 : Math.Clamp(QuoteMinLength, 0, QuoteMaxLength);

        QuoteRefreshMinutes = QuoteRefreshMinutes <= 0
            ? 0
            : Math.Clamp(QuoteRefreshMinutes, MinRefreshMinutes, MaxRefreshMinutes);

        QuoteExtraQuery = QuoteExtraQuery?.Trim() ?? "";

        // 屏蔽词按「一行一个」落盘：去空行、去重、超上限截断都在 QuoteBlocklist 里。
        QuoteBlockedWords = QuoteBlocklist.Normalize(QuoteBlockedWords);

        WeatherRefreshMinutes = Math.Clamp(WeatherRefreshMinutes, MinRefreshMinutes, MaxRefreshMinutes);

        if (!double.IsFinite(WeatherLatitude) || WeatherLatitude is < -90 or > 90)
            WeatherLatitude = DefaultLatitude;

        if (!double.IsFinite(WeatherLongitude) || WeatherLongitude is < -180 or > 180)
            WeatherLongitude = DefaultLongitude;

        WeatherCityName = string.IsNullOrWhiteSpace(WeatherCityName) ? DefaultCityName : WeatherCityName.Trim();
        WeatherLocationKey = WeatherLocationKey?.Trim() ?? "";
    }

    /// <summary>按当前配置拼一个一言请求选项。</summary>
    public HitokotoQueryOptions BuildQuoteOptions() => new HitokotoQueryOptions
    {
        Categories = QuoteCategories,
        MinLength = QuoteMinLength,
        MaxLength = QuoteMaxLength,
        ExtraQuery = QuoteExtraQuery,
        UseInternationalEndpoint = QuoteUseInternationalEndpoint
    }.Normalize();
}
