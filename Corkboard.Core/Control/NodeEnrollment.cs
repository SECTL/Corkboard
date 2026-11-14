using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Corkboard.Core.ControlCenter;

/// <summary>一个班级（集控结构树的最末一层）。</summary>
/// <param name="Id">班级 id。</param>
/// <param name="Name">班级名（只用于界面展示）。</param>
public sealed record ControlClassNode(string Id, string Name);

/// <summary>一个年级（集控结构树的中间层）。</summary>
/// <param name="Id">年级 id。</param>
/// <param name="Name">年级名。</param>
/// <param name="Classes">年级下的班级。</param>
public sealed record ControlGradeNode(string Id, string Name, IReadOnlyList<ControlClassNode> Classes);

/// <summary>
///     设备凭证（AES-GCM 加密后落在数据根下，**不进 settings.json、不参与云同步**）。
///     <para>
///         <see cref="DeviceId" /> 是**服务端签发的**设备 id，不是本机 UUID——
///         本机标识只用于「这台机器接在别的地点」的冲突提示，两个概念不要混。
///     </para>
/// </summary>
/// <param name="DeviceId">服务端签发的设备 id。</param>
/// <param name="Secret">服务端只回一次的 secret。</param>
/// <param name="SiteId">所属地点 id（<c>SC-XXXXXX</c>）。</param>
/// <param name="SiteName">地点名（给用户确认「正在接入 启明中学」）。</param>
/// <param name="ClassId">接入时配对码绑定的班级（可为空）。</param>
/// <param name="ClassName">接入时配对码绑定的班级名（可为空）。</param>
public sealed record ControlNodeCredential(
    string DeviceId,
    string Secret,
    string SiteId,
    string? SiteName = null,
    string? ClassId = null,
    string? ClassName = null)
{
    /// <summary>接入时间（排障用）。</summary>
    public DateTimeOffset EnrolledAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>凭证是否完整（缺一项就等于没有凭证）。</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(DeviceId) &&
        !string.IsNullOrWhiteSpace(Secret) &&
        !string.IsNullOrWhiteSpace(SiteId);
}

/// <summary>
///     双因子接入的输入归一化与整句粘贴解析。
///     <para>
///         字母表与归一化规则见协议 §1.2，**服务端与客户端必须实现同一套**：
///         地点ID 归一成 <c>SC-XXXXXX</c>（6 位本体，<c>SC-</c> 可省），
///         配对码归一成 <c>ABCD-7KQ2</c>（8 位，中间一个连字符）。
///     </para>
///     <para>
///         整句粘贴（老师常把网页上那一行整段复制过来）只在**两个都能认出来**时才成功；
///         只认出地点就返回 <c>false</c> 且两个 <c>out</c> 都为 <c>null</c>——
///         半份结果最容易被调用方误当成完整输入。
///     </para>
/// </summary>
public static partial class NodeEnrollment
{
    /// <summary>地点ID 的本体长度（不含 <c>SC-</c>）。</summary>
    public const int SiteIdBodyLength = 6;

    /// <summary>配对码的压缩长度（不含连字符）。</summary>
    public const int PairCodeLength = 8;

    /// <summary>整句粘贴允许的最大长度（防止把一整份表单贴进来）。</summary>
    public const int MaxInputLength = 200;

    /// <summary>地点ID 前缀。</summary>
    public const string SiteIdPrefix = "SC-";

    /// <summary>
    ///     地点ID 归一化：只留字母数字、全角转半角、转大写，<c>SC</c> 前缀可省，
    ///     本体必须正好 6 位，返回 <c>SC-XXXXXX</c>；认不出来返回 <c>null</c>。
    /// </summary>
    public static string? NormalizeSiteId(string? value)
    {
        var compact = Compact(value, keepOnlyAsciiAlphanumeric: true);
        if (compact.Length == 0)
            return null;

        var body = compact;
        if (body.StartsWith("SC", StringComparison.Ordinal) && body.Length == 2 + SiteIdBodyLength)
            body = body[2..];

        return body.Length == SiteIdBodyLength ? SiteIdPrefix + body : null;
    }

    /// <summary>
    ///     配对码归一化：只留字母数字、转大写，必须正好 8 位，返回展示形态 <c>ABCD-7KQ2</c>；
    ///     认不出来返回 <c>null</c>。
    /// </summary>
    public static string? NormalizePairCode(string? value)
    {
        var compact = Compact(value, keepOnlyAsciiAlphanumeric: true);
        return compact.Length == PairCodeLength ? FormatPairCode(compact) : null;
    }

    /// <summary>把 8 位压缩配对码变成展示形态（<c>ABCD7KQ2</c> → <c>ABCD-7KQ2</c>）。</summary>
    public static string FormatPairCode(string compact)
    {
        ArgumentNullException.ThrowIfNull(compact);
        return compact.Length == PairCodeLength
            ? string.Concat(compact.AsSpan(0, 4), "-", compact.AsSpan(4, 4))
            : compact;
    }

    /// <summary>
    ///     解析整句粘贴（例：<c>地点ID：SC-6M34RQ　配对码：K7Q2-9XMD　有效期 24 小时</c>）。
    ///     <para>
    ///         <b>必须先找地点、把它从文本里摘掉，再找配对码。</b>反过来的顺序会让
    ///         「地点那 6 位本体 + 紧跟的字符」被正则拼成一个假的 4+4 配对码
    ///         （<c>SC-6M34RQK7Q2-9XMD</c> 会先匹到 <c>34RQK7Q2</c>）。
    ///     </para>
    /// </summary>
    public static bool TryParseEnrollmentText(string? value, out string? siteId, out string? pairCode)
    {
        siteId = null;
        pairCode = null;

        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxInputLength)
            return false;

        // 全角冒号 / 全角空格先折成半角，再统一大写（协议 §1.2 的归一化在两边都要做）。
        var text = value.Normalize(NormalizationForm.FormKC).ToUpperInvariant();

        var siteMatch = SiteIdPattern().Match(text);
        if (!siteMatch.Success)
            return false;

        var normalizedSite = NormalizeSiteId(siteMatch.Value);
        if (normalizedSite is null)
            return false;

        var remainder = text.Remove(siteMatch.Index, siteMatch.Length);
        var codeMatch = PairCodePattern().Match(remainder);
        if (!codeMatch.Success)
            return false;

        var normalizedCode = NormalizePairCode(codeMatch.Value);
        if (normalizedCode is null)
            return false;

        siteId = normalizedSite;
        pairCode = normalizedCode;
        return true;
    }

    /// <summary>只留 ASCII 字母数字并转大写（可先把全角折成半角）。</summary>
    private static string Compact(string? value, bool keepOnlyAsciiAlphanumeric)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var text = value.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (!keepOnlyAsciiAlphanumeric || char.IsAsciiLetterOrDigit(ch))
                builder.Append(char.ToUpperInvariant(ch));
        }

        return builder.ToString();
    }

    // 字母表排除容易读错的 0/1/I/L/O（协议 §1.1），与参考实现逐字一致。
    [GeneratedRegex(@"SC[\s\-_:：.]*[2-9A-HJ-KM-NP-Z]{6}", RegexOptions.CultureInvariant)]
    private static partial Regex SiteIdPattern();

    [GeneratedRegex(@"[2-9A-HJ-KM-NP-Z]{4}[\s\-_:：.]*[2-9A-HJ-KM-NP-Z]{4}", RegexOptions.CultureInvariant)]
    private static partial Regex PairCodePattern();
}
