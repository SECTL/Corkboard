using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Corkboard.Shared;
using Microsoft.Extensions.Logging;

namespace Corkboard.Core.ControlCenter;

/// <summary>
///     设备凭证的加密落盘（AES-GCM + 独立密钥文件），照上游 SecRandom-C 的
///     <c>NodeEnrollmentStore</c> 搬过来。
///     <para>
///         位置：<c>data/config/security/control-node.json</c>（密文）与同目录 <c>control-node.key</c>（密钥）。
///         <b>凭证绝不进 settings.json</b>：设置文件会被导出、被云同步、被用户随手贴进聊天窗，
///         而 secret 一旦泄露，谁都能冒充这台教室机。
///     </para>
///     <para>
///         <b>任何读取失败（文件坏了、密钥对不上、格式版本不认识）都返回「没有凭证」</b>，
///         并把两个文件一起删掉，让用户重新输入配对码；绝不抛出、也绝不带着解不开的密文继续连
///         （那会变成「已接入但永远握手失败」的死循环）。
///     </para>
/// </summary>
public sealed class NodeEnrollmentStore : IControlNodeCredentialProvider
{
    /// <summary>密文信封格式版本（v2 = AES-GCM，AAD 绑定用途与版本）。</summary>
    public const int FormatVersion = 2;

    /// <summary>密钥文件格式版本。</summary>
    public const int KeyFormatVersion = 1;

    /// <summary>AES-256 的密钥长度。</summary>
    public const int KeyLength = 32;

    /// <summary>GCM nonce 长度（96 bit，协议与实现都按这个来）。</summary>
    public const int NonceLength = 12;

    /// <summary>GCM tag 长度。</summary>
    public const int TagLength = 16;

    /// <summary>
    ///     附加认证数据：把密文绑到「用途 + 版本」上，别的功能就算拿到同一个密钥也解不开这份文件，
    ///     改动文件名/版本号同样会导致解密失败（fail closed）。
    /// </summary>
    private static readonly byte[] AdditionalData = Encoding.UTF8.GetBytes("Corkboard/ControlNodeEnrollment/v2");

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<NodeEnrollmentStore>? _logger;
    private readonly object _gate = new();
    private ControlNodeCredential? _current;

    /// <summary>
    ///     建一个凭证存储。
    /// </summary>
    /// <param name="logger">可选日志。</param>
    /// <param name="credentialPath">自定义密文路径（测试用）。</param>
    /// <param name="keyPath">自定义密钥路径（测试用；默认与密文同目录）。</param>
    public NodeEnrollmentStore(
        ILogger<NodeEnrollmentStore>? logger = null,
        string? credentialPath = null,
        string? keyPath = null)
    {
        _logger = logger;
        CredentialPath = string.IsNullOrWhiteSpace(credentialPath)
            ? Utils.GetFilePath("config", "security", "control-node.json")
            : credentialPath;
        KeyPath = string.IsNullOrWhiteSpace(keyPath)
            ? Path.Combine(Path.GetDirectoryName(CredentialPath)!, "control-node.key")
            : keyPath;
    }

    /// <summary>密文文件路径。</summary>
    public string CredentialPath { get; }

    /// <summary>密钥文件路径。</summary>
    public string KeyPath { get; }

    /// <inheritdoc />
    public ControlNodeCredential? Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    /// <inheritdoc />
    public Task<ControlNodeCredential?> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _current = LoadCore();
            return Task.FromResult(_current);
        }
    }

    /// <inheritdoc />
    public Task SaveAsync(ControlNodeCredential credential, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (!credential.IsComplete)
            throw new ArgumentException("设备凭证不完整，拒绝落盘", nameof(credential));
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            SaveCore(credential);
            _current = credential;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            TryDelete(CredentialPath);
            TryDelete(KeyPath);
            _current = null;
        }

        return Task.CompletedTask;
    }

    private ControlNodeCredential? LoadCore()
    {
        if (!File.Exists(CredentialPath))
            return null;

        try
        {
            var key = TryReadKey();
            if (key is null)
            {
                // 有密文没密钥（或密钥坏了）：这份密文永远解不开，直接删干净让用户重新配对。
                LogUnreadable("密钥文件缺失或损坏");
                DiscardCorrupt();
                return null;
            }

            var json = File.ReadAllText(CredentialPath, Encoding.UTF8);
            var envelope = JsonSerializer.Deserialize<EnvelopeFile>(json, JsonOptions);
            if (envelope is null || envelope.FormatVersion != FormatVersion)
            {
                LogUnreadable($"信封格式版本不认识（期望 {FormatVersion}，实际 {envelope?.FormatVersion}）");
                DiscardCorrupt();
                return null;
            }

            var nonce = Convert.FromBase64String(envelope.Nonce);
            var tag = Convert.FromBase64String(envelope.Tag);
            var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
            if (nonce.Length != NonceLength || tag.Length != TagLength || ciphertext.Length == 0)
            {
                LogUnreadable("nonce/tag/ciphertext 长度不对");
                DiscardCorrupt();
                return null;
            }

            var plaintext = new byte[ciphertext.Length];
            using (var aes = new AesGcm(key, TagLength))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext, AdditionalData);
            }

            var payload = JsonSerializer.Deserialize<PayloadFile>(plaintext, JsonOptions);
            if (payload is null || string.IsNullOrWhiteSpace(payload.DeviceId) ||
                string.IsNullOrWhiteSpace(payload.Secret) || string.IsNullOrWhiteSpace(payload.SiteId))
            {
                LogUnreadable("解密后缺 device_id / secret / site_id");
                DiscardCorrupt();
                return null;
            }

            return new ControlNodeCredential(
                payload.DeviceId,
                payload.Secret,
                payload.SiteId,
                payload.SiteName,
                payload.ClassId,
                payload.ClassName)
            {
                EnrolledAt = payload.EnrolledAt == default ? DateTimeOffset.UtcNow : payload.EnrolledAt
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or FormatException or CryptographicException or NotSupportedException
                                       or ArgumentException)
        {
            // 解不开就当没有凭证：这是「密钥不匹配 / 文件被别的东西覆盖」的唯一可接受处理。
            _logger?.LogWarning(ex, "集控凭证文件无法读取，已按「未接入」处理：{Path}", CredentialPath);
            DiscardCorrupt();
            return null;
        }
    }

    private void SaveCore(ControlNodeCredential credential)
    {
        // 顺序很重要：**先确保密钥存在，再写密文**。反过来的话中途崩溃会留下
        // 「有密文、没密钥」的残局——那就等于「显示已接入、实际永远握手失败」。
        var key = EnsureKey();

        var payload = new PayloadFile
        {
            DeviceId = credential.DeviceId,
            Secret = credential.Secret,
            SiteId = credential.SiteId,
            SiteName = credential.SiteName,
            ClassId = credential.ClassId,
            ClassName = credential.ClassName,
            EnrolledAt = credential.EnrolledAt == default ? DateTimeOffset.UtcNow : credential.EnrolledAt
        };

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];

        using (var aes = new AesGcm(key, TagLength))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AdditionalData);
        }

        var envelope = new EnvelopeFile
        {
            FormatVersion = FormatVersion,
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Ciphertext = Convert.ToBase64String(ciphertext)
        };

        WriteAtomic(CredentialPath, JsonSerializer.Serialize(envelope, JsonOptions));
    }

    private byte[]? TryReadKey()
    {
        try
        {
            if (!File.Exists(KeyPath))
                return null;

            var json = File.ReadAllText(KeyPath, Encoding.UTF8);
            var file = JsonSerializer.Deserialize<KeyFile>(json, JsonOptions);
            if (file is null || file.FormatVersion != KeyFormatVersion)
                return null;

            var key = Convert.FromBase64String(file.Key);
            return key.Length == KeyLength ? key : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or FormatException or NotSupportedException or ArgumentException)
        {
            _logger?.LogWarning(ex, "集控凭证密钥文件损坏：{Path}", KeyPath);
            return null;
        }
    }

    /// <summary>读现有密钥；没有（或坏了）就生成一把新的，并且**连带删掉旧密文**。</summary>
    private byte[] EnsureKey()
    {
        var existing = TryReadKey();
        if (existing is not null)
            return existing;

        // 换了密钥，旧密文就再也解不开了：留着只会在下次启动时走一遍「解不开→删文件」，先删干净。
        TryDelete(CredentialPath);

        var key = RandomNumberGenerator.GetBytes(KeyLength);
        var file = new KeyFile
        {
            FormatVersion = KeyFormatVersion,
            Key = Convert.ToBase64String(key)
        };
        WriteAtomic(KeyPath, JsonSerializer.Serialize(file, JsonOptions));
        return key;
    }

    private void DiscardCorrupt()
    {
        TryDelete(CredentialPath);
        TryDelete(KeyPath);
        _current = null;
    }

    private void LogUnreadable(string reason) =>
        _logger?.LogWarning("集控凭证无法使用（{Reason}），已按「未接入」处理：{Path}", reason, CredentialPath);

    private static void WriteAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            File.Move(temp, path, true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 删不掉也继续：调用方拿到的是「没凭证」，下次启动会再试一次。
        }
    }

    /// <summary>密文信封（base64 三个字段；AAD 在代码里固定，不落盘）。</summary>
    private sealed class EnvelopeFile
    {
        public int FormatVersion { get; set; } = NodeEnrollmentStore.FormatVersion;
        public string Nonce { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string Ciphertext { get; set; } = string.Empty;
    }

    /// <summary>密钥文件。</summary>
    private sealed class KeyFile
    {
        public int FormatVersion { get; set; } = NodeEnrollmentStore.KeyFormatVersion;
        public string Key { get; set; } = string.Empty;
    }

    /// <summary>加密前的凭证负载。</summary>
    private sealed class PayloadFile
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Secret { get; set; } = string.Empty;
        public string SiteId { get; set; } = string.Empty;
        public string? SiteName { get; set; }
        public string? ClassId { get; set; }
        public string? ClassName { get; set; }
        public DateTimeOffset EnrolledAt { get; set; }
    }
}
