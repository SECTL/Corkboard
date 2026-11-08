using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Corkboard.Core.DutyRoster.Import;

namespace Corkboard.Core.DutyRoster.Transfer;

/// <summary>
///     云传输的名单压缩包：与上游 SecRandom 的 roster archive 结构一致——
///     根目录一个 <c>secrandom-roster-transfer.json</c>（名单文档），外加给手机端预览用的 xlsx/csv。
///     <para>
///         上游解包只认那个 json；xlsx/csv 是「下载到手机后还能另存」的顺手产物，缺了也不影响导入。
///         这里把「哪些条目必须存在」写死成契约，避免以后两边各改一套。
///     </para>
/// </summary>
public static class DutyRosterArchive
{
    public const string DocumentEntryName = "secrandom-roster-transfer.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>组装压缩包；值日名单默认带 csv（手机端预览用），xlsx 可按需要额外传入。</summary>
    public static byte[] Create(
        RosterTransferDocument document,
        IReadOnlyDictionary<string, byte[]>? extraEntries = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Version != OfflineQrProtocol.DocumentVersion)
            throw new InvalidDataException($"名单文档版本 {document.Version} 不受支持");

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (extraEntries is not null)
            {
                foreach (var (name, content) in extraEntries)
                {
                    if (string.IsNullOrWhiteSpace(name) || name.Contains('/') || name.Contains('\\'))
                        throw new ArgumentException($"压缩包条目名不合法：{name}", nameof(extraEntries));

                    var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                    using var stream = entry.Open();
                    stream.Write(content);
                }
            }

            var documentEntry = archive.CreateEntry(DocumentEntryName, CompressionLevel.Fastest);
            using var documentStream = documentEntry.Open();
            JsonSerializer.Serialize(documentStream, document, JsonOptions);
        }

        var result = output.ToArray();
        DutyTransferLimits.EnsurePayloadSize(result.LongLength, "名单传输压缩包");
        return result;
    }

    /// <summary>解开压缩包并校验版本；坏包抛 <see cref="InvalidDataException" />。</summary>
    public static RosterTransferDocument Extract(byte[] archiveBytes)
    {
        ArgumentNullException.ThrowIfNull(archiveBytes);
        DutyTransferLimits.EnsurePayloadSize(archiveBytes.LongLength, "名单传输压缩包");

        using var input = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        var entry = archive.GetEntry(DocumentEntryName)
                    ?? throw new InvalidDataException("传输压缩包里没有名单数据");

        if (entry.Length > DutyTransferLimits.MaxPayloadBytes)
            throw new InvalidDataException("名单数据超过 1 MiB 传输上限");

        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        DutyTransferLimits.EnsurePayloadSize(buffer.Length, "名单数据");

        RosterTransferDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<RosterTransferDocument>(buffer.ToArray(), JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("名单数据不是有效的 JSON", exception);
        }

        if (document is null || document.Version != OfflineQrProtocol.DocumentVersion)
            throw new InvalidDataException("传输的名单版本不受支持");

        return document;
    }
}