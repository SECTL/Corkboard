namespace Corkboard.Core.Widgets.Weather;

/// <summary>
///     城市搜索里的一条：名字、行政区归属、经纬度，外加服务端那串 <c>locationKey</c>。
///     <para>
///         接口里<b>没有数字型的 cityId</b>：大家常说的「101010100」只是 <c>locationKey</c>
///         （<c>weathercn:101010100</c>）的尾巴。而定位时服务端只认经纬度，
///         所以真正要用的是 <see cref="Latitude" /> / <see cref="Longitude" />。
///     </para>
/// </summary>
/// <param name="Name">城市 / 区县名（如「东城区」）。</param>
/// <param name="Affiliation">上级归属（如「北京市, 中国」），可能没有。</param>
/// <param name="Latitude">纬度。</param>
/// <param name="Longitude">经度。</param>
/// <param name="LocationKey">服务端标识（<c>weathercn:101011600</c>），可能没有。</param>
public sealed record XiaomiCity(
    string Name,
    string? Affiliation,
    double Latitude,
    double Longitude,
    string? LocationKey)
{
    /// <summary>
    ///     下拉列表里显示的那一行：<c>东城区（北京市, 中国）</c>；没有归属时就只有名字。
    ///     <para>
    ///         全国重名的区县很多（东城区、朝阳区各有一堆），不带上归属根本选不对。
    ///     </para>
    /// </summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Affiliation) ? Name : $"{Name}（{Affiliation}）";
}
