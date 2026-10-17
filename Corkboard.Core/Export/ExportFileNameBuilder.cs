using System.Globalization;
using System.IO;

namespace Corkboard.Core.Export;

/// <summary>
///     导出文件名：默认名字（时间戳）与保存对话框兜底用的扩展名处理。
///     <para>
///         时间戳一律用 <see cref="CultureInfo.InvariantCulture" /> 的 <see cref="TimestampFormat" />，
///         不随系统语言 / 时区变——文件名是给人翻的，跨机器看起来应该一样。
///     </para>
/// </summary>
public static class ExportFileNameBuilder
{
    /// <summary>默认文件名里的时间戳格式：<c>2026-10-18_14-30-05</c>。</summary>
    public const string TimestampFormat = "yyyy-MM-dd_HH-mm-ss";

    /// <summary>
    ///     生成默认文件名：时间戳 + 扩展名，形如 <c>2026-10-18_14-30-05.png</c>。
    /// </summary>
    /// <param name="now">要写进文件名的时间（调用方给，测试才不依赖本机时钟）。</param>
    /// <param name="extension">
    ///     扩展名，<c>"png"</c> 与 <c>".pdf"</c> 都可以：前后的空白与点会被归一，最终只有一个小写扩展名。
    ///     给空串时只返回时间戳（不带结尾的点）。
    /// </param>
    public static string Build(DateTime now, string extension)
    {
        var timestamp = now.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var normalized = NormalizeExtension(extension);
        return normalized.Length == 0 ? timestamp : $"{timestamp}.{normalized}";
    }

    /// <summary>
    ///     保存对话框的兜底：给的文件名没有目标扩展名就补上，带了别的扩展名则先去掉再补。
    ///     已经带目标扩展名时（大小写不敏感）原样返回，不会把 <c>a.PNG</c> 改成 <c>a.png</c>。
    /// </summary>
    /// <param name="fileName">用户填的文件名，可以带目录。</param>
    /// <param name="extension">目标扩展名，<c>"png"</c> / <c>".pdf"</c> 均可。</param>
    public static string EnsureExtension(string fileName, string extension)
    {
        var name = fileName ?? string.Empty;
        var normalized = NormalizeExtension(extension);

        // 没有文件名或没有扩展名可补：原样交回去，交给保存对话框自己校验。
        if (string.IsNullOrWhiteSpace(name) || normalized.Length == 0)
            return name;

        if (name.EndsWith("." + normalized, StringComparison.OrdinalIgnoreCase))
            return name;

        // 只去掉最后一段扩展名，目录里的点（如 C:\out.v2\作业）不能碰；
        // 名字以点结尾时（Path.GetExtension 认为是空扩展名）先把结尾的点收掉，免得拼出 a..png。
        var existing = Path.GetExtension(name);
        var stem = existing.Length > 0 ? name[..^existing.Length] : name;
        stem = stem.TrimEnd('.');

        // 全被点占满的名字（"." / "..."）没有可用主干，原样交回去。
        return stem.Length == 0 ? name : stem + "." + normalized;
    }

    /// <summary>
    ///     归一扩展名：去空白、去前导点、转小写；空输入返回空串。
    /// </summary>
    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        return extension.Trim().TrimStart('.').ToLowerInvariant();
    }
}
