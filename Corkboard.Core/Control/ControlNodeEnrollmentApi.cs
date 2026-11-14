using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Corkboard.Core.ControlCenter;

/// <summary>
///     接入（<c>POST /control/api/enroll</c>）的结果。
///     <para>
///         <see cref="ErrorKey" /> 是**本地化键**，不是给人看的中文文案：
///         UI 拿它去 resx 里取词（键由下一波补），Core 里不出现中文字面量。
///     </para>
/// </summary>
/// <param name="Ok">是否接入成功。</param>
/// <param name="Credential">成功时拿到的设备凭证。</param>
/// <param name="ErrorKey">失败时的本地化错误键。</param>
/// <param name="Message">服务端给的人话（只进日志 / 兜底展示）。</param>
/// <param name="ConflictSiteName">设备冲突时，服务端说这台机器已经接在哪个地点。</param>
public sealed record ControlEnrollResult(
    bool Ok,
    ControlNodeCredential? Credential,
    string? ErrorKey,
    string? Message,
    string? ConflictSiteName = null)
{
    /// <summary>成功。</summary>
    public static ControlEnrollResult Success(ControlNodeCredential credential) => new(true, credential, null, null);

    /// <summary>失败。</summary>
    public static ControlEnrollResult Failure(string errorKey, string? message = null, string? conflictSiteName = null) =>
        new(false, null, errorKey, message, conflictSiteName);
}

/// <summary>读设备结构树的结果。</summary>
/// <param name="Ok">是否成功。</param>
/// <param name="Grades">年级 → 班级（失败时为空）。</param>
/// <param name="ErrorKey">失败时的本地化错误键。</param>
/// <param name="Message">服务端给的人话。</param>
/// <param name="SiteId">地点 id。</param>
/// <param name="SiteName">地点名。</param>
public sealed record ControlDeviceTreeResult(
    bool Ok,
    IReadOnlyList<ControlGradeNode> Grades,
    string? ErrorKey,
    string? Message,
    string? SiteId = null,
    string? SiteName = null)
{
    /// <summary>成功。</summary>
    public static ControlDeviceTreeResult Success(
        IReadOnlyList<ControlGradeNode> grades,
        string? siteId = null,
        string? siteName = null) => new(true, grades, null, null, siteId, siteName);

    /// <summary>失败。</summary>
    public static ControlDeviceTreeResult Failure(string errorKey, string? message = null) =>
        new(false, [], errorKey, message);
}

/// <summary>接入失败的本地化键（下一波在 resx 里补中文；测试直接比常量）。</summary>
public static class ControlEnrollErrorKeys
{
    /// <summary>键前缀。</summary>
    public const string Prefix = "Settings_Control_Enroll_Error_";

    /// <summary>网络不通 / 超时。</summary>
    public const string Network = Prefix + "Network";

    /// <summary>服务端回的东西认不出来。</summary>
    public const string InvalidResponse = Prefix + "InvalidResponse";

    /// <summary>请求本身不合法。</summary>
    public const string BadRequest = Prefix + "BadRequest";

    /// <summary>地点不存在。</summary>
    public const string SiteNotFound = Prefix + "SiteNotFound";

    /// <summary>配对码不存在。</summary>
    public const string CodeNotFound = Prefix + "CodeNotFound";

    /// <summary>配对码与地点不匹配。</summary>
    public const string CodeSiteMismatch = Prefix + "CodeSiteMismatch";

    /// <summary>配对码已过期。</summary>
    public const string CodeExpired = Prefix + "CodeExpired";

    /// <summary>配对码已被用过（一次性）。</summary>
    public const string CodeUsed = Prefix + "CodeUsed";

    /// <summary>配对码已被吊销。</summary>
    public const string CodeRevoked = Prefix + "CodeRevoked";

    /// <summary>平台不被允许接入。</summary>
    public const string PlatformNotAllowed = Prefix + "PlatformNotAllowed";

    /// <summary>这台机器已经接在别的地点。</summary>
    public const string DeviceConflict = Prefix + "DeviceConflict";

    /// <summary>尝试次数过多，稍后再试。</summary>
    public const string TooManyRequests = Prefix + "TooManyRequests";

    /// <summary>输入的配对码格式不对（客户端本地就能判出来）。</summary>
    public const string CodeInvalid = Prefix + "CodeInvalid";

    /// <summary>本地没启用集控。</summary>
    public const string Disabled = Prefix + "Disabled";

    /// <summary>认不出来的错误。</summary>
    public const string Unknown = Prefix + "Unknown";
}

/// <summary>设备自助接口失败的本地化键。</summary>
public static class ControlApiErrorKeys
{
    /// <summary>键前缀。</summary>
    public const string Prefix = "Settings_Control_Error_";

    /// <summary>网络不通 / 超时。</summary>
    public const string Network = Prefix + "Network";

    /// <summary>服务端回的东西认不出来。</summary>
    public const string InvalidResponse = Prefix + "InvalidResponse";

    /// <summary>还没接入（没有凭证）。</summary>
    public const string NotEnrolled = Prefix + "NotEnrolled";

    /// <summary>认不出来的错误。</summary>
    public const string Unknown = Prefix + "Unknown";
}

/// <summary>
///     集控中心的两类 HTTP 接口：双因子接入与设备自助读结构。
///     <para>
///         用 <see cref="HttpClient" /> 而不是自己拼 socket：站点可能挂在反代后面，
///         这里能直接吃系统代理与 TLS 配置。可注入 <see cref="HttpMessageHandler" /> 便于单测。
///     </para>
/// </summary>
public sealed class ControlNodeEnrollmentApi : IDisposable
{
    private const int DefaultTimeoutSeconds = 15;

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly ControlNodeConnectOptions _options;

    /// <summary>
    ///     建一个 HTTP 客户端封装。
    /// </summary>
    /// <param name="options">连接参数（站点地址、平台标识）。</param>
    /// <param name="httpClient">注入的 <see cref="HttpClient" />（测试用）；<c>null</c> 时自己建一个。</param>
    public ControlNodeEnrollmentApi(ControlNodeConnectOptions? options = null, HttpClient? httpClient = null)
    {
        _options = options ?? new ControlNodeConnectOptions();
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds) };
    }

    /// <summary>
    ///     用「地点ID + 配对码」换设备凭证。
    ///     <para>
    ///         传进来的两个参数必须**已经归一化**（<see cref="NodeEnrollment.NormalizeSiteId" /> /
    ///         <see cref="NodeEnrollment.NormalizePairCode" />），这里只负责发请求与认错误码。
    ///     </para>
    /// </summary>
    public async Task<ControlEnrollResult> EnrollAsync(
        string siteId,
        string pairCode,
        string? deviceName,
        IReadOnlyList<string> capabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pairCode);
        ArgumentNullException.ThrowIfNull(capabilities);

        var payload = new JsonObject
        {
            ["siteId"] = siteId,
            ["code"] = pairCode,
            ["platform"] = _options.Platform,
            ["capabilities"] = ToJsonArray(capabilities)
            // 不报 machine：Corkboard 没有稳定的机器指纹来源（跨平台拿不到机器 GUID），只报主机名会让
            // 服务端按 `guid|host` 的哈希跨地点查「同一台机器」冲突；guid 为空时指纹只剩主机名，
            // 校园镜像克隆出来的同名机器在另一个地点接入就会被误判成 device_conflict。
            // 宁可少一条「这台机器已在别的地点」的提示，也不能凭空挡住正常接入（服务端对空 machine 不做该检查）。
        };
        if (!string.IsNullOrWhiteSpace(deviceName))
            payload["deviceName"] = deviceName.Trim();

        var (responseOk, root, failureKey) = await PostJsonAsync(
            _options.ResolveHttpEndpoint(ControlV4Protocol.EnrollPath),
            payload,
            ControlEnrollErrorKeys.Network,
            ControlEnrollErrorKeys.InvalidResponse,
            cancellationToken).ConfigureAwait(false);

        if (root is null)
            return ControlEnrollResult.Failure(failureKey ?? ControlEnrollErrorKeys.InvalidResponse);

        if (!responseOk)
        {
            var code = ReadString(root, "code");
            var message = ReadString(root, "message");
            var conflictSiteName = ReadNestedString(root, "detail", "siteName");
            return ControlEnrollResult.Failure(MapEnrollErrorKey(code), message, conflictSiteName);
        }

        // 只有 enroll 把结果装进 data；仍然容忍服务端把字段平铺在顶层（参考实现就是这么兜的）。
        var data = root["data"] as JsonObject ?? root;
        var deviceId = ReadString(data, "deviceId") ?? ReadString(data, "device_id");
        var secret = ReadString(data, "secret");
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(secret))
            return ControlEnrollResult.Failure(ControlEnrollErrorKeys.InvalidResponse, "服务端没有返回 deviceId / secret");

        var site = data["site"] as JsonObject;
        var enrolledSiteId = ReadString(site, "publicId") ?? ReadString(site, "public_id") ?? siteId;
        var siteName = ReadString(site, "name");
        var classInfo = data["class"] as JsonObject;

        return ControlEnrollResult.Success(new ControlNodeCredential(
            deviceId,
            secret,
            enrolledSiteId,
            siteName,
            ReadString(classInfo, "id"),
            ReadString(classInfo, "name")));
    }

    /// <summary>
    ///     用设备凭证读结构树（年级 → 班级）。
    ///     <para>凭证走**请求体**，绝不进 URL 或 Authorization 头（会被反代日志原样记下来）。</para>
    /// </summary>
    public async Task<ControlDeviceTreeResult> GetDeviceTreeAsync(
        ControlNodeCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (!credential.IsComplete)
            return ControlDeviceTreeResult.Failure(ControlApiErrorKeys.NotEnrolled);

        var payload = new JsonObject
        {
            ["deviceId"] = credential.DeviceId,
            ["secret"] = credential.Secret
        };

        var (responseOk, root, failureKey) = await PostJsonAsync(
            _options.ResolveHttpEndpoint(ControlV4Protocol.DeviceTreePath),
            payload,
            ControlApiErrorKeys.Network,
            ControlApiErrorKeys.InvalidResponse,
            cancellationToken).ConfigureAwait(false);

        if (root is null)
            return ControlDeviceTreeResult.Failure(failureKey ?? ControlApiErrorKeys.InvalidResponse);

        if (!responseOk)
            return ControlDeviceTreeResult.Failure(ControlApiErrorKeys.Unknown, ReadString(root, "message"));

        var data = root["data"] as JsonObject ?? root;
        ControlTreePayload? tree;
        try
        {
            tree = data.Deserialize<ControlTreePayload>(ControlV4Protocol.JsonOptions);
        }
        catch (JsonException ex)
        {
            return ControlDeviceTreeResult.Failure(ControlApiErrorKeys.InvalidResponse, ex.Message);
        }

        if (tree is null)
            return ControlDeviceTreeResult.Failure(ControlApiErrorKeys.InvalidResponse);

        return ControlDeviceTreeResult.Success(
            tree.ToGradeNodes(),
            tree.Site?.PublicId,
            tree.Site?.Name);
    }

    /// <summary>释放自己建的 <see cref="HttpClient" />（注入进来的不归我管）。</summary>
    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }

    /// <summary>发一个 JSON POST。返回「HTTP 成功 + 解析出的根对象」，失败时给本地化键。</summary>
    private async Task<(bool ResponseOk, JsonObject? Root, string? FailureKey)> PostJsonAsync(
        Uri uri,
        JsonObject payload,
        string networkErrorKey,
        string invalidResponseKey,
        CancellationToken cancellationToken)
    {
        string text;
        HttpStatusCode statusCode;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(payload.ToJsonString(ControlV4Protocol.JsonOptions),
                    Encoding.UTF8, "application/json")
            };
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            statusCode = response.StatusCode;
            text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout 到点也抛 OperationCanceledException（没带取消令牌），按网络失败处理。
            return (false, null, networkErrorKey);
        }
        catch (HttpRequestException)
        {
            return (false, null, networkErrorKey);
        }

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            root = null;
        }

        if (root is null)
            return (false, null, invalidResponseKey);

        var ok = ReadBool(root, "ok") ?? IsSuccessStatusCode(statusCode);
        return (ok, root, null);
    }

    private static bool IsSuccessStatusCode(HttpStatusCode statusCode) =>
        (int)statusCode is >= 200 and <= 299;

    private static JsonArray ToJsonArray(IReadOnlyList<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                array.Add(value);
        }

        return array;
    }

    private static string MapEnrollErrorKey(string? code) => code switch
    {
        "bad_request" or "invalid_request" => ControlEnrollErrorKeys.BadRequest,
        "site_not_found" => ControlEnrollErrorKeys.SiteNotFound,
        "code_not_found" => ControlEnrollErrorKeys.CodeNotFound,
        "code_site_mismatch" => ControlEnrollErrorKeys.CodeSiteMismatch,
        "code_expired" => ControlEnrollErrorKeys.CodeExpired,
        "code_used" => ControlEnrollErrorKeys.CodeUsed,
        "code_revoked" => ControlEnrollErrorKeys.CodeRevoked,
        "platform_not_allowed" => ControlEnrollErrorKeys.PlatformNotAllowed,
        "device_conflict" => ControlEnrollErrorKeys.DeviceConflict,
        "too_many_requests" => ControlEnrollErrorKeys.TooManyRequests,
        "invalid_code" => ControlEnrollErrorKeys.CodeInvalid,
        _ => ControlEnrollErrorKeys.Unknown
    };

    private static bool? ReadBool(JsonObject? obj, string name) => ControlJson.ReadBool(obj, name);

    private static string? ReadString(JsonObject? obj, string name) => ControlJson.ReadString(obj, name);

    private static string? ReadNestedString(JsonObject? obj, string parent, string name) =>
        obj?[parent] is JsonObject child ? ReadString(child, name) : null;
}
