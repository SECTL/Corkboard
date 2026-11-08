using System;
using System.Net;

namespace Corkboard.Core.RichText;

/// <summary>
///     超链接地址的收拾与拼装：把用户输入的半截地址补成能用的绝对 URI，
///     并按库认的 <c>&lt;a href&gt;</c> 形状拼出一段可以交给 <c>InsertHtml</c> 的 HTML。
///     <para>
///         只放行 <c>http</c> / <c>https</c>：库自己点链接时也只认这两个前缀
///         （<c>RichEditor.OpenUrl</c>），而 <c>javascript:</c>、<c>file:</c> 这类地址
///         从粘贴进来的富文本里溜进作业内容没有意义，还会在点开时吓人一跳。
///     </para>
///     <para>
///         纯逻辑、不碰 Avalonia，单测钉着（<c>HyperlinkUrlTests</c>）。
///     </para>
/// </summary>
public static class HyperlinkUrl
{
    /// <summary>
    ///     把输入收拾成绝对链接：<c>example.com/a</c> 与 <c>www.example.com</c> 都补上 <c>https://</c>；
    ///     空串、只有协议头、带别的协议（<c>ftp:</c> / <c>javascript:</c> …）一律拒绝。
    /// </summary>
    /// <param name="input">用户输入或从别处拿到的地址，可以带首尾空白。</param>
    /// <param name="url">成功时是规范化后的绝对地址，失败时是空串。</param>
    public static bool TryNormalize(string? input, out string url)
    {
        url = string.Empty;

        var text = input?.Trim();
        if (string.IsNullOrEmpty(text))
            return false;

        // 「example.com」这种最常见：没有协议头就补 https://；「www.」开头同样这么补，
        // 但显式写了 http:// 的保持原样（用户可能就是要走 http）。
        if (text.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            || !text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed))
            return false;

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        // 「https://」在有些平台上能造出空主机的 Uri，这种点开也没地方去。
        if (string.IsNullOrEmpty(parsed.Host))
            return false;

        url = parsed.AbsoluteUri;
        return true;
    }

    /// <summary>
    ///     拼一段链接 HTML（给 <c>RichEditor.InsertHtml</c> 用）：文字留空时直接拿地址当显示文字，
    ///     两处都会做 HTML 转义，免得地址里的 <c>&amp;</c> / 引号把整段片段弄坏。
    /// </summary>
    public static string BuildAnchorHtml(string url, string? text)
    {
        var label = string.IsNullOrWhiteSpace(text) ? url : text.Trim();
        return $"<a href=\"{WebUtility.HtmlEncode(url)}\">{WebUtility.HtmlEncode(label)}</a>";
    }
}
