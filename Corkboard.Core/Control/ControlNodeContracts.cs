using Microsoft.Extensions.Logging;

namespace Corkboard.Core.ControlCenter;

/// <summary>
///     集控长连接的状态。
///     <para>
///         状态机（A4 定稿）：<c>Disabled</c> → <c>Unpaired</c> → <c>Connecting</c> → <c>Online</c>，
///         之后可能被顶掉（<see cref="Superseded" />）或被移除（<see cref="Revoked" />）。
///         <b>吊销 ≠ 断线</b>：断线只是回到 <see cref="Connecting" /> 继续退避重连，
///         吊销 / 凭证无效 / 协议太旧才是终态（清凭证、不再重试）。
///     </para>
///     <para>
///         枚举按数字落盘/上报（就绪态判定只看数值），因此**只能在末尾追加成员**。
///     </para>
/// </summary>
public enum ControlNodeLinkStatus
{
    /// <summary>设置里没启用集控。</summary>
    Disabled = 0,

    /// <summary>已启用，但还没有设备凭证（需要「地点ID + 配对码」）。</summary>
    Unpaired = 1,

    /// <summary>连接中 / 已断开、正在退避重连。</summary>
    Connecting = 2,

    /// <summary>已连上集控中心（可以还没选班）。</summary>
    Online = 3,

    /// <summary>这个班已被别的设备接管（不是终态，重连后还能自己再选）。</summary>
    Superseded = 4,

    /// <summary>设备已被网页移除 / 凭证无效 / 协议太旧：需要重新接入，不再自动重连。</summary>
    Revoked = 5,

    /// <summary>用户主动停了客户端。</summary>
    Stopped = 6
}

/// <summary>
///     集控状态的不可变快照。界面拿它画状态条，<b>不要</b>把中文写进这里：
///     文案是 <see cref="StatusKey" /> / <see cref="DetailKey" /> 这样的本地化键，
///     resx 由下一波统一加（应用是中英双语，中文不允许出现 Core 里）。
/// </summary>
/// <param name="Status">当前状态。</param>
/// <param name="DetailKey">补充说明的本地化键（可为 <c>null</c>）。</param>
/// <param name="RetryDelay">正在退避重连时，距离下一次尝试还有多久。</param>
/// <param name="SelectedClassId">本机当前占着的班（没有就是 <c>null</c>）。</param>
public sealed record ControlNodeLinkState(
    ControlNodeLinkStatus Status,
    string? DetailKey = null,
    TimeSpan? RetryDelay = null,
    string? SelectedClassId = null)
{
    /// <summary>初值：还没启动。</summary>
    public static ControlNodeLinkState Initial { get; } = new(ControlNodeLinkStatus.Disabled);

    /// <summary>状态文案的本地化键（下一波在 resx 里加对应条目）。</summary>
    public string StatusKey => Status switch
    {
        ControlNodeLinkStatus.Disabled => "Settings_Control_Status_Disabled",
        ControlNodeLinkStatus.Unpaired => "Settings_Control_Status_Unpaired",
        ControlNodeLinkStatus.Connecting => "Settings_Control_Status_Connecting",
        ControlNodeLinkStatus.Online => "Settings_Control_Status_Online",
        ControlNodeLinkStatus.Superseded => "Settings_Control_Status_Superseded",
        ControlNodeLinkStatus.Revoked => "Settings_Control_Status_Revoked",
        ControlNodeLinkStatus.Stopped => "Settings_Control_Status_Stopped",
        _ => "Settings_Control_Status_Unknown"
    };

    /// <summary>连接还在（含被顶掉但连接没断的情况）。</summary>
    public bool IsConnected => Status is ControlNodeLinkStatus.Online or ControlNodeLinkStatus.Superseded;

    /// <summary>需要用户重新输入「地点ID + 配对码」。</summary>
    public bool NeedsEnrollment => Status is ControlNodeLinkStatus.Unpaired or ControlNodeLinkStatus.Revoked;

    /// <summary>被外力打断（被顶掉 / 被移除），界面上要显眼提示。</summary>
    public bool IsInterrupted => Status is ControlNodeLinkStatus.Superseded or ControlNodeLinkStatus.Revoked;
}

/// <summary>
///     <see cref="ControlNodeLinkState.DetailKey" /> 的取值。
///     <para>跟状态键一样，全是本地化键，不是中文文案。</para>
/// </summary>
public static class ControlDetailKeys
{
    /// <summary>设置里没启用。</summary>
    public const string Disabled = "Settings_Control_Detail_Disabled";

    /// <summary>还没有设备凭证，等用户填码。</summary>
    public const string Unpaired = "Settings_Control_Detail_Unpaired";

    /// <summary>正在建立连接。</summary>
    public const string Connecting = "Settings_Control_Detail_Connecting";

    /// <summary>已连上集控中心。</summary>
    public const string Online = "Settings_Control_Detail_Online";

    /// <summary>连接断了，正在退避重连。</summary>
    public const string Retrying = "Settings_Control_Detail_Retrying";

    /// <summary>这个班已被别的设备接管。</summary>
    public const string Superseded = "Settings_Control_Detail_Superseded";

    /// <summary>设备已被网页移除 / 凭证无效，需要重新接入。</summary>
    public const string Revoked = "Settings_Control_Detail_Revoked";

    /// <summary>协议版本太旧，升级后才能连。</summary>
    public const string ProtocolTooOld = "Settings_Control_Detail_ProtocolTooOld";

    /// <summary>用户主动停止。</summary>
    public const string Stopped = "Settings_Control_Detail_Stopped";
}

/// <summary>
///     一条已经序列化好的长连接帧的收发通道。
///     <para>
///         刻意只交换 <see cref="string" />：帧编解码留在会话层，传输层可以换成 WebSocket、
///         内存管道或测试里的假实现，单测因此不需要起真的 WebSocket 服务端。
///     </para>
/// </summary>
public interface IControlNodeTransport : IAsyncDisposable
{
    /// <summary>发一帧已经序列化好的 JSON 文本。</summary>
    Task SendAsync(string frameJson, CancellationToken cancellationToken);

    /// <summary>收一帧 JSON 文本；连接正常关闭时返回 <c>null</c>。</summary>
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);

    /// <summary>关闭原因（对端给的，可为 <c>null</c>）。</summary>
    string? CloseReason { get; }

    /// <summary>关闭码（对端给的，可为 <c>null</c>）。</summary>
    int? CloseCode { get; }
}

/// <summary>建立长连接。凭证不进 URL（会被反代访问日志原样记下来）。</summary>
public interface IControlNodeTransportFactory
{
    /// <summary>连到 <paramref name="endpoint" />（<c>wss://…/control/ws</c>）。</summary>
    Task<IControlNodeTransport> ConnectAsync(Uri endpoint, CancellationToken cancellationToken);
}

/// <summary>
///     设备凭证的读写口。实现必须<b>不抛</b>：文件坏了 / 密钥对不上 = 「没有凭证」，
///     让用户重新配对，而不是把整个应用弄崩。
/// </summary>
public interface IControlNodeCredentialProvider
{
    /// <summary>当前内存里的凭证（没读过或已清掉时为 <c>null</c>）。</summary>
    ControlNodeCredential? Current { get; }

    /// <summary>读凭证；没有 / 读不出来都返回 <c>null</c>。</summary>
    Task<ControlNodeCredential?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>写凭证（先写密钥再写密文，顺序不能反）。</summary>
    Task SaveAsync(ControlNodeCredential credential, CancellationToken cancellationToken = default);

    /// <summary>清掉凭证（被移除 / 用户「取消接入」时调）。</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>
///     已应用期望状态与上次选的班。落盘在 <c>data/config/control/node-state.json</c>，
///     <b>不是</b>设置项（不进 settings.json、不参与云同步）。
/// </summary>
public interface IControlNodeStateStore
{
    /// <summary>读状态；文件坏了返回默认值（不抛）。</summary>
    ControlNodeState Load();

    /// <summary>原子写状态；写失败只记日志（不抛）。</summary>
    void Save(ControlNodeState state);
}

/// <summary>把「已应用 revision / 上次选的班」持久化下来的不可变快照。</summary>
/// <param name="AppliedStateRevision">已应用的期望状态 revision（重启后靠它防重放）。</param>
/// <param name="SelectedClassId">上次占着的班（重连后自动重占）。</param>
/// <param name="UpdatedAt">最后一次写入时间（只用于排障）。</param>
public sealed record ControlNodeState(
    long AppliedStateRevision = 0,
    string? SelectedClassId = null,
    DateTimeOffset UpdatedAt = default)
{
    /// <summary>默认值：没应用过任何状态、没选过班。</summary>
    public static ControlNodeState Empty { get; } = new();
}

/// <summary>
///     连接参数。服务端下发的 <c>heartbeat_seconds</c> / <c>offline_after_seconds</c> /
///     <c>limits.cmdTimeoutMs</c> 会覆盖这里的缺省值（协议 §3.5：客户端不许写死）。
/// </summary>
public sealed class ControlNodeConnectOptions
{
    /// <summary>服务端默认地址。</summary>
    public const string DefaultServerUrl = "https://sectl.cn";

    /// <summary>服务端登记的 Corkboard 平台标识。</summary>
    public const string DefaultPlatform = "corkboard";

    /// <summary>
    ///     退避序列：1s → 2s → 5s → 15s → 30s 封顶（A4 定稿；与参考客户端的 2^n 不同，
    ///     教室机整批掉线时前者更平缓）。
    /// </summary>
    public static IReadOnlyList<TimeSpan> DefaultBackoff { get; } =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30)
    ];

    /// <summary>站点地址（<c>https://sectl.cn</c> 或 <c>http://host:port</c>）；启动时按设置里的地址刷新。</summary>
    public string ServerUrl { get; set; } = DefaultServerUrl;

    /// <summary>平台标识。</summary>
    public string Platform { get; init; } = DefaultPlatform;

    /// <summary>客户端版本（只用于网页上展示）。</summary>
    public string Version { get; init; } =
        typeof(ControlNodeConnectOptions).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>握手回包没给 <c>limits.cmdTimeoutMs</c> 时的指令超时。</summary>
    public int FallbackCommandTimeoutMs { get; init; } = ControlV4Protocol.DefaultCommandTimeoutMs;

    /// <summary>建立 TCP/TLS 连接的超时。</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>hello 回包等多久（协议要求首帧 10 秒内，否则服务端 4002 关闭）。</summary>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>普通请求（选班 / 释放 / tree.get）等回执的超时。</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>服务端说 <c>deadline_ms</c>，客户端留出的余量：在 <c>deadline − margin</c> 就代回 <c>timeout</c>。</summary>
    public TimeSpan CommandResultMargin { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>退避序列（测试里可换短的）。</summary>
    public IReadOnlyList<TimeSpan> ReconnectBackoff { get; init; } = DefaultBackoff;

    /// <summary>握手成功后是否自动重占上次的班（<c>_selectedClassId</c>）。</summary>
    public bool RestoreSelectedClass { get; init; } = true;

    /// <summary>按「第几次连续失败」取退避时长，越界取最后一个（封顶）。</summary>
    public TimeSpan BackoffFor(int attempt)
    {
        var backoff = ReconnectBackoff.Count > 0 ? ReconnectBackoff : DefaultBackoff;
        var index = Math.Clamp(attempt, 0, backoff.Count - 1);
        return backoff[index];
    }

    /// <summary><c>https://sectl.cn</c> → <c>wss://sectl.cn/control/ws</c>。</summary>
    public Uri ResolveWebSocketEndpoint()
    {
        var baseUri = ResolveBaseUri();
        var scheme = baseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? "wss"
            : "ws";
        var prefix = baseUri.AbsolutePath.TrimEnd('/');
        var builder = new UriBuilder(baseUri)
        {
            Scheme = scheme,
            Path = prefix + ControlV4Protocol.WebSocketPath
        };
        return builder.Uri;
    }

    /// <summary>拼一个 HTTP 接口地址（<c>enroll</c> / <c>device/tree</c>）。</summary>
    public Uri ResolveHttpEndpoint(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return new Uri(ResolveBaseUri(), relativePath.TrimStart('/'));
    }

    /// <summary>站点根地址；带 <c>/</c> 结尾才能拼相对路径。</summary>
    public Uri ResolveBaseUri()
    {
        var url = string.IsNullOrWhiteSpace(ServerUrl) ? DefaultServerUrl : ServerUrl.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            uri = new Uri(DefaultServerUrl);
        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            uri = new Uri(DefaultServerUrl);
        return new UriBuilder(uri) { Path = uri.AbsolutePath.TrimEnd('/') + "/" }.Uri;
    }
}

/// <summary>还没连上就发请求时抛这个（选班 / 释放班 / tree.get）。</summary>
public sealed class ControlNodeNotConnectedException : Exception
{
    /// <summary>默认消息。</summary>
    public ControlNodeNotConnectedException() : base("control node is not connected")
    {
    }
}

/// <summary>
///     会话层的日志出口。Core 里允许直接注入 <see cref="ILogger" />，
///     这里包一层只是为了给「订阅方回调里抛异常」也留一个统一的吞异常点。
/// </summary>
internal static class ControlLogExtensions
{
    /// <summary>回调异常只记 debug：界面写坏了不能把集控连接带下去。</summary>
    public static void LogCallbackFailure(this ILogger? logger, string what, Exception exception) =>
        logger?.LogDebug(exception, "集控回调 {What} 抛出异常", what);
}
