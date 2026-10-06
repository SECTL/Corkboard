namespace Corkboard.Services.Auth;

/// <summary>
///     一次 OAuth 授权尝试：需要告诉授权端点的信息，以及换取授权码所需的 PKCE verifier。
/// </summary>
public sealed record AuthRedirectAttempt(string RedirectUri, string State, string CodeVerifier);

/// <summary>
///     授权端点回传的重定向，以及令牌交换所需的全部信息。
///     <paramref name="Error" /> 承载服务端给出的失败描述。
/// </summary>
public sealed record AuthRedirectResult(
    string RedirectUri,
    string State,
    string CodeVerifier,
    string? Code,
    string? Error);

/// <summary>
///     为一次登录取得授权重定向，使 PKCE、state 校验、令牌交换与刷新策略保持平台无关。
///     本项目只提供桌面实现：监听本机 loopback 端口收下系统浏览器的回调。
/// </summary>
public interface IAuthRedirectBroker : IDisposable
{
    /// <summary>等待用户完成授权的上限。loopback 只需要撑过一次浏览器往返。</summary>
    TimeSpan WaitTimeout { get; }

    /// <summary>本次尝试要交给授权端点的重定向地址。</summary>
    string CreateRedirectUri();

    /// <summary>在浏览器打开之前登记本次尝试（loopback 实现此时开始监听）。</summary>
    Task PrepareAsync(AuthRedirectAttempt attempt, CancellationToken cancellationToken = default);

    /// <summary>等待属于 <paramref name="attempt" /> 的重定向。</summary>
    Task<AuthRedirectResult> WaitForRedirectAsync(AuthRedirectAttempt attempt,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     返回上一次进程生命周期里到达、尚未交换的重定向；没有可用结果时返回 <see langword="null" />。
    ///     桌面 loopback 流程总在同一次尝试内完成，因此恒为 <see langword="null" />，
    ///     保留该成员是为了让上层启动逻辑在平台实现变化时不必改签名。
    /// </summary>
    Task<AuthRedirectResult?> TakePendingRedirectAsync(CancellationToken cancellationToken = default);
}

/// <summary>每次登录创建一个代理，因为一个代理只持有一次尝试的状态。</summary>
public interface IAuthRedirectBrokerFactory
{
    IAuthRedirectBroker Create();
}
