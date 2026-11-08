using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Corkboard.Core.DutyRoster.Transfer;

/// <summary>
///     云传输信封：把整包内容用 AES-256-GCM 加密后的自描述对象。
///     字段名与上游 SecRandom 的 <c>secrandom-sync-envelope/v1</c> 逐字一致（camelCase 序列化）。
/// </summary>
public sealed record DutyTransferEnvelope(
    string Format,
    string Encryption,
    string KeyDerivation,
    int? Iterations,
    string Salt,
    string Nonce,
    string Tag,
    string Ciphertext);

/// <summary>
///     信封的封包/解包（纯字节运算，不碰网络）。
///     <para>
///         两种密钥派生，照搬上游：
///         <list type="bullet">
///             <item>
///                 <description><c>raw</c>：扫码快速传输，配对链接里直接带 32 字节密钥（fragment 不过网）。</description>
///             </item>
///             <item>
///                 <description><c>pbkdf2-sha256</c>：会话码只有 12 位，用 PBKDF2（60 万次迭代）撑强度。</description>
///             </item>
///         </list>
///         600k 次 PBKDF2 很慢（数百毫秒），调用方必须放到后台线程，别卡 UI。
///     </para>
/// </summary>
public static class DutyTransferCipher
{
    public const string FormatName = "secrandom-sync-envelope/v1";

    public const string EncryptionName = "aes-256-gcm";

    public const string RawKeyDerivation = "raw";

    public const string Pbkdf2KeyDerivation = "pbkdf2-sha256";

    public const int Pbkdf2Iterations = 600_000;

    /// <summary>GCM 密钥必须是 32 字节。</summary>
    public const int KeyLength = 32;

    private const int SaltLength = 16;

    private const int NonceLength = 12;

    private const int TagLength = 16;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>扫码快速传输：配对链接里已经带了 32 字节密钥。</summary>
    public static DutyTransferEnvelope EncryptWithRawKey(byte[] plaintext, ReadOnlySpan<byte> key)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        if (key.Length != KeyLength)
            throw new ArgumentException("快速传输密钥必须是 32 字节", nameof(key));

        return Encrypt(plaintext, key.ToArray(), RawKeyDerivation, null);
    }

    /// <summary>会话码传输：密钥由 12 位会话码经 PBKDF2 派生。</summary>
    public static DutyTransferEnvelope EncryptWithSessionCode(byte[] plaintext, string sessionCode)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var code = DutySessionCode.Normalize(sessionCode);
        if (!DutySessionCode.IsValid(code))
            throw new InvalidDataException("会话码必须是 12 位字母或数字");

        return Encrypt(plaintext, Encoding.UTF8.GetBytes(code), Pbkdf2KeyDerivation, Pbkdf2Iterations);
    }

    /// <summary>
    ///     解信封。<paramref name="keyMaterial" /> 在 <c>raw</c> 模式下是 32 字节密钥，
    ///     在 <c>pbkdf2-sha256</c> 模式下是会话码的 UTF-8 字节。
    /// </summary>
    /// <exception cref="InvalidDataException">格式不支持、密钥不对，或内容被改过（GCM tag 校验失败）。</exception>
    public static byte[] Decrypt(DutyTransferEnvelope envelope, ReadOnlySpan<byte> keyMaterial)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Format != FormatName || envelope.Encryption != EncryptionName)
            throw new InvalidDataException("传输信封的格式不支持");

        var salt = DecodeBase64Url(envelope.Salt);
        byte[] key;
        switch (envelope.KeyDerivation)
        {
            case RawKeyDerivation when keyMaterial.Length == KeyLength:
                key = keyMaterial.ToArray();
                break;
            case Pbkdf2KeyDerivation when envelope.Iterations is > 0:
                key = Rfc2898DeriveBytes.Pbkdf2(keyMaterial, salt, envelope.Iterations.Value,
                    HashAlgorithmName.SHA256, KeyLength);
                break;
            default:
                throw new InvalidDataException("传输信封的密钥类型与内容不匹配");
        }

        var ciphertext = DecodeBase64Url(envelope.Ciphertext);
        DutyTransferLimits.EnsurePayloadSize(ciphertext.LongLength, "加密传输内容");
        var nonce = DecodeBase64Url(envelope.Nonce);
        var tag = DecodeBase64Url(envelope.Tag);
        if (nonce.Length != NonceLength || tag.Length != TagLength || salt.Length != SaltLength)
            throw new InvalidDataException("传输信封的随机参数长度不对");

        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (CryptographicException)
        {
            throw new InvalidDataException("传输内容解密失败：密钥不对或内容被改动过");
        }

        DutyTransferLimits.EnsurePayloadSize(plaintext.LongLength, "解密后的传输内容");
        return plaintext;
    }

    /// <summary>信封 → JSON 请求体（camelCase，与上游一致）。</summary>
    public static byte[] Serialize(DutyTransferEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        DutyTransferLimits.EnsurePayloadSize(payload.LongLength, "加密传输信封");
        return payload;
    }

    /// <summary>JSON 请求体 → 信封；格式不认识时抛 <see cref="InvalidDataException" />。</summary>
    public static DutyTransferEnvelope Deserialize(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        DutyTransferLimits.EnsurePayloadSize(payload.LongLength, "加密传输信封");
        try
        {
            return JsonSerializer.Deserialize<DutyTransferEnvelope>(payload, JsonOptions)
                   ?? throw new InvalidDataException("传输信封是空的");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("传输信封不是有效的 JSON", exception);
        }
    }

    private static DutyTransferEnvelope Encrypt(byte[] plaintext, byte[] keyMaterial, string keyDerivation,
        int? iterations)
    {
        DutyTransferLimits.EnsurePayloadSize(plaintext.LongLength, "待加密的传输内容");

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var key = keyDerivation == RawKeyDerivation
            ? keyMaterial
            : Rfc2898DeriveBytes.Pbkdf2(keyMaterial, salt, iterations!.Value, HashAlgorithmName.SHA256, KeyLength);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        using (var aes = new AesGcm(key, TagLength))
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return new DutyTransferEnvelope(
            FormatName,
            EncryptionName,
            keyDerivation,
            iterations,
            EncodeBase64Url(salt),
            EncodeBase64Url(nonce),
            EncodeBase64Url(tag),
            EncodeBase64Url(ciphertext));
    }

    private static string EncodeBase64Url(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64Url(string? value)
    {
        if (string.IsNullOrEmpty(value))
            throw new InvalidDataException("传输信封缺少必要字段");

        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("传输信封里的 Base64Url 字段无效", exception);
        }
    }
}