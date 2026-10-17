using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Corkboard.Core.Updates;
using Corkboard.Shared.Updates;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace Corkboard.Core.Tests;

/// <summary>
///     <see cref="UpdateManifestVerifier" /> 的单测：清单是更新链路唯一的信任根，
///     签名、产品、tag、通道任何一项对不上都不能放行。
///     <para>
///         测试用 BouncyCastle 现场生成 Ed25519 密钥对签清单，不依赖随包分发的真实公钥。
///     </para>
/// </summary>
public class UpdateManifestVerifierTests
{
    private const string Product = "Corkboard";
    private const string Tag = "v0.2.0";

    [Fact]
    public void Verify_AcceptsSignedManifest()
    {
        var (keys, manifestBytes) = CreateSignedManifest();

        var manifest = UpdateManifestVerifier.Verify(
            manifestBytes, keys.Signature, keys.PublicKey, Product, Tag, UpdateChannel.Release);

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal(Product, manifest.Product);
        Assert.Equal("release", manifest.Channel);
        Assert.Equal(Tag, manifest.Tag);
        Assert.Equal("0.2.0", manifest.Version);
        Assert.Equal("https://github.com/SECTL/Corkboard/releases/tag/v0.2.0", manifest.NotesUrl);
        var artifact = Assert.Single(manifest.Artifacts);
        Assert.Equal("Corkboard-v0.2.0-win-x64-portable-full.zip", artifact.AssetName);
        Assert.Equal("portable-zip", artifact.Kind);
        Assert.Equal(1234, artifact.ByteLength);
    }

    [Fact]
    public void Verify_RejectsTamperedManifest()
    {
        var (keys, manifestBytes) = CreateSignedManifest();
        // 改一个字节：签名覆盖的是原始字节，任何改动都必须验不过。
        manifestBytes[manifestBytes.Length - 2] ^= 0x01;

        Assert.Throws<CryptographicException>(() => UpdateManifestVerifier.Verify(
            manifestBytes, keys.Signature, keys.PublicKey, Product, Tag, UpdateChannel.Release));
    }

    [Fact]
    public void Verify_RejectsWrongTag()
    {
        var (keys, manifestBytes) = CreateSignedManifest();

        var exception = Assert.Throws<InvalidDataException>(() => UpdateManifestVerifier.Verify(
            manifestBytes, keys.Signature, keys.PublicKey, Product, "v9.9.9", UpdateChannel.Release));
        Assert.Contains(UpdateText.Get("M_ManifestMismatch"), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_RejectsWrongChannel()
    {
        var (keys, manifestBytes) = CreateSignedManifest();

        Assert.Throws<InvalidDataException>(() => UpdateManifestVerifier.Verify(
            manifestBytes, keys.Signature, keys.PublicKey, Product, Tag, UpdateChannel.Beta));
    }

    [Fact]
    public void Verify_RejectsWrongProduct()
    {
        var (keys, manifestBytes) = CreateSignedManifest();

        Assert.Throws<InvalidDataException>(() => UpdateManifestVerifier.Verify(
            manifestBytes, keys.Signature, keys.PublicKey, "SecRandom", Tag, UpdateChannel.Release));
    }

    [Fact]
    public void Verify_RejectsWrongPublicKey()
    {
        var (keys, manifestBytes) = CreateSignedManifest();
        var otherKeys = CreateKeyPair();

        // 清单确实是 keys 签的；换一把合法但不同的公钥来验，必须拒绝。
        Assert.Throws<CryptographicException>(() => UpdateManifestVerifier.Verify(
            manifestBytes, keys.Signature, otherKeys.PublicKey, Product, Tag, UpdateChannel.Release));
    }

    [Fact]
    public void Verify_RejectsWrongSignatureLength()
    {
        var (keys, manifestBytes) = CreateSignedManifest();

        Assert.Throws<CryptographicException>(() => UpdateManifestVerifier.Verify(
            manifestBytes, keys.Signature[..32], keys.PublicKey, Product, Tag, UpdateChannel.Release));
    }

    [Fact]
    public void Verify_RejectsWrongPublicKeyLength()
    {
        var (keys, manifestBytes) = CreateSignedManifest();

        Assert.Throws<CryptographicException>(() => UpdateManifestVerifier.Verify(
            manifestBytes, keys.Signature, keys.PublicKey[..16], Product, Tag, UpdateChannel.Release));
    }

    [Fact]
    public void Verify_RejectsUnsupportedSchemaVersion()
    {
        var (keys, manifestBytes) = CreateSignedManifest(schemaVersion: 2);

        Assert.Throws<InvalidDataException>(() => UpdateManifestVerifier.Verify(
            manifestBytes, keys.Signature, keys.PublicKey, Product, Tag, UpdateChannel.Release));
    }

    [Fact]
    public void Deserialize_RejectsInvalidJson()
    {
        Assert.Throws<InvalidDataException>(() => UpdateManifestVerifier.Deserialize("not json"u8.ToArray()));
    }

    [Fact]
    public void Deserialize_RejectsNullArtifacts()
    {
        var json = """{ "schemaVersion": 1, "product": "Corkboard", "artifacts": null }"""u8.ToArray();

        Assert.Throws<InvalidDataException>(() => UpdateManifestVerifier.Deserialize(json));
    }

    [Fact]
    public void DecodePublicKey_AcceptsValidKey()
    {
        var keys = CreateKeyPair();

        var decoded = UpdateManifestVerifier.DecodePublicKey(Convert.ToBase64String(keys.PublicKey));

        Assert.Equal(keys.PublicKey, decoded);
    }

    [Fact]
    public void DecodePublicKey_RejectsInvalidBase64()
    {
        Assert.Throws<CryptographicException>(() => UpdateManifestVerifier.DecodePublicKey("not base64!"));
    }

    [Fact]
    public void DecodePublicKey_RejectsWrongLength()
    {
        Assert.Throws<CryptographicException>(() => UpdateManifestVerifier.DecodePublicKey(
            Convert.ToBase64String(new byte[UpdateManifestVerifier.PublicKeySize - 1])));
    }

    [Fact]
    public void DecodePublicKey_RejectsAllZeroKey()
    {
        Assert.Throws<CryptographicException>(() => UpdateManifestVerifier.DecodePublicKey(
            Convert.ToBase64String(new byte[UpdateManifestVerifier.PublicKeySize])));
    }

    private static (TestKeyPair Keys, byte[] ManifestBytes) CreateSignedManifest(int schemaVersion = 1)
    {
        var manifest = new UpdateManifest
        {
            SchemaVersion = schemaVersion,
            Product = Product,
            Channel = "release",
            Tag = Tag,
            Version = "0.2.0",
            PublishedAt = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero),
            NotesUrl = "https://github.com/SECTL/Corkboard/releases/tag/v0.2.0",
            Artifacts =
            [
                new UpdateArtifact
                {
                    Id = "windows-x64-portable-zip-self-contained",
                    Os = "windows",
                    Arch = "x64",
                    Kind = "portable-zip",
                    RuntimeKind = "self-contained",
                    AssetName = "Corkboard-v0.2.0-win-x64-portable-full.zip",
                    ByteLength = 1234,
                    Sha512 = new string('a', 128),
                    EntryPath = "Corkboard.Desktop.exe"
                }
            ]
        };
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions
        {
            WriteIndented = false
        });
        var keys = CreateKeyPair();
        var signature = Sign(manifestBytes, keys.PrivateKey);
        return (new TestKeyPair(keys.PublicKey, keys.PrivateKey, signature), manifestBytes);
    }

    private static TestKeyPair CreateKeyPair()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        var privateKey = (Ed25519PrivateKeyParameters)pair.Private;
        var publicKey = (Ed25519PublicKeyParameters)pair.Public;
        return new TestKeyPair(publicKey.GetEncoded(), privateKey.GetEncoded(), []);
    }

    private static byte[] Sign(byte[] content, byte[] privateKey)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, new Ed25519PrivateKeyParameters(privateKey, 0));
        signer.BlockUpdate(content, 0, content.Length);
        return signer.GenerateSignature();
    }

    private sealed record TestKeyPair(byte[] PublicKey, byte[] PrivateKey, byte[] Signature);
}
