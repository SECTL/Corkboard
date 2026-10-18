using Corkboard.Core;

namespace Corkboard.Services.Auth;

/// <summary>
///     SECTL 账号体系（OAuth 2.0 + PKCE）的全部外部端点、客户端标识与协议常量。
/// </summary>
/// <remarks>
///     本类型是该项目里唯一允许出现 SECTL 地址与客户端 ID 的地方：服务实现、令牌存储、
///     心跳与重定向代理都必须从这里取值，便于在 SECTL 侧换环境或换客户端时只改一处。
/// </remarks>
public static class SectlAuthEndpoints
{
    /// <summary>
    ///     桌面端 OAuth 客户端 ID（SECTL 侧已注册），也就是平台标识
    ///     <see cref="GlobalConstants.PlatformId" />。
    /// </summary>
    /// <remarks>
    ///     SECTL 侧平台记录的主键同时用于 OAuth 与统计（在线状态、使用量、版本上报），
    ///     所以这里直接取 <see cref="GlobalConstants.PlatformId" />，**不要写第二份字面量**。
    ///     客户端注册时需登记桌面端的 loopback 重定向地址
    ///     （<c>http://localhost:&lt;动态端口&gt;/callback</c>）。
    /// </remarks>
    public const string ClientId = GlobalConstants.PlatformId;

    /// <summary>SECTL API 基址，同时是允许下载头像的唯一来源。</summary>
    public const string ApiBaseUrl = "https://appwrite.sectl.cn";

    /// <summary>授权页所在站点（浏览器打开的地址）。</summary>
    public const string BrowserBaseUrl = "https://sectl.cn";

    /// <summary>发起授权（<c>response_type=code</c>）的地址。</summary>
    public const string AuthorizeUrl = $"{BrowserBaseUrl}/oauth/authorize";

    /// <summary>用授权码换取令牌的地址。</summary>
    public const string TokenUrl = $"{ApiBaseUrl}/api/oauth/token";

    /// <summary>用 refresh token 轮换令牌的地址。</summary>
    public const string RefreshUrl = $"{ApiBaseUrl}/api/oauth/refresh";

    /// <summary>结束会话的地址。</summary>
    public const string LogoutUrl = $"{ApiBaseUrl}/api/oauth/logout";

    /// <summary>账号资料（含头像地址）的地址。</summary>
    public const string UserInfoUrl = $"{ApiBaseUrl}/api/oauth/userinfo";

    /// <summary>设备活跃上报地址（账号体系的一部分，无业务负载）。</summary>
    public const string HeartbeatUrl = $"{ApiBaseUrl}/api/oauth/heartbeat";

    /// <summary>版本使用人数上报地址（服务端统计，载荷只含平台标识、版本与设备标识）。</summary>
    public const string VersionReportUrl = $"{ApiBaseUrl}/api/stats/version";

    // ---------------------------------------------------------------- 云存储（云同步用）

    /// <summary>云存储：文件列表（<c>GET</c>，带 <c>client_id</c>）。</summary>
    public const string CloudFilesUrl = $"{ApiBaseUrl}/api/cloud/files";

    /// <summary>云存储：上传文件（<c>POST multipart/form-data</c>）。</summary>
    public const string CloudUploadUrl = $"{ApiBaseUrl}/api/cloud/upload";

    /// <summary>云存储：账号与平台的空间占用（<c>GET</c>，带 <c>client_id</c>）。</summary>
    public const string CloudUsageUrl = $"{ApiBaseUrl}/api/cloud/storage/usage";

    /// <summary>云存储：单个文件的删除地址（<c>DELETE</c>，JSON 体里带 <c>client_id</c>）。</summary>
    public static string CloudFileUrl(string fileId) => $"{CloudFilesUrl}/{Uri.EscapeDataString(fileId)}";

    /// <summary>云存储：换临时下载地址（<c>GET</c>，返回体里的 <c>download_url</c> 才带文件内容）。</summary>
    public static string CloudFileDownloadUrl(string fileId) => $"{CloudFileUrl(fileId)}/download";

    // ---------------------------------------------------------------- 平台统计（在线状态 / 使用量）

    /// <summary>在线状态上报地址（<c>POST</c>，载荷 <c>platform_id</c> / <c>device_uuid</c> / <c>device_type</c>）。</summary>
    public const string OnlineReportUrl = $"{ApiBaseUrl}/api/stats/online";

    /// <summary>在线人数查询地址（<c>GET</c>，返回 <c>success</c> / <c>online_count</c> / <c>error</c>）。</summary>
    public static string OnlineStatsUrl(string platformId) =>
        $"{ApiBaseUrl}/api/stats/platform/{Uri.EscapeDataString(platformId)}";

    /// <summary>使用量增量上报地址（<c>POST</c>，载荷 <c>platform_id</c> / <c>field_key</c> / <c>delta</c>）。</summary>
    public const string UsageIncrementUrl = $"{ApiBaseUrl}/api/stats/usage/increment";

    /// <summary>令牌文件在数据根下的相对路径段。</summary>
    public static readonly string[] TokenFileSegments = ["config", "sectl-auth.json"];

    /// <summary>设备标识文件在数据根下的相对路径段。</summary>
    public static readonly string[] DeviceIdFileSegments = ["config", "sectl-device.json"];

    // ---------------------------------------------------------------- 协议常量

    /// <summary>固定授权码模式。</summary>
    public const string ResponseType = "code";

    /// <summary>PKCE challenge 派生算法，必须与 code_challenge 的计算方式一致。</summary>
    public const string CodeChallengeMethod = "S256";

    /// <summary>授权码换令牌的 grant 类型。</summary>
    public const string AuthorizationCodeGrantType = "authorization_code";

    /// <summary>刷新令牌的 grant 类型。</summary>
    public const string RefreshTokenGrantType = "refresh_token";

    /// <summary>请求的作用域：只读账号资料，以及云端能力（云备份由上层业务使用）。</summary>
    public const string OAuthScope = "user:read cloud:read cloud:write";

    /// <summary>刷新被服务端终态拒绝时的兜底错误码。</summary>
    public const string InvalidGrantErrorCode = "invalid_grant";

    /// <summary>刷新被服务端以 401 拒绝时的兜底错误码。</summary>
    public const string InvalidClientErrorCode = "invalid_client";

    /// <summary>限流错误码。</summary>
    public const string RateLimitedErrorCode = "rate_limited";

    /// <summary>服务暂时不可用错误码。</summary>
    public const string TemporarilyUnavailableErrorCode = "temporarily_unavailable";

    /// <summary>本地判定 refresh token 缺失时的错误码。</summary>
    public const string RefreshTokenMissingErrorCode = "refresh_token_missing";

    /// <summary>本地判定 refresh token 超过有效期时的错误码。</summary>
    public const string RefreshTokenExpiredErrorCode = "refresh_token_expired";

    /// <summary>终止本地会话的原因：用户主动登出。</summary>
    public const string SignedOutReason = "signed_out";

    /// <summary>终止本地会话的原因：访问令牌被服务端吊销。</summary>
    public const string AccessTokenRevokedReason = "access_token_revoked";

    // ---------------------------------------------------------------- 产品标识（User-Agent）

    /// <summary>User-Agent 里的产品标识。</summary>
    public const string ProductName = "Corkboard";

    /// <summary>产品版本；启动时由 <c>App.BuildHost</c> 用 <c>GlobalConstants.Version</c> 覆盖。</summary>
    public static string ProductVersion { get; set; } = "0.0.0";

    /// <summary>
    ///     User-Agent 里的设备名，默认取机器名。
    ///     注重隐私的构建可在启动时把它置为 <see langword="null" /> 或空串，此时只上报产品标识。
    /// </summary>
    public static string? DeviceName { get; set; } = Environment.MachineName;

    /// <summary>构造 SECTL 请求使用的 User-Agent。</summary>
    public static string BuildUserAgent()
    {
        var product = $"{ProductName}/{ProductVersion}";
        var platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
        return string.IsNullOrWhiteSpace(DeviceName)
            ? $"{product} ({platform})"
            : $"{product} ({platform}; {DeviceName})";
    }
}
