using System.Globalization;

namespace Corkboard.Core.Display;

/// <summary>
///     <c>corkboard://</c> 自定义协议的解析 —— 纯函数，有单测。
///     <para>
///         只认一个主机名 <c>display</c>（看板模式），命令有两种：
///         <list type="bullet">
///             <item><c>corkboard://display</c> —— 打开全屏看板模式（已经开着就拉到前面）。</item>
///             <item><c>corkboard://display?exit=1</c> —— 关掉它。</item>
///         </list>
///         另外还认 <c>?from=19:00&amp;to=21:30</c>：这一次打开时临时用这对时刻，
///         不改用户存下来的配置（给「今天晚展示改到八点」这种临时情况用）。
///     </para>
///     <para>
///         ⚠️ <b>不抛异常</b>：这个方法的输入来自浏览器与命令行，什么样的脏串都可能来。
///         认不出来就返回 <see langword="false" />，调用方照常启动，什么都不做。
///     </para>
/// </summary>
public static class DisplayBoardUri
{
    /// <summary>协议 scheme，与 <c>GlobalConstants.UrlProtocolScheme</c> 同值。</summary>
    public const string Scheme = "corkboard";

    /// <summary>看板模式的主机名：<c>corkboard://display</c>。</summary>
    public const string Host = "display";

    /// <summary>
    ///     旧版本的主机名（功能还叫「自习模式」时）：<c>corkboard://study</c> 仍然认。
    ///     浏览器书签、快捷方式里存的可能还是旧地址，别让它们失效。
    /// </summary>
    public const string LegacyHost = "study";

    /// <summary>完整的协议前缀（注册表与文档里用）。</summary>
    public const string Prefix = Scheme + "://";

    /// <summary>
    ///     试着把一条命令行参数解析成看板模式指令。
    /// </summary>
    /// <param name="argument">一条参数（可能是 <c>corkboard://display</c>，也可能是别的）。</param>
    /// <param name="command">解析出来的指令；认不出来时为 <see langword="null" />。</param>
    /// <returns>这条参数是不是看板模式的 URI。</returns>
    public static bool TryParse(string? argument, out DisplayBoardCommand? command)
    {
        command = null;

        if (string.IsNullOrWhiteSpace(argument))
            return false;

        var text = argument.Trim();

        // 命令行里常常带着一对引号（Windows 的 URL 关联会把整条 URI 用引号包起来）。
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            text = text[1..^1].Trim();

        if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = text[Prefix.Length..];

        // 主机名到第一个 / ? # 为止。
        var hostEnd = rest.IndexOfAny(['/', '?', '#']);
        var host = hostEnd < 0 ? rest : rest[..hostEnd];
        if (!IsKnownHost(host))
            return false;

        var query = hostEnd < 0 ? string.Empty : rest[hostEnd..];
        command = ParseQuery(query);
        return true;
    }

    /// <summary>在一串参数里找出第一条看板模式 URI（<c>Program.Main</c> 用它）。</summary>
    public static bool TryParseAny(IEnumerable<string>? arguments, out DisplayBoardCommand? command)
    {
        command = null;
        if (arguments is null)
            return false;

        foreach (var argument in arguments)
        {
            if (TryParse(argument, out var parsed))
            {
                command = parsed;
                return true;
            }
        }

        return false;
    }

    /// <summary>新主机名与旧主机名都认（<c>display</c> / <c>study</c>），大小写不敏感。</summary>
    private static bool IsKnownHost(string host) =>
        string.Equals(host, Host, StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, LegacyHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>把 <c>?exit=1&amp;from=19:00&amp;to=21:30</c> 这样的查询串读成指令。</summary>
    private static DisplayBoardCommand ParseQuery(string query)
    {
        var exit = false;
        TimeOnly? from = null;
        TimeOnly? to = null;

        var trimmed = query.TrimStart('?', '/');
        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = (separator < 0 ? pair : pair[..separator]).Trim();
            var value = separator < 0 ? string.Empty : pair[(separator + 1)..].Trim();

            if (key.Equals("exit", StringComparison.OrdinalIgnoreCase))
                exit = IsTruthy(value);
            else if (key.Equals("from", StringComparison.OrdinalIgnoreCase) && TryParseTime(value) is { } parsedFrom)
                from = parsedFrom;
            else if (key.Equals("to", StringComparison.OrdinalIgnoreCase) && TryParseTime(value) is { } parsedTo)
                to = parsedTo;
        }

        // from 与 to 必须成对：只给一个的话那段时段没有意义，两个一起忽略。
        var window = from is { } start && to is { } end
            ? new DisplayTimeWindow(start, end)
            : (DisplayTimeWindow?)null;

        return new DisplayBoardCommand(exit ? DisplayBoardAction.Close : DisplayBoardAction.Open, window);
    }

    /// <summary>只给键不给值时算「真」（<c>?exit</c> 与 <c>?exit=1</c> 同义）。</summary>
    private static bool IsTruthy(string value) =>
        value.Length == 0
        || value.Equals("1", StringComparison.Ordinal)
        || value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value.Equals("yes", StringComparison.OrdinalIgnoreCase);

    /// <summary>读 <c>HH:mm</c> / <c>H:mm</c> / <c>HH:mm:ss</c> 三种写法，认不出来返回 <see langword="null" />。</summary>
    private static TimeOnly? TryParseTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // 冒号在 URL 里可能被转义成 %3A。
        var text = value.Replace("%3A", ":", StringComparison.OrdinalIgnoreCase);

        var parts = text.Split(':');
        if (parts.Length is < 2 or > 3)
            return null;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minute))
            return null;

        var second = 0;
        if (parts.Length == 3
            && !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out second))
            return null;

        if (hour is < 0 or > 23 || minute is < 0 or > 59 || second is < 0 or > 59)
            return null;

        return new TimeOnly(hour, minute, second);
    }
}

/// <summary>URI 让看板模式做哪件事。</summary>
public enum DisplayBoardAction
{
    /// <summary>打开全屏看板模式。</summary>
    Open = 0,

    /// <summary>关掉全屏看板模式。</summary>
    Close = 1
}

/// <summary>一条解析好的看板模式指令。</summary>
/// <param name="Action">打开还是关掉。</param>
/// <param name="Window">这一次临时使用的时段；<see langword="null" /> 表示按配置里的来。</param>
public readonly record struct DisplayBoardCommand(DisplayBoardAction Action, DisplayTimeWindow? Window);

/// <summary>一次临时的展示时段（URI 里的 <c>from</c> / <c>to</c>）。</summary>
/// <param name="Enter">进入时刻。</param>
/// <param name="Exit">退出时刻；小于进入时刻表示跨零点。</param>
public readonly record struct DisplayTimeWindow(TimeOnly Enter, TimeOnly Exit);
