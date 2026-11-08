using System;
using System.IO;
using System.Text;
using Corkboard.Core.DutyRoster.Import;
using Corkboard.Core.DutyRoster.Transfer;

namespace Corkboard.Core.Tests;

/// <summary>
///     云传输协议层的单测：信封加解密、会话码、配对链接、名单压缩包。
///     全部是纯字节运算，不联网、不碰 UI——与服务层分开测，协议回归不会牵连网络。
/// </summary>
public sealed class DutyRosterTransferTests
{
    private const string PairingBase = "https://secrandom-sync.sectl.cn";

    [Fact]
    public void RawKeyEnvelopeRoundTrips()
    {
        var key = Enumerable.Range(0, DutyTransferCipher.KeyLength).Select(index => (byte)index).ToArray();
        var plaintext = Encoding.UTF8.GetBytes("值日名单压缩包内容");

        var envelope = DutyTransferCipher.EncryptWithRawKey(plaintext, key);
        Assert.Equal(DutyTransferCipher.FormatName, envelope.Format);
        Assert.Equal(DutyTransferCipher.EncryptionName, envelope.Encryption);
        Assert.Equal(DutyTransferCipher.RawKeyDerivation, envelope.KeyDerivation);
        Assert.Null(envelope.Iterations);

        var decrypted = DutyTransferCipher.Decrypt(envelope, key);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void SessionCodeEnvelopeRoundTrips()
    {
        var code = DutySessionCode.Create();
        var plaintext = Encoding.UTF8.GetBytes("session payload");

        var envelope = DutyTransferCipher.EncryptWithSessionCode(plaintext, code);
        Assert.Equal(DutyTransferCipher.Pbkdf2KeyDerivation, envelope.KeyDerivation);
        Assert.Equal(DutyTransferCipher.Pbkdf2Iterations, envelope.Iterations);

        var decrypted = DutyTransferCipher.Decrypt(envelope, Encoding.UTF8.GetBytes(code));
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void WrongSessionCodeIsRejected()
    {
        var envelope = DutyTransferCipher.EncryptWithSessionCode("secret"u8.ToArray(), DutySessionCode.Create());
        var otherCode = DutySessionCode.Create();

        var exception = Assert.Throws<InvalidDataException>(() =>
            DutyTransferCipher.Decrypt(envelope, Encoding.UTF8.GetBytes(otherCode)));
        Assert.Contains("解密失败", exception.Message);
    }

    [Fact]
    public void TamperedCiphertextIsRejected()
    {
        var key = Enumerable.Repeat((byte)7, DutyTransferCipher.KeyLength).ToArray();
        var envelope = DutyTransferCipher.EncryptWithRawKey("secret"u8.ToArray(), key);
        var ciphertext = Convert.FromBase64String(envelope.Ciphertext
            .Replace('-', '+').Replace('_', '/')
            .PadRight(envelope.Ciphertext.Length + (4 - envelope.Ciphertext.Length % 4) % 4, '='));
        ciphertext[0] ^= 0xFF;
        var tampered = envelope with { Ciphertext = Convert.ToBase64String(ciphertext).TrimEnd('=') };

        Assert.Throws<InvalidDataException>(() => DutyTransferCipher.Decrypt(tampered, key));
    }

    [Fact]
    public void EnvelopeJsonRoundTripsWithCamelCaseFields()
    {
        var envelope = DutyTransferCipher.EncryptWithRawKey("payload"u8.ToArray(), new byte[32]);
        var json = DutyTransferCipher.Serialize(envelope);

        var text = Encoding.UTF8.GetString(json);
        Assert.Contains("\"keyDerivation\":\"raw\"", text);
        Assert.Contains("\"format\":\"secrandom-sync-envelope/v1\"", text);
        Assert.Contains("\"ciphertext\"", text);

        var parsed = DutyTransferCipher.Deserialize(json);
        Assert.Equal(envelope, parsed);
        Assert.Equal("payload"u8.ToArray(), DutyTransferCipher.Decrypt(parsed, new byte[32]));
    }

    [Fact]
    public void SessionCodesAreNormalizedAndValidated()
    {
        Assert.Equal(12, DutySessionCode.Length);
        Assert.Equal("AB12CD34EF56", DutySessionCode.Normalize("ab-12 cd_34ef56"));
        Assert.Equal(string.Empty, DutySessionCode.Normalize(null));

        var code = DutySessionCode.Create();
        Assert.True(DutySessionCode.IsValid(code));
        Assert.Equal(12, code.Length);
        Assert.DoesNotContain(code, character => "IO01".Contains(character));

        Assert.False(DutySessionCode.IsValid("AB12CD34EF5"));
        Assert.False(DutySessionCode.IsValid("AB12CD34EF5O"));
        Assert.False(DutySessionCode.IsValid(null));
    }

    [Fact]
    public void QuickPairingRoundTripsAndRejectsNonPairingText()
    {
        var transferId = new string('t', DutyQuickPairing.TransferIdLength);
        var key = Enumerable.Range(0, DutyQuickPairing.KeyLength).Select(index => (byte)(index * 3)).ToArray();

        var pairingUrl = DutyQuickPairing.Build(PairingBase, transferId, key);
        Assert.StartsWith($"{PairingBase}/#t=", pairingUrl);
        Assert.True(DutyQuickPairing.TryParse(pairingUrl, out var parsedId, out var parsedKey));
        Assert.Equal(transferId, parsedId);
        Assert.Equal(key, parsedKey);

        Assert.False(DutyQuickPairing.TryParse("https://example.com/#t=short&k=abc", out _, out _));
        Assert.False(DutyQuickPairing.TryParse("https://example.com/", out _, out _));
        Assert.False(DutyQuickPairing.TryParse("not a url", out _, out _));
        Assert.False(DutyQuickPairing.TryParse(null, out _, out _));
        Assert.False(DutyQuickPairing.TryParse($"https://example.com/#t={transferId}&k=AAAA", out _, out _));
    }

    [Fact]
    public void RosterArchiveRoundTripsDocumentAndExtras()
    {
        var document = new RosterTransferDocument(
            OfflineQrProtocol.DocumentVersion,
            RosterTransferKind.DutyRoster,
            "值日名单.secrandom-roster.json",
            [
                new RosterTransferRow(true, "01", "张三", "男", "第一组", "一组"),
                new RosterTransferRow(false, "02", "李四", "女", null, null)
            ]);
        var extras = new Dictionary<string, byte[]> { ["值日名单.csv"] = "序号,名称\r\n01,张三\r\n"u8.ToArray() };

        var archive = DutyRosterArchive.Create(document, extras);
        var extracted = DutyRosterArchive.Extract(archive);

        Assert.Equal(document.Version, extracted.Version);
        Assert.Equal(document.Kind, extracted.Kind);
        Assert.Equal(document.FileName, extracted.FileName);
        Assert.Equal(document.Rows, extracted.Rows);
    }

    [Fact]
    public void UnsupportedArchiveVersionIsRejected()
    {
        var document = new RosterTransferDocument(2, RosterTransferKind.DutyRoster, "x.json", []);
        Assert.Throws<InvalidDataException>(() => DutyRosterArchive.Create(document));

        Assert.Throws<InvalidDataException>(() => DutyRosterArchive.Extract([1, 2, 3]));
    }

    [Fact]
    public void OversizedPayloadIsRejected()
    {
        var tooLarge = new byte[DutyTransferLimits.MaxPayloadBytes + 1];
        Assert.Throws<InvalidDataException>(() => DutyTransferLimits.EnsurePayloadSize(tooLarge.Length, "测试内容"));
    }
}