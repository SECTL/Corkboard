namespace Corkboard.Core.Updates;

/// <summary>
///     比较签名更新清单里的版本号与当前运行版本。发布 tag 是 <c>v3.0.0-beta.1</c> 这类 SemVer 字符串，
///     <see cref="Version" /> 本身解析不了，所以先去掉可选的 v 前缀与预发布/构建后缀，再比数字核心。
///     <para>
///         当前处于预发布通道时也能看见更新的正式版，桌面端与移动端共用同一条规则。
///     </para>
/// </summary>
public static class UpdateVersionComparer
{
    /// <summary>候选版本是否比当前版本新；任一侧解析失败都按「不新」处理。</summary>
    public static bool IsNewer(string candidate, string current) =>
        TryParse(candidate, out var candidateVersion)
        && TryParse(current, out var currentVersion)
        && candidateVersion > currentVersion;

    /// <summary>把 <c>v3.0.0-beta.1+build</c> 这类标签解析成数字版本；后缀不参与比较。</summary>
    public static bool TryParse(string text, out Version version)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = text.Trim().TrimStart('v', 'V');
        var suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
            normalized = normalized[..suffixIndex];
        return Version.TryParse(normalized, out version!);
    }
}
