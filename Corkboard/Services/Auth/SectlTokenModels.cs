using System.Text.Json;
using System.Text.Json.Serialization;

namespace Corkboard.Services.Auth;

/// <summary>
///     SECTL OAuth 令牌对。字段名与令牌端点返回的 JSON 一致，因此同一个类型既能反序列化服务端
///     响应，也能作为磁盘上的持久化结构。
/// </summary>
public sealed record SectlToken(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("user_id")] string? UserId,
    [property: JsonPropertyName("expires_in")] int ExpiresIn)
{
    /// <summary>
    ///     访问令牌的本地过期时刻，在令牌落盘时写入。
    ///     早于该字段存在的令牌文件读回后为空，此时只会在服务端拒绝后刷新，而不是提前刷新。
    /// </summary>
    [JsonPropertyName("access_token_expires_at")]
    public DateTimeOffset? AccessTokenExpiresAt { get; init; }

    /// <summary>
    ///     固定刷新窗口的起点（首次授权时刻）。轮换不会延长该窗口，因此每次轮换都原样保留。
    /// </summary>
    [JsonPropertyName("refresh_token_issued_at")]
    public DateTimeOffset? RefreshTokenIssuedAt { get; init; }
}

/// <summary>
///     账号资料。服务端可能把字段包在 <c>data</c> 里，也可能平铺在根对象上，
///     两种形态都由 <see cref="TryParse" /> 归一化。
/// </summary>
public sealed record SectlUser(
    [property: JsonPropertyName("user_id")] string? UserId,
    [property: JsonPropertyName("name")] string? UserName,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl)
{
    public string? ResolvedUserName => FirstNonBlank(UserName, Data?.UserName);
    public string? ResolvedUserId => FirstNonBlank(UserId, Data?.UserId);
    public string? ResolvedEmail => FirstNonBlank(Email, Data?.Email);
    public string? ResolvedAvatarUrl => FirstNonBlank(AvatarUrl, Data?.AvatarUrl);

    [JsonIgnore]
    public SectlUserData? Data { get; init; }

    /// <summary>解析 userinfo 响应；根节点不是对象时返回 <see langword="null" />。</summary>
    public static SectlUser? TryParse(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return null;

        var data = payload.TryGetProperty("data", out var nested) && nested.ValueKind == JsonValueKind.Object
            ? ReadFields(nested)
            : null;
        var direct = ReadFields(payload);
        return new SectlUser(direct.UserId, direct.UserName, direct.Email, direct.AvatarUrl)
        {
            Data = data
        };
    }

    private static SectlUserData ReadFields(JsonElement value) => new(
        ReadString(value, "user_id"),
        ReadString(value, "name"),
        ReadString(value, "email"),
        ReadString(value, "avatar_url"));

    private static string? ReadString(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? FirstNonBlank(property.GetString())
            : null;

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

/// <summary>userinfo 响应里嵌套的 <c>data</c> 对象。</summary>
public sealed record SectlUserData(
    [property: JsonPropertyName("user_id")] string? UserId,
    [property: JsonPropertyName("name")] string? UserName,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl);
