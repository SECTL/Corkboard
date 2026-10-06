namespace Corkboard.Services.Auth;

/// <summary>
///     桌面端 SECTL OAuth 会话：持有访问/刷新令牌对、刷新策略，以及对其他 SECTL API 的唯一授权出口。
/// </summary>
/// <remarks>
///     刷新端点每次成功都会轮换 refresh token 并立刻作废旧值，因此实现必须
///     (1) 在使用新访问令牌之前把新令牌对原子落盘，
///     (2) 把并发刷新合并成单飞，
///     (3) 把被服务端拒绝的 refresh token 当作终态而不是循环重试。
/// </remarks>
public interface ISectlAuthService
{
    /// <summary>当前内存中的令牌对；未登录时为 <see langword="null" />。</summary>
    SectlToken? Token { get; }

    /// <summary>是否存在可用的访问令牌。</summary>
    bool IsSignedIn { get; }

    /// <summary>账号资料（含头像 URL）；尚未取到时为 <see langword="null" />。</summary>
    SectlUser? User { get; }

    /// <summary>已下载的头像字节；未取到时为 <see langword="null" />。</summary>
    byte[]? AvatarBytes { get; }

    /// <summary>
    ///     服务端永久拒绝了已保存的凭据（refresh token 被重用/撤销/过期、客户端不匹配、
    ///     访问令牌被吊销）时置为 true。此时本地会话已被清空，用户必须重新授权。
    /// </summary>
    bool RequiresReauthorization { get; }

    /// <summary>登录状态或账号资料发生变化。事件可能在后台线程触发。</summary>
    event EventHandler? StateChanged;

    /// <summary>
    ///     从磁盘装回登录态。本地状态先放行，账号资料与头像在后台补齐（完成后触发
    ///     <see cref="StateChanged" />），因此启动链路可以安全地等待本方法。
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     交换上一次进程生命周期里到达的授权回调。桌面 loopback 流程不会产生待处理回调，
    ///     调用它是安全的空操作，保留以便启动逻辑统一。
    /// </summary>
    Task ResumePendingAuthorizationAsync(CancellationToken cancellationToken = default);

    /// <summary>走 PKCE 授权码流程登录：打开系统浏览器，等待 loopback 回调，换取并持久化令牌。</summary>
    Task SignInAsync(CancellationToken cancellationToken = default);

    /// <summary>通知服务端结束会话并清空本地令牌。</summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     发送一个带授权的 SECTL API 请求：令牌临近过期时先刷新，被服务端拒绝时再刷新重试一次。
    ///     调用方传入请求工厂，因为被拒绝的请求必须重建才能重试；令牌存储与刷新策略留在本服务内部。
    /// </summary>
    Task<HttpResponseMessage> SendAuthorizedAsync(
        Func<HttpRequestMessage> createRequest,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     返回非 HTTP 传输（例如 WebSocket 的 Authorization 头）应当携带的访问令牌，
    ///     策略与 <see cref="SendAuthorizedAsync" /> 一致：过期前先刷新，被拒绝后强制刷新一次。
    /// </summary>
    Task<string?> TryGetAccessTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>上报设备活跃。被拒绝的访问令牌会先刷新一次再重试。</summary>
    Task<bool> SendHeartbeatAsync(CancellationToken cancellationToken = default);
}
