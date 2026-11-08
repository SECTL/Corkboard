using System.Text;
using Corkboard.Core.DutyRoster.Import;

namespace Corkboard.Core.DutyRoster.Transfer;

/// <summary>
///     扫码传输的配对链接：<c>{公共地址}/#t={transferId}&amp;k={密钥 Base64Url}</c>。
///     <para>
///         与上游 SecRandom 的格式逐字一致：<c>t</c> 必须恰好 24 字符、<c>k</c> 必须解出 32 字节。
///         密钥只放在 URL fragment 里——fragment 不会随 HTTP 请求发出去，服务端永远看不到解密密钥。
///         校验别人发来的链接时要守住这两条，否则普通网址也会被当成配对码解出一堆乱码。
///     </para>
/// </summary>
public static class DutyQuickPairing
{
    /// <summary>配对码里的传输 id 长度（服务端生成的 24 字符 id）。</summary>
    public const int TransferIdLength = 24;

    /// <summary>配对码里的原始密钥长度。</summary>
    public const int KeyLength = 32;

    /// <summary>拼出可以直接画成二维码的配对链接。</summary>
    public static string Build(string publicUrl, string transferId, ReadOnlySpan<byte> key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(transferId);
        if (key.Length != KeyLength)
            throw new ArgumentException("配对密钥必须是 32 字节", nameof(key));

        var keyText = OfflineQrProtocol.EncodeBase64Url(key.ToArray());
        return $"{publicUrl.TrimEnd('/')}/#t={Uri.EscapeDataString(transferId)}&k={Uri.EscapeDataString(keyText)}";
    }

    /// <summary>解析扫码得到的文本；不是配对链接就返回 false，不抛异常。</summary>
    public static bool TryParse(string? value, out string transferId, out byte[] key)
    {
        transferId = string.Empty;
        key = [];
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var pairing) ||
            string.IsNullOrWhiteSpace(pairing.Fragment))
        {
            return false;
        }

        string? transfer = null;
        string? keyText = null;
        foreach (var pair in pairing.Fragment.TrimStart('#').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0 || separator == pair.Length - 1)
                continue;

            var name = Uri.UnescapeDataString(pair[..separator]);
            var text = Uri.UnescapeDataString(pair[(separator + 1)..]);
            if (name == "t")
                transfer = text;
            else if (name == "k")
                keyText = text;
        }

        if (transfer is null || keyText is null || transfer.Length != TransferIdLength)
            return false;

        var decoded = OfflineQrProtocol.DecodeBase64Url(keyText);
        if (decoded is not { Length: KeyLength })
            return false;

        transferId = transfer;
        key = decoded;
        return true;
    }

    /// <summary>把会话码放进链接不需要，但有些用户会直接贴「文本里的链接」，这里留一个纯文本复制入口。</summary>
    public static string Describe(string transferId) => $"传输编号：{transferId}";
}