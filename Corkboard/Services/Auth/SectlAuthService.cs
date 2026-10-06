using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.Auth;

/// <summary>
///     桌面端 SECTL OAuth 会话实现，见 <see cref="ISectlAuthService" />。
/// </summary>
/// <remarks>
///     所有依赖都通过构造函数注入，便于在 DI 容器里注册；
///     日志使用 <see cref="ILogger{T}" />，不依赖任何遥测组件，也不依赖任何配置模型。
/// </remarks>
public sealed class SectlAuthService(
    SectlTokenStore tokenStore,
    IHttpClientFactory httpClientFactory,
    IDeviceIdProvider deviceIdProvider,
    ILogger<SectlAuthService> logger,
    IAuthRedirectBrokerFactory redirectBrokerFactory,
    IAuthBrowser authBrowser) : ISectlAuthService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>服务端未给出或给不出 <c>expires_in</c> 时的兜底访问令牌有效期。</summary>
    private const int DefaultAccessTokenLifetimeSeconds = 3600;

    /// <summary>
    ///     刷新令牌有效期。从首次授权开始计算，轮换不会延长它；
    ///     窗口结束后客户端停止刷新（而不是对着一个已死的令牌空转）。
    /// </summary>
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(180);

    /// <summary>提前这么久刷新，保证请求不会带着即将失效的令牌发出。</summary>
    private static readonly TimeSpan AccessTokenRefreshLead = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan RefreshRequestTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RefreshLockTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromSeconds(65);

    private static readonly TimeSpan[] InitializationRetryDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1)
    ];

    private readonly object _refreshGate = new();
    private Task<SectlRefreshOutcome>? _inflightRefresh;
    private SectlToken? _token;
    private int _sessionVersion;
    private bool _initialized;

    public SectlToken? Token => _token;
    public bool IsSignedIn => !string.IsNullOrWhiteSpace(_token?.AccessToken);
    public SectlUser? User { get; private set; }
    public byte[]? AvatarBytes { get; private set; }
    public bool RequiresReauthorization { get; private set; }

    public event EventHandler? StateChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        try
        {
            _token = await tokenStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _token = null;
        }

        _initialized = true;

        // 本地登录状态必须先放行：账号资料与头像来自网络，界面等到资料就绪会把首屏拖慢；
        // 资料改在后台补齐，并通过 StateChanged 刷新界面。
        StateChanged?.Invoke(this, EventArgs.Empty);
        if (IsSignedIn)
            _ = RefreshAccountDataAsync(cancellationToken);
        else
            _ = ResumePendingAuthorizationAsync(cancellationToken);
    }

    /// <summary>
    ///     交换上一次进程生命周期里到达、尚未完成的授权回调。
    ///     桌面 loopback 流程总在同一次尝试内完成，代理因此恒返回空结果，这里等于空操作；
    ///     保留它是为了让平台回调方式变化时上层启动逻辑无需改动。
    /// </summary>
    public async Task ResumePendingAuthorizationAsync(CancellationToken cancellationToken = default)
    {
        if (IsSignedIn)
            return;

        try
        {
            using var broker = redirectBrokerFactory.Create();
            var redirect = await broker.TakePendingRedirectAsync(cancellationToken).ConfigureAwait(false);
            if (redirect is null)
                return;

            await CompleteAuthorizationAsync(redirect, expectedState: null, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "恢复未完成的 SECTL 授权失败。");
        }
    }

    /// <summary>
    ///     后台拉取账号资料与头像。失败时保留已加载的令牌，只清空资料，
    ///     并通过 <see cref="StateChanged" /> 让界面自行收敛到「资料不可用」。
    /// </summary>
    private async Task RefreshAccountDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            await InitializeAccountDataWithRetryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task InitializeAccountDataWithRetryAsync(CancellationToken cancellationToken)
    {
        foreach (var delay in InitializationRetryDelays)
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            try
            {
                var user = await GetUserInfoAsync(cancellationToken).ConfigureAwait(false);
                if (user is null)
                    continue;

                User = user;
                AvatarBytes = await GetAvatarBytesAsync(user.ResolvedAvatarUrl, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // 启动期的临时失败由下一轮重试；令牌保留，账号仍可安全登出。
            }
        }

        User = null;
        AvatarBytes = null;
    }

    public async Task SignInAsync(CancellationToken cancellationToken = default)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        using var broker = redirectBrokerFactory.Create();
        var attempt = new AuthRedirectAttempt(broker.CreateRedirectUri(), state, verifier);
        // loopback 代理在这里开始监听；此后浏览器里的回调才可能被接住。
        await broker.PrepareAsync(attempt, cancellationToken).ConfigureAwait(false);

        var query = string.Join("&", new Dictionary<string, string>
        {
            ["client_id"] = SectlAuthEndpoints.ClientId,
            ["redirect_uri"] = attempt.RedirectUri,
            ["response_type"] = SectlAuthEndpoints.ResponseType,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = SectlAuthEndpoints.CodeChallengeMethod,
            ["scope"] = SectlAuthEndpoints.OAuthScope,
            ["state"] = state
        }.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        var authorizeUrl = $"{SectlAuthEndpoints.AuthorizeUrl}?{query}";
        if (!authBrowser.TryOpenAuthorization(authorizeUrl))
            throw new InvalidOperationException("无法打开浏览器。");

        AuthRedirectResult redirect;
        try
        {
            redirect = await broker.WaitForRedirectAsync(attempt, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // 授权页被关掉、或服务端拒绝了 redirect_uri 时，回调永远不会到达：
            // 给用户一个明确的结论，而不是让界面一直停在「登录中」。
            throw new InvalidOperationException("登录超时，请重试。");
        }

        await CompleteAuthorizationAsync(redirect, state, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     用授权码换取令牌对。<paramref name="expectedState" /> 是本次登录生成的 state；
    ///     只有当代理解已经自行比对过该次尝试时才可以传 <see langword="null" />。
    /// </summary>
    private async Task CompleteAuthorizationAsync(AuthRedirectResult redirect, string? expectedState,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(redirect.Error))
            throw new InvalidOperationException($"SECTL 授权失败：{redirect.Error}");
        var stateMatches = expectedState is null || string.Equals(expectedState, redirect.State, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(redirect.Code) || !stateMatches)
            throw new InvalidOperationException("SECTL 授权回调无效。");

        var client = httpClientFactory.CreateClient();
        // 客户端不再自行解析并上报公网 IP：服务端从连接来源即可得知地址，本机只上传设备标识。
        var payload = new
        {
            grant_type = SectlAuthEndpoints.AuthorizationCodeGrantType,
            code = redirect.Code,
            client_id = SectlAuthEndpoints.ClientId,
            redirect_uri = redirect.RedirectUri,
            code_verifier = redirect.CodeVerifier,
            device_uuid = deviceIdProvider.GetOrCreate().ToString()
        };
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, SectlAuthEndpoints.TokenUrl)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        tokenRequest.Headers.UserAgent.ParseAdd(SectlAuthEndpoints.BuildUserAgent());
        using var result = await client.SendAsync(tokenRequest, cancellationToken).ConfigureAwait(false);
        result.EnsureSuccessStatusCode();
        var issued = await result.Content.ReadFromJsonAsync<SectlToken>(JsonOptions, cancellationToken).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("SECTL 未返回 token。");
        var authorizedAt = DateTimeOffset.UtcNow;
        var authorized = issued with
        {
            AccessTokenExpiresAt = authorizedAt.AddSeconds(ResolveAccessTokenLifetime(issued)),
            // 180 天刷新窗口从这里开始，轮换不会移动它。
            RefreshTokenIssuedAt = authorizedAt
        };
        await tokenStore.SaveAsync(authorized, cancellationToken).ConfigureAwait(false);
        SetSession(authorized);
        await InitializeAccountDataWithRetryAsync(cancellationToken).ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        if (IsSignedIn)
        {
            var client = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, SectlAuthEndpoints.LogoutUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token!.AccessToken);
            try
            {
                await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // 登出以本地为准：服务端不可达也必须能退出账号。
            }
        }

        ClearSession(SectlAuthEndpoints.SignedOutReason, requiresReauthorization: false);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<HttpResponseMessage> SendAuthorizedAsync(
        Func<HttpRequestMessage> createRequest,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createRequest);
        var accessToken = await GetUsableAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("SECTL 账号未登录。");

        var response = await SendWithTokenAsync(createRequest, accessToken, completionOption, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        var description = await ReadErrorDescriptionAsync(response, cancellationToken).ConfigureAwait(false);
        if (IsAccessTokenRevoked(description))
        {
            // 访问令牌已被服务端吊销，刷新也不可能成功：结束本地会话，
            // 而不是去轮换一个已经死掉的 refresh token。
            logger.LogWarning("SECTL 访问令牌已被吊销，需要重新授权。");
            ClearSession(SectlAuthEndpoints.AccessTokenRevokedReason, requiresReauthorization: true);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return response;
        }

        if (!await RefreshForRejectedTokenAsync(accessToken, cancellationToken).ConfigureAwait(false))
            return response;

        var retryToken = _token?.AccessToken;
        if (string.IsNullOrWhiteSpace(retryToken))
            return response;

        response.Dispose();
        return await SendWithTokenAsync(createRequest, retryToken, completionOption, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string?> TryGetAccessTokenAsync(bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh)
            return await GetUsableAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        var outcome = await RefreshSingleFlightAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (outcome.Status == SectlRefreshStatus.Ended)
            return null;
        if (outcome.Status == SectlRefreshStatus.Succeeded)
            return _token?.AccessToken;

        // 临时性刷新失败不能丢掉一个仍然有效的令牌。
        var token = _token;
        return token is not null && !IsExpired(token) ? token.AccessToken : null;
    }

    private async Task<HttpResponseMessage> SendWithTokenAsync(Func<HttpRequestMessage> createRequest, string accessToken,
        HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        var request = createRequest();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            return await httpClientFactory.CreateClient()
                .SendAsync(request, completionOption, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            request.Dispose();
        }
    }

    public Task<bool> SendHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        return SendHeartbeatAsync(allowRefresh: true, cancellationToken);
    }

    private async Task<bool> SendHeartbeatAsync(bool allowRefresh, CancellationToken cancellationToken)
    {
        string? accessToken = await GetUsableAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
            return false;

        using var request = new HttpRequestMessage(HttpMethod.Post, SectlAuthEndpoints.HeartbeatUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await httpClientFactory.CreateClient()
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var description = await ReadErrorDescriptionAsync(response, cancellationToken).ConfigureAwait(false);
            if (IsAccessTokenRevoked(description))
            {
                logger.LogWarning("SECTL 访问令牌已被吊销，需要重新授权。");
                ClearSession(SectlAuthEndpoints.AccessTokenRevokedReason, requiresReauthorization: true);
                StateChanged?.Invoke(this, EventArgs.Empty);
                return false;
            }

            if (allowRefresh && await RefreshForRejectedTokenAsync(accessToken, cancellationToken).ConfigureAwait(false))
                return await SendHeartbeatAsync(allowRefresh: false, cancellationToken).ConfigureAwait(false);

            return false;
        }

        return response.IsSuccessStatusCode;
    }

    private async Task<SectlUser?> GetUserInfoAsync(CancellationToken cancellationToken, bool allowRefresh = true)
    {
        if (!IsSignedIn) return null;

        return await GetOAuthUserInfoAsync(cancellationToken, allowRefresh).ConfigureAwait(false);
    }

    private async Task<SectlUser?> GetOAuthUserInfoAsync(CancellationToken cancellationToken, bool allowRefresh)
    {
        if (!IsSignedIn) return null;
        var accessToken = await GetUsableAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken)) return null;

        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, SectlAuthEndpoints.UserInfoUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var description = await ReadErrorDescriptionAsync(response, cancellationToken).ConfigureAwait(false);
            if (IsAccessTokenRevoked(description))
            {
                ClearSession(SectlAuthEndpoints.AccessTokenRevokedReason, requiresReauthorization: true);
                StateChanged?.Invoke(this, EventArgs.Empty);
                return null;
            }

            if (allowRefresh && await RefreshForRejectedTokenAsync(accessToken, cancellationToken).ConfigureAwait(false))
                return await GetOAuthUserInfoAsync(cancellationToken, allowRefresh: false).ConfigureAwait(false);
        }

        if (!response.IsSuccessStatusCode) return null;

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return SectlUser.TryParse(payload);
    }

    /// <summary>
    ///     返回一个「还没有临近过期」的访问令牌。落在提前刷新窗口内的令牌会先刷新；
    ///     临时性刷新失败时，只要当前令牌仍然有效就继续返回它——短暂故障不会危害会话。
    /// </summary>
    private async Task<string?> GetUsableAccessTokenAsync(CancellationToken cancellationToken)
    {
        var token = _token;
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            return null;
        if (!IsExpiringWithin(token, AccessTokenRefreshLead))
            return token.AccessToken;

        var outcome = await RefreshSingleFlightAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (outcome.Status == SectlRefreshStatus.Succeeded)
            return _token?.AccessToken;
        if (outcome.Status == SectlRefreshStatus.Ended)
            return null;
        return IsExpired(token) ? null : token.AccessToken;
    }

    /// <summary>
    ///     在服务端拒绝了一个尚未到达本地过期时刻的访问令牌后刷新
    ///     （服务端可能提前吊销）。如果另一个调用方已经替换了该令牌，则不刷新。
    /// </summary>
    private async Task<bool> RefreshForRejectedTokenAsync(string rejectedAccessToken,
        CancellationToken cancellationToken)
    {
        // 并发的终态结果已经清空会话，此时没有任何可用令牌可以重试。
        if (string.IsNullOrWhiteSpace(_token?.AccessToken))
            return false;
        if (!string.Equals(_token.AccessToken, rejectedAccessToken, StringComparison.Ordinal))
            return true;

        var outcome = await RefreshSingleFlightAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        return outcome.Status == SectlRefreshStatus.Succeeded
               && !string.IsNullOrWhiteSpace(_token?.AccessToken)
               && !string.Equals(_token.AccessToken, rejectedAccessToken, StringComparison.Ordinal);
    }

    /// <summary>
    ///     把所有并发刷新合并成一次单飞。共享的那次刷新刻意不携带调用方的取消令牌：
    ///     一个调用方放弃，不能中止其他调用方正在等待的轮换；各自的取消只在等待处体现。
    /// </summary>
    private Task<SectlRefreshOutcome> RefreshSingleFlightAsync()
    {
        lock (_refreshGate)
        {
            if (_inflightRefresh is { IsCompleted: false } running)
                return running;

            var flight = RefreshAsync();
            _inflightRefresh = flight;
            return flight;
        }
    }

    private async Task<SectlRefreshOutcome> RefreshAsync()
    {
        try
        {
            return await RefreshCoreAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "SECTL 令牌刷新出现未预期的错误。");
            return SectlRefreshOutcome.Unavailable("unexpected_error", retryable: false);
        }
        finally
        {
            lock (_refreshGate)
            {
                _inflightRefresh = null;
            }
        }
    }

    private async Task<SectlRefreshOutcome> RefreshCoreAsync()
    {
        var current = _token;
        var refreshToken = current?.RefreshToken;
        if (current is null || string.IsNullOrWhiteSpace(refreshToken))
        {
            // 没有 refresh token 就没有可轮换的东西，用户必须重新授权。
            logger.LogWarning("SECTL 本地令牌缺少 refresh token，需要重新授权。");
            ClearSession(SectlAuthEndpoints.RefreshTokenMissingErrorCode, requiresReauthorization: true);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return SectlRefreshOutcome.Ended(SectlAuthEndpoints.RefreshTokenMissingErrorCode);
        }

        if (current.RefreshTokenIssuedAt is { } issuedAt && DateTimeOffset.UtcNow - issuedAt >= RefreshTokenLifetime)
        {
            logger.LogWarning("SECTL refresh token 已满 180 天，无法再轮换，需要重新授权。");
            ClearSession(SectlAuthEndpoints.RefreshTokenExpiredErrorCode, requiresReauthorization: true);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return SectlRefreshOutcome.Ended(SectlAuthEndpoints.RefreshTokenExpiredErrorCode);
        }

        var sessionVersion = Volatile.Read(ref _sessionVersion);

        // 刷新会轮换一个一次性令牌，因此整个「读取→轮换→写回」序列持有跨进程锁：
        // 另一个窗口的进程绝不能把同一个令牌轮换两次。
        var fileLock = await tokenStore.TryAcquireLockAsync(RefreshLockTimeout, CancellationToken.None)
            .ConfigureAwait(false);
        try
        {
            if (fileLock is null)
                logger.LogDebug("SECTL 刷新锁未取得，改用进程内单飞与磁盘重读保证一致性。");

            // 等待期间另一个进程可能已经轮换过令牌对。采用磁盘上已有的令牌，
            // 正是「网络结果未知」的恢复方式。
            if (await AdoptRotatedTokenAsync(refreshToken).ConfigureAwait(false) is { } adopted)
                return adopted;

            for (var attempt = 1; ; attempt++)
            {
                var outcome = await SendRefreshRequestAsync(refreshToken).ConfigureAwait(false);
                if (outcome.Status == SectlRefreshStatus.Succeeded && outcome.Token is { } rotated)
                {
                    if (sessionVersion != Volatile.Read(ref _sessionVersion))
                        return SectlRefreshOutcome.Ended("session_changed");

                    await ApplyRotatedTokenAsync(rotated, current).ConfigureAwait(false);
                    return SectlRefreshOutcome.Success();
                }

                if (outcome.Status == SectlRefreshStatus.Ended)
                {
                    logger.LogWarning(
                        "SECTL refresh token 被服务端终态拒绝（{Code}）：{Description}；停止重试并要求重新授权。",
                        outcome.ErrorCode, outcome.Description);
                    ClearSession(outcome.ErrorCode ?? SectlAuthEndpoints.InvalidGrantErrorCode,
                        requiresReauthorization: true);
                    StateChanged?.Invoke(this, EventArgs.Empty);
                    return outcome;
                }

                var attemptLimit = outcome.NetworkUnknown ? 2 : 3;
                if (!outcome.Retryable || attempt >= attemptLimit)
                {
                    logger.LogWarning("SECTL 令牌刷新暂时不可用（{Code}），保留本地会话并等待下次重试。",
                        outcome.ErrorCode);
                    return outcome;
                }

                var delay = ComputeBackoff(outcome, attempt);
                logger.LogDebug("SECTL 令牌刷新暂不可用（{Code}），{DelayMs}ms 后重试第 {Attempt} 次。",
                    outcome.ErrorCode, (int)delay.TotalMilliseconds, attempt + 1);
                await Task.Delay(delay, CancellationToken.None).ConfigureAwait(false);

                if (await AdoptRotatedTokenAsync(refreshToken).ConfigureAwait(false) is { } rotatedAfterBackoff)
                    return rotatedAfterBackoff;
            }
        }
        finally
        {
            fileLock?.Dispose();
        }
    }

    /// <summary>
    ///     采用另一个进程写入磁盘的更新令牌对，避免两个进程先后轮换同一个一次性 refresh token。
    /// </summary>
    private async Task<SectlRefreshOutcome?> AdoptRotatedTokenAsync(string usedRefreshToken)
    {
        var stored = await tokenStore.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        if (stored is null || string.IsNullOrWhiteSpace(stored.AccessToken))
            return null;
        if (string.IsNullOrWhiteSpace(stored.RefreshToken)
            || string.Equals(stored.RefreshToken, usedRefreshToken, StringComparison.Ordinal))
            return null;

        SetSession(stored);
        logger.LogInformation("检测到其他进程已完成 SECTL 令牌轮换，复用磁盘上的新令牌。");
        return SectlRefreshOutcome.Success();
    }

    private async Task<SectlRefreshOutcome> SendRefreshRequestAsync(string refreshToken)
    {
        using var timeout = new CancellationTokenSource(RefreshRequestTimeout);
        try
        {
            var client = httpClientFactory.CreateClient();
            var payload = new
            {
                grant_type = SectlAuthEndpoints.RefreshTokenGrantType,
                refresh_token = refreshToken,
                client_id = SectlAuthEndpoints.ClientId,
                device_uuid = deviceIdProvider.GetOrCreate().ToString()
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, SectlAuthEndpoints.RefreshUrl)
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };
            request.Headers.UserAgent.ParseAdd(SectlAuthEndpoints.BuildUserAgent());
            using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return ClassifyRefreshResponse(response, body);
        }
        catch (OperationCanceledException)
        {
            // 刷新请求超时：服务端可能已经轮换、也可能没有，调用方据此重读令牌文件。
            return SectlRefreshOutcome.NetworkFailure("timeout");
        }
        catch (HttpRequestException exception)
        {
            return SectlRefreshOutcome.NetworkFailure($"network_error: {exception.Message}");
        }
    }

    private static SectlRefreshOutcome ClassifyRefreshResponse(HttpResponseMessage response, string body)
    {
        var (code, description, retryAfter) = ParseOAuthError(body, response);
        if (response.IsSuccessStatusCode)
        {
            var token = TryDeserializeToken(body);
            return token is null || string.IsNullOrWhiteSpace(token.AccessToken)
                ? SectlRefreshOutcome.Unavailable("invalid_response")
                : SectlRefreshOutcome.Success(token);
        }

        return response.StatusCode switch
        {
            // 400 invalid_grant 表示已保存的 refresh token 已被使用、撤销或过期。
            // 重试同一个值永远不可能成功，而且正是这种重试造成了服务端的重用检测风暴，
            // 因此会话在这里结束。
            HttpStatusCode.BadRequest => SectlRefreshOutcome.Ended(
                code ?? SectlAuthEndpoints.InvalidGrantErrorCode, description),
            HttpStatusCode.Unauthorized => SectlRefreshOutcome.Ended(
                code ?? SectlAuthEndpoints.InvalidClientErrorCode, description),
            HttpStatusCode.TooManyRequests => SectlRefreshOutcome.RateLimited(
                code ?? SectlAuthEndpoints.RateLimitedErrorCode, retryAfter),
            HttpStatusCode.ServiceUnavailable => SectlRefreshOutcome.ServiceUnavailable(
                code ?? SectlAuthEndpoints.TemporarilyUnavailableErrorCode, retryAfter),
            _ when (int)response.StatusCode >= 500 => SectlRefreshOutcome.ServiceUnavailable(
                code ?? $"http_{(int)response.StatusCode}", retryAfter),
            _ => SectlRefreshOutcome.Unavailable(code ?? $"http_{(int)response.StatusCode}")
        };
    }

    private async Task ApplyRotatedTokenAsync(SectlToken rotated, SectlToken previous)
    {
        var applied = rotated with
        {
            // 轮换总会返回新的 refresh token；如果旧版服务端漏发，沿用上一个以保住会话。
            RefreshToken = string.IsNullOrWhiteSpace(rotated.RefreshToken) ? previous.RefreshToken : rotated.RefreshToken,
            UserId = rotated.UserId ?? previous.UserId,
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(ResolveAccessTokenLifetime(rotated)),
            RefreshTokenIssuedAt = previous.RefreshTokenIssuedAt
        };

        // 先落盘再用：刷新令牌一次性使用，进程若在这两步之间崩溃，
        // 旧令牌已失效而新令牌尚未保存。
        try
        {
            await tokenStore.SaveAsync(applied).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 服务端已经完成轮换，此时拒绝新令牌对同样会让本进程丢掉会话；
            // 先在内存里继续使用，之后再尝试写入。
            logger.LogError(exception, "SECTL 新令牌写入磁盘失败，本次仅在内存中继续使用。");
        }

        SetSession(applied);
        logger.LogInformation("SECTL 访问令牌已刷新并写入磁盘。");
    }

    private void SetSession(SectlToken token)
    {
        _token = token;
        RequiresReauthorization = false;
        Interlocked.Increment(ref _sessionVersion);
    }

    private void ClearSession(string reason, bool requiresReauthorization)
    {
        var wasSignedIn = _token is not null;
        Interlocked.Increment(ref _sessionVersion);
        _token = null;
        User = null;
        AvatarBytes = null;
        RequiresReauthorization = requiresReauthorization;
        tokenStore.Delete();
        if (wasSignedIn)
            logger.LogInformation("SECTL 会话已结束：{Reason}。", reason);
    }

    private static TimeSpan ComputeBackoff(SectlRefreshOutcome outcome, int attempt)
    {
        if (outcome.RetryAfter is { } hinted && hinted > TimeSpan.Zero)
            return hinted > MaximumBackoff ? MaximumBackoff : hinted;

        var baseDelay = outcome.ErrorCode == SectlAuthEndpoints.RateLimitedErrorCode
            ? TimeSpan.FromSeconds(1)
            : TimeSpan.FromMilliseconds(500);
        var scaled = baseDelay * Math.Pow(2, attempt - 1) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
        return scaled > MaximumBackoff ? MaximumBackoff : scaled;
    }

    private static SectlToken? TryDeserializeToken(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<SectlToken>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (string? Code, string? Description, TimeSpan? RetryAfter) ParseOAuthError(
        string? body, HttpResponseMessage? response = null)
    {
        string? code = null;
        string? description = null;
        TimeSpan? retryAfter = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    code = ReadString(document.RootElement, "error") ?? ReadString(document.RootElement, "code");
                    description = ReadString(document.RootElement, "error_description")
                                  ?? ReadString(document.RootElement, "message");
                    if (document.RootElement.TryGetProperty("retry_after_ms", out var retryMilliseconds)
                        && retryMilliseconds.ValueKind == JsonValueKind.Number
                        && retryMilliseconds.TryGetDouble(out var value)
                        && value > 0)
                        retryAfter = TimeSpan.FromMilliseconds(value);
                }
            }
            catch (JsonException)
            {
                description = Truncate(body.Trim(), 200);
            }
        }

        if (retryAfter is null && response is not null)
        {
            var header = response.Headers.RetryAfter;
            if (header?.Delta is { } delta && delta > TimeSpan.Zero)
                retryAfter = delta;
            else if (header?.Date is { } date)
                retryAfter = date - DateTimeOffset.UtcNow;
        }

        return (code, description, retryAfter);
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength] + "…";

    private static bool IsAccessTokenRevoked(string? description) =>
        description is not null && description.Contains("revoked", StringComparison.OrdinalIgnoreCase);

    private static int ResolveAccessTokenLifetime(SectlToken token) =>
        token.ExpiresIn > 0 ? token.ExpiresIn : DefaultAccessTokenLifetimeSeconds;

    private static bool IsExpiringWithin(SectlToken token, TimeSpan lead) =>
        token.AccessTokenExpiresAt is { } expiresAt && expiresAt - DateTimeOffset.UtcNow <= lead;

    private static bool IsExpired(SectlToken token) =>
        token.AccessTokenExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow;

    /// <summary>
    ///     读取被拒绝请求的 OAuth 错误体。响应内容会被缓冲，因此保留响应的调用方之后仍能读取它。
    /// </summary>
    private static async Task<string?> ReadErrorDescriptionAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var (_, description, _) = ParseOAuthError(body, response);
            return description;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    ///     下载头像。只接受与 <see cref="SectlAuthEndpoints.ApiBaseUrl" /> 同源、且不含凭据的
    ///     HTTPS 地址，并刻意不携带 Authorization 头，避免令牌被第三方地址带走。
    /// </summary>
    private async Task<byte[]?> GetAvatarBytesAsync(string? avatarUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl)
            || !Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.GetLeftPart(UriPartial.Authority), SectlAuthEndpoints.ApiBaseUrl,
                StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo))
            return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>一次刷新尝试的结果，包括重试它是否有意义。</summary>
    private enum SectlRefreshStatus
    {
        Succeeded,

        /// <summary>被限流或服务暂时不可用：退避后用同一个 refresh token 重试。</summary>
        Transient,

        /// <summary>凭据被永久拒绝：任何重试都不可能成功。</summary>
        Ended
    }

    private readonly record struct SectlRefreshOutcome(
        SectlRefreshStatus Status,
        SectlToken? Token = null,
        string? ErrorCode = null,
        string? Description = null,
        bool Retryable = false,
        bool NetworkUnknown = false,
        TimeSpan? RetryAfter = null)
    {
        public static SectlRefreshOutcome Success(SectlToken? token = null) =>
            new(SectlRefreshStatus.Succeeded, Token: token);

        public static SectlRefreshOutcome Ended(string code, string? description = null) =>
            new(SectlRefreshStatus.Ended, ErrorCode: code, Description: description);

        public static SectlRefreshOutcome RateLimited(string code, TimeSpan? retryAfter) =>
            new(SectlRefreshStatus.Transient, ErrorCode: code, Retryable: true, RetryAfter: retryAfter);

        public static SectlRefreshOutcome ServiceUnavailable(string code, TimeSpan? retryAfter) =>
            new(SectlRefreshStatus.Transient, ErrorCode: code, Retryable: true, RetryAfter: retryAfter);

        public static SectlRefreshOutcome Unavailable(string code, bool retryable = true) =>
            new(SectlRefreshStatus.Transient, ErrorCode: code, Retryable: retryable);

        /// <summary>
        ///     请求没有产生可用答复（超时或断连）。服务端可能已经完成轮换，
        ///     因此调用方在把它当成失败之前会先重读令牌文件。
        /// </summary>
        public static SectlRefreshOutcome NetworkFailure(string code) =>
            new(SectlRefreshStatus.Transient, ErrorCode: code, Retryable: true, NetworkUnknown: true);
    }
}
