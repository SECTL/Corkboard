using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Corkboard.Shared.Updates;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Corkboard.Core.Updates;

/// <summary>
///     更新清单验证：先用内置 Ed25519 公钥验证随包分发的 <c>.sig</c> 签名，再反序列化清单，
///     最后核对 schemaVersion / product / tag / channel 是否与本次请求一致。
///     <para>
///         清单是更新链路里唯一的信任根：签名只覆盖清单字节，清单再承载每个资产的 SHA-512 与长度。
///         所以这里任何一步不通过都必须抛异常，交给服务层转成「校验失败」状态，绝不能降级放行。
///     </para>
/// </summary>
public static class UpdateManifestVerifier
{
    /// <summary>当前客户端认识的清单格式版本。</summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>Ed25519 公钥字节长度（= <c>Ed25519PublicKeyParameters.KeySize</c>，BouncyCastle 里不是常量）。</summary>
    public const int PublicKeySize = 32;

    /// <summary>Ed25519 签名长度。</summary>
    public const int SignatureSize = 64;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    ///     验证签名并反序列化清单，同时校验 <paramref name="expectedProduct" /> / <paramref name="expectedTag" /> /
    ///     <paramref name="expectedChannel" /> 与清单内容一致。
    /// </summary>
    /// <exception cref="CryptographicException">签名无效、公钥无效或签名长度不对。</exception>
    /// <exception cref="InvalidDataException">清单格式无效或与预期不一致。</exception>
    public static UpdateManifest Verify(
        byte[] manifestBytes,
        byte[] signatureBytes,
        ReadOnlySpan<byte> publicKey,
        string expectedProduct,
        string expectedTag,
        UpdateChannel expectedChannel)
    {
        ArgumentNullException.ThrowIfNull(manifestBytes);
        ArgumentNullException.ThrowIfNull(signatureBytes);
        ArgumentException.ThrowIfNullOrEmpty(expectedProduct);
        ArgumentException.ThrowIfNullOrEmpty(expectedTag);

        VerifySignature(manifestBytes, signatureBytes, publicKey);
        var manifest = Deserialize(manifestBytes);
        ValidateMetadata(manifest, expectedProduct, expectedTag, expectedChannel);
        return manifest;
    }

    /// <summary>
    ///     校验 Ed25519 签名。独立暴露给只关心签名的调用方，行为与 <see cref="Verify" /> 的签名部分一致。
    /// </summary>
    /// <exception cref="CryptographicException">公钥长度不对或签名不通过。</exception>
    public static void VerifySignature(byte[] manifestBytes, byte[] signatureBytes, ReadOnlySpan<byte> publicKey)
    {
        ArgumentNullException.ThrowIfNull(manifestBytes);
        ArgumentNullException.ThrowIfNull(signatureBytes);

        if (publicKey.Length != PublicKeySize)
            throw new CryptographicException(UpdateText.Get("M_PublicKeyInvalid"));
        if (signatureBytes.Length != SignatureSize)
            throw new CryptographicException(UpdateText.Get("M_ManifestSignatureInvalid"));

        var signer = new Ed25519Signer();
        signer.Init(false, new Ed25519PublicKeyParameters(publicKey.ToArray(), 0));
        signer.BlockUpdate(manifestBytes, 0, manifestBytes.Length);
        if (!signer.VerifySignature(signatureBytes))
            throw new CryptographicException(UpdateText.Get("M_ManifestSignatureInvalid"));
    }

    /// <summary>可信清单字节的反序列化（大小写不敏感，与发布脚本的输出保持一致）。</summary>
    /// <exception cref="InvalidDataException">清单为空或不是合法的 JSON。</exception>
    public static UpdateManifest Deserialize(byte[] manifestBytes)
    {
        ArgumentNullException.ThrowIfNull(manifestBytes);

        UpdateManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestBytes, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(UpdateText.Get("M_ManifestInvalid"), exception);
        }

        if (manifest is null || manifest.Artifacts is null)
            throw new InvalidDataException(UpdateText.Get("M_ManifestInvalid"));

        return manifest;
    }

    /// <summary>校验清单声明的产品、tag、通道与本次请求一致。</summary>
    /// <exception cref="InvalidDataException">清单与预期不一致。</exception>
    public static void ValidateMetadata(
        UpdateManifest manifest,
        string expectedProduct,
        string expectedTag,
        UpdateChannel expectedChannel)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrEmpty(expectedProduct);
        ArgumentException.ThrowIfNullOrEmpty(expectedTag);

        // 通道在协议里是小写的枚举名（release / beta / alpha），与 metadata.yaml 的键一致。
        var expectedChannelText = expectedChannel.ToString().ToLowerInvariant();
        if (manifest.SchemaVersion != SupportedSchemaVersion
            || !string.Equals(manifest.Product, expectedProduct, StringComparison.Ordinal)
            || !string.Equals(manifest.Tag, expectedTag, StringComparison.Ordinal)
            || !string.Equals(manifest.Channel, expectedChannelText, StringComparison.Ordinal))
            throw new InvalidDataException(UpdateText.Get("M_ManifestMismatch"));
    }

    /// <summary>
    ///     解析内置公钥（Base64 文本）：必须是 32 字节且不能全零。
    /// </summary>
    /// <exception cref="CryptographicException">Base64 不合法、长度不对或内容全零。</exception>
    public static byte[] DecodePublicKey(string base64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64);

        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64.Trim());
        }
        catch (FormatException exception)
        {
            throw new CryptographicException(UpdateText.Get("M_PublicKeyInvalid"), exception);
        }

        if (key.Length != PublicKeySize || key.All(static value => value == 0))
            throw new CryptographicException(UpdateText.Get("M_PublicKeyInvalid"));

        return key;
    }
}
