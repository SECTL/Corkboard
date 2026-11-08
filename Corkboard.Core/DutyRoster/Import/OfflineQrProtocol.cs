using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Corkboard.Core.DutyRoster.Import;

/// <summary>
///     离线二维码传输的文档类型。与上游 SecRandom 的 <c>RosterTransferKind</c> <b>数值兼容</b>：
///     Students=0 / Prizes=1 保持不变，Corkboard 的值日名单追加在末尾。
///     <para>
///         枚举按数字序列化（<see cref="JsonSerializerDefaults.Web" />），所以只在末尾追加——
///         改动已有数值会让另一端的旧版本把名单解析成别的种类。
///     </para>
/// </summary>
public enum RosterTransferKind
{
    Students = 0,
    Prizes = 1,

    /// <summary>值日名单（Corkboard 追加，SR 端不认识时会拒绝导入，属于预期行为）。</summary>
    DutyRoster = 2
}

/// <summary>
///     传输的单行。字段命名与 SR 一致（<c>exists</c> / <c>id</c> / <c>name</c> / <c>detailOne</c> /
///     <c>detailTwo</c> / <c>tags</c>），避免同一个二维码在两边被解释成不同的东西。
///     值日名单把性别放 <see cref="DetailOne" />、分组放 <see cref="DetailTwo" />，与 SR 导学生名单的约定相同。
/// </summary>
public sealed record RosterTransferRow(
    bool Exists,
    string Id,
    string Name,
    string? DetailOne = null,
    string? DetailTwo = null,
    string? Tags = null);

/// <summary>
///     离线二维码承载的文档。<c>Version</c> 给未来协议演进留余地（读取端当前不校验，宽容优先）。
/// </summary>
public sealed record RosterTransferDocument(
    int Version,
    RosterTransferKind Kind,
    string FileName,
    IReadOnlyList<RosterTransferRow> Rows);

/// <summary>单帧的接收结论；<see cref="Rejected" /> 的帧直接丢掉，不影响已收内容，也不会让界面弹错。</summary>
public enum RosterQrFrameImportResult
{
    /// <summary>帧有效，已记下。</summary>
    Accepted = 0,

    /// <summary>这一帧之前已经收过（重复扫描/两帧同框扫到两次）。</summary>
    Duplicate = 1,

    /// <summary>帧格式不对、不属于当前会话，或数值越界。</summary>
    Rejected = 2
}

/// <summary>整包组装阶段的错误码。错误码给界面取文案，<see cref="OfflineQrImportResult.Message" /> 只作诊断。</summary>
public enum RosterQrImportError
{
    None = 0,

    /// <summary>清单帧还没扫到。</summary>
    NoManifest,

    /// <summary>还有数据帧没扫到。</summary>
    Incomplete,

    /// <summary>分片拼出来的长度不够（理论上与 <see cref="Incomplete" /> 重叠，保留给畸形帧）。</summary>
    MissingChunks,

    /// <summary>SHA-256 与清单帧不符：内容被改过或扫错。</summary>
    ChecksumMismatch,

    /// <summary>解压后体积超过安全上限（防 zip bomb）。</summary>
    DecompressionLimit,

    /// <summary>解压成功但 JSON 不是有效文档。</summary>
    InvalidContent
}

/// <summary>整包组装结果。<see cref="Document" /> 只在成功时非空。</summary>
public sealed record OfflineQrImportResult(RosterTransferDocument? Document, RosterQrImportError Error, string? Message)
{
    public bool Success => Error == RosterQrImportError.None && Document is not null;

    internal static OfflineQrImportResult Failure(RosterQrImportError error, string message) => new(null, error, message);
}

/// <summary>
///     离线二维码分包协议（与 SR 的 <c>RosterTransferService</c> bit 级兼容）：
///     文档 → JSON(camelCase) → Brotli → 按 128 字节切块 → 清单帧 + 数据帧文本。
///     <para>
///         协议层只处理文本帧与文档对象，<b>不涉及图片生成/识别</b>——那部分交给界面与 QR 库，
///         这样整段逻辑可以在没有 UI、没有摄像头的情况下单测。
///     </para>
///     <para>
///         帧格式（<c>|</c> 分隔，字段顺序照搬 SR，不能改）：
///         清单 = <c>SRQR1M|会话(32)|文件名(Base64Url)|压缩长度|总帧数|校验(SHA-256 十六进制 64 位)</c>；
///         数据 = <c>SRQR1D|会话|帧号(D4)|偏移(D7)|压缩长度|校验|分片(Base64)</c>。
///         帧号固定 4 位、偏移固定 7 位，是为了让码图上的文本长度稳定——变长的帧会让同一批二维码
///         版本号跳来跳去，扫描端体验很差。
///     </para>
/// </summary>
public static class OfflineQrProtocol
{
    public const string ManifestPrefix = "SRQR1M";

    public const string DataPrefix = "SRQR1D";

    /// <summary>新版本每帧承载的字节数。</summary>
    public const int FramePayloadLength = 128;

    /// <summary>旧版本（SR 早期）每帧承载的字节数；只用于读取兼容，导出时默认不写 320。</summary>
    public const int LegacyFramePayloadLength = 320;

    public const int FrameIndexDigits = 4;

    public const int FrameOffsetDigits = 7;

    /// <summary>帧数上限。再多就扫不动了，应该改用文件导入。</summary>
    public const int MaximumFrameCount = 4096;

    /// <summary>压缩后内容上限 300 KiB。二维码扫几千帧不现实，超过就提示改用文件导入。</summary>
    public const int MaximumPayloadBytes = 300 * 1024;

    /// <summary>解压后的安全上限倍数（相对压缩后长度），防 zip bomb 把内存撑爆。</summary>
    public const int MaximumDecompressionRatio = 2;

    /// <summary>当前文档版本号。</summary>
    public const int DocumentVersion = 1;

    private const int SessionIdLength = 32;

    private const int ChecksumLength = 64;

    /// <summary>camelCase 与 SR 一致；同一个选项对象也用于反序列化（见 <see cref="OfflineQrFrameAccumulator" />）。</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    ///     文档 → 帧文本列表（第一帧是清单帧）。
    ///     <para>
    ///         <paramref name="sessionId" /> 默认随机生成；测试与重放场景可以传固定值。
    ///         <paramref name="framePayloadLength" /> 只允许 128（默认）或 320（旧版本兼容测试）。
    ///     </para>
    /// </summary>
    /// <exception cref="InvalidDataException">内容超过 300 KiB 或帧数超过 4096。</exception>
    public static IReadOnlyList<string> CreateFrames(
        RosterTransferDocument document,
        string? sessionId = null,
        int framePayloadLength = FramePayloadLength)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (framePayloadLength is not (FramePayloadLength or LegacyFramePayloadLength))
            throw new ArgumentOutOfRangeException(nameof(framePayloadLength), framePayloadLength, "每帧只支持 128 或 320 字节。");

        var session = string.IsNullOrWhiteSpace(sessionId) ? CreateSessionId() : sessionId;
        if (session.Length != SessionIdLength)
            throw new ArgumentException($"会话 ID 必须是 {SessionIdLength} 个字符。", nameof(sessionId));

        var json = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        var compressed = Compress(json);
        if (compressed.LongLength > MaximumPayloadBytes)
        {
            throw new InvalidDataException(
                $"二维码内容超过 {MaximumPayloadBytes / 1024} KiB，请减少名单人数，或改用文件导入。");
        }

        var totalFrames = (int)Math.Max(1, (compressed.Length + framePayloadLength - 1) / framePayloadLength);
        if (totalFrames > MaximumFrameCount)
            throw new InvalidDataException($"二维码需要 {totalFrames} 帧，超过 {MaximumFrameCount} 帧上限，请改用文件导入。");

        var checksum = Convert.ToHexString(SHA256.HashData(compressed));
        var frames = new List<string>(totalFrames + 1)
        {
            string.Join('|', ManifestPrefix, session, EncodeBase64Url(Encoding.UTF8.GetBytes(document.FileName ?? string.Empty)),
                compressed.Length, totalFrames, checksum)
        };

        for (var index = 0; index < totalFrames; index++)
        {
            var offset = index * framePayloadLength;
            var length = Math.Min(framePayloadLength, compressed.Length - offset);
            var frameBytes = new byte[framePayloadLength];
            if (length > 0)
                Buffer.BlockCopy(compressed, offset, frameBytes, 0, length);

            frames.Add(string.Join('|', DataPrefix, session,
                index.ToString("D" + FrameIndexDigits, CultureInfo.InvariantCulture),
                offset.ToString("D" + FrameOffsetDigits, CultureInfo.InvariantCulture),
                compressed.Length, checksum, Convert.ToBase64String(frameBytes)));
        }

        return frames;
    }

    /// <summary>生成 32 位十六进制会话 ID（与 SR 的 <c>Guid.NewGuid().ToString("N")</c> 同形）。</summary>
    public static string CreateSessionId() => Guid.NewGuid().ToString("N");

    /// <summary>压缩（Brotli Optimal）。与 SR 相同，压缩级别影响能否压进 300 KiB。</summary>
    private static byte[] Compress(byte[] content)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
            brotli.Write(content, 0, content.Length);

        return output.ToArray();
    }

    /// <summary>解压并限制体积。超过上限立刻失败，不把内存交给可疑数据。</summary>
    internal static byte[] Decompress(byte[] payload)
    {
        using var input = new MemoryStream(payload, false);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = brotli.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > MaximumPayloadBytes * MaximumDecompressionRatio)
                throw new InvalidDataException("Offline QR transfer expands beyond its safety limit.");

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    /// <summary>Base64 → URL 安全形态（去掉补位、+ → -、/ → _），二维码里 '-' '_' 比 '+' '/' 好扫。</summary>
    public static string EncodeBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>URL 安全 Base64 → 字节；字符串非法时返回 <c>null</c>（宽容解析，不抛给调用方）。</summary>
    public static byte[]? DecodeBase64Url(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Replace('-', '+').Replace('_', '/');
        var remainder = normalized.Length % 4;
        if (remainder == 1)
            return null;

        if (remainder > 0)
            normalized += new string('=', 4 - remainder);

        try
        {
            return Convert.FromBase64String(normalized);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>
///     扫码接收端：把用户扫到的帧文本逐条喂进来，凑齐后组装成文档。
///     <para>
///         帧可能乱序、重复、跨会话（上一份名单扫了一半换了新码），所以这里不假设任何顺序：
///         <list type="bullet">
///             <item>清单帧决定会话 ID、校验值、压缩长度与总帧数；</item>
///             <item>数据帧按帧号放进字典，重复帧返回 <see cref="RosterQrFrameImportResult.Duplicate" />；</item>
///             <item>扫到<b>另一个会话</b>的清单帧时清空重开，旧的半份名单不会污染新的。</item>
///         </list>
///     </para>
///     <para>
///         组装失败一律返回 <see cref="RosterQrImportError" /> 而不是抛异常：扫码是长流程，
///         中间任何一帧不对都应该让用户重扫那一帧，而不是让导入页崩溃。
///     </para>
/// </summary>
public sealed class OfflineQrFrameAccumulator
{
    private readonly Dictionary<int, byte[]> _chunks = [];

    private string? _checksum;
    private string? _fileName;
    private int _payloadLength = -1;
    private int _totalFrames = -1;

    /// <summary>当前会话 ID；还没扫到清单帧时为 <c>null</c>。</summary>
    public string? SessionId { get; private set; }

    /// <summary>清单帧里带的原始文件名（仅供界面提示，真实名字在文档里）。</summary>
    public string? HintFileName => _fileName;

    public bool HasManifest => SessionId is not null;

    /// <summary>已收到的不同数据帧数量。</summary>
    public int ReceivedFrameCount => _chunks.Count;

    /// <summary>清单帧声明的总帧数；没有清单帧时为 0。</summary>
    public int TotalFrameCount => _totalFrames > 0 ? _totalFrames : 0;

    /// <summary>清单帧已扫到且数据帧齐了。齐了才能调用 <see cref="Complete" />。</summary>
    public bool IsComplete => HasManifest && _totalFrames > 0 && _chunks.Count == _totalFrames;

    /// <summary>丢掉当前会话的所有状态（用户取消、换一份名单时调用）。</summary>
    public void Reset()
    {
        _chunks.Clear();
        SessionId = null;
        _checksum = null;
        _fileName = null;
        _payloadLength = -1;
        _totalFrames = -1;
    }

    /// <summary>
    ///     喂一帧。返回帧级结论；畸形帧直接 <see cref="RosterQrFrameImportResult.Rejected" />，不抛异常。
    /// </summary>
    public RosterQrFrameImportResult AddFrame(string? frame)
    {
        var parts = ParseFrameParts(frame);
        if (parts is null)
            return RosterQrFrameImportResult.Rejected;

        return parts[0] == OfflineQrProtocol.ManifestPrefix
            ? ApplyManifest(parts)
            : ApplyDataFrame(parts);
    }

    /// <summary>
    ///     组装。未收齐、校验失败、解压超限、内容不是文档都返回对应错误码；
    ///     成功时返回文档，且不会自动 <see cref="Reset" />（界面确认导入后自己清）。
    /// </summary>
    public OfflineQrImportResult Complete()
    {
        if (!HasManifest)
            return OfflineQrImportResult.Failure(RosterQrImportError.NoManifest, "还没有扫描到二维码清单帧。");

        if (_payloadLength <= 0 || _totalFrames <= 0)
            return OfflineQrImportResult.Failure(RosterQrImportError.Incomplete, "二维码清单帧不完整，请重新扫描。");

        if (!IsComplete)
        {
            return OfflineQrImportResult.Failure(RosterQrImportError.Incomplete,
                $"还缺 {_totalFrames - _chunks.Count} 帧没有扫描。");
        }

        var payload = new byte[_payloadLength];
        var written = 0;
        for (var index = 0; index < _totalFrames && written < _payloadLength; index++)
        {
            if (!_chunks.TryGetValue(index, out var chunk))
                return OfflineQrImportResult.Failure(RosterQrImportError.MissingChunks, "二维码分包缺失。");

            var length = Math.Min(chunk.Length, _payloadLength - written);
            if (length <= 0)
                continue;

            Buffer.BlockCopy(chunk, 0, payload, written, length);
            written += length;
        }

        if (written < _payloadLength)
            return OfflineQrImportResult.Failure(RosterQrImportError.MissingChunks, "二维码分包缺失。");

        // 定长比较：校验值本身不敏感，但它决定「这份内容能不能信」，用 FixedTimeEquals 免掉逐字节提前返回。
        var expected = ParseHex(_checksum);
        if (expected is null || !CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(payload)))
            return OfflineQrImportResult.Failure(RosterQrImportError.ChecksumMismatch, "二维码传输校验失败，请重新扫描。");

        byte[] decompressed;
        try
        {
            decompressed = OfflineQrProtocol.Decompress(payload);
        }
        catch (InvalidDataException)
        {
            return OfflineQrImportResult.Failure(RosterQrImportError.DecompressionLimit, "二维码内容异常（解压失败或体积过大）。");
        }

        RosterTransferDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<RosterTransferDocument>(decompressed, OfflineQrProtocol.JsonOptions);
        }
        catch (JsonException)
        {
            document = null;
        }

        return document is null
            ? OfflineQrImportResult.Failure(RosterQrImportError.InvalidContent, "二维码内容无效。")
            : new OfflineQrImportResult(document, RosterQrImportError.None, null);
    }

    private RosterQrFrameImportResult ApplyManifest(string[] parts)
    {
        if (parts.Length != 6)
            return RosterQrFrameImportResult.Rejected;

        if (!TryParseInt(parts[3], out var payloadLength) || payloadLength is <= 0 or > OfflineQrProtocol.MaximumPayloadBytes)
            return RosterQrFrameImportResult.Rejected;

        if (!TryParseInt(parts[4], out var totalFrames) || totalFrames is <= 0 or > OfflineQrProtocol.MaximumFrameCount)
            return RosterQrFrameImportResult.Rejected;

        if (parts[5].Length != 64)
            return RosterQrFrameImportResult.Rejected;

        var fileNameBytes = OfflineQrProtocol.DecodeBase64Url(parts[2]);
        if (fileNameBytes is null)
            return RosterQrFrameImportResult.Rejected;

        var fileName = Encoding.UTF8.GetString(fileNameBytes);
        if (fileName.Length == 0)
            return RosterQrFrameImportResult.Rejected;

        // 同一个会话的清单又扫到一次：说明用户在两帧之间重复扫了清单码，保持已收数据不动。
        if (SessionId == parts[1])
            return RosterQrFrameImportResult.Duplicate;

        // 不同会话：上一份名单已经作废（换了码 / 重新生成），清空重开。
        Reset();
        SessionId = parts[1];
        _checksum = parts[5];
        _payloadLength = payloadLength;
        _totalFrames = totalFrames;
        _fileName = fileName;
        return RosterQrFrameImportResult.Accepted;
    }

    private RosterQrFrameImportResult ApplyDataFrame(string[] parts)
    {
        if (parts.Length != 7)
            return RosterQrFrameImportResult.Rejected;

        // 没有清单帧、会话对不上、校验值/长度对不上，都说明这帧不属于当前这份名单，直接丢。
        if (SessionId is null || !string.Equals(parts[1], SessionId, StringComparison.Ordinal))
            return RosterQrFrameImportResult.Rejected;

        if (_checksum is null || !string.Equals(parts[5], _checksum, StringComparison.OrdinalIgnoreCase))
            return RosterQrFrameImportResult.Rejected;

        if (!TryParseInt(parts[4], out var payloadLength) || payloadLength != _payloadLength)
            return RosterQrFrameImportResult.Rejected;

        if (!TryParseInt(parts[2], out var index) || index < 0 || index >= _totalFrames)
            return RosterQrFrameImportResult.Rejected;

        if (!TryParseInt(parts[3], out var offset) || offset < 0 || offset >= _payloadLength)
            return RosterQrFrameImportResult.Rejected;

        byte[] chunk;
        try
        {
            chunk = Convert.FromBase64String(parts[6]);
        }
        catch (FormatException)
        {
            return RosterQrFrameImportResult.Rejected;
        }

        if (chunk.Length == 0)
            return RosterQrFrameImportResult.Rejected;

        // 帧长不写进帧里，靠帧号与偏移反推：offset / index 就是每帧长度。
        // 这样做是为了兼容早期 320 字节一帧的二维码，用户手里可能还留着老码图。
        var inferredLength = index > 0 && offset % index == 0 ? offset / index : chunk.Length;
        var isSingleLegacyFrame = index == 0
                                  && chunk.Length == _payloadLength
                                  && chunk.Length <= OfflineQrProtocol.LegacyFramePayloadLength;
        if (inferredLength is not (OfflineQrProtocol.FramePayloadLength or OfflineQrProtocol.LegacyFramePayloadLength)
            && !isSingleLegacyFrame)
        {
            return RosterQrFrameImportResult.Rejected;
        }

        if (chunk.Length > inferredLength || offset != index * inferredLength)
            return RosterQrFrameImportResult.Rejected;

        return _chunks.TryAdd(index, chunk)
            ? RosterQrFrameImportResult.Accepted
            : RosterQrFrameImportResult.Duplicate;
    }

    /// <summary>切帧、校验前缀与会话长度；任何结构问题都返回 <c>null</c>，由调用方当作 Rejected。</summary>
    private static string[]? ParseFrameParts(string? frame)
    {
        if (string.IsNullOrWhiteSpace(frame))
            return null;

        var parts = frame.Trim().Split('|', 7, StringSplitOptions.None);
        if (parts.Length is not (6 or 7))
            return null;

        if (parts[0] is not (OfflineQrProtocol.ManifestPrefix or OfflineQrProtocol.DataPrefix))
            return null;

        if (parts[1].Length != 32)
            return null;

        return parts;
    }

    private static bool TryParseInt(string? value, out int parsed) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);

    private static byte[]? ParseHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != ChecksumLength)
            return null;

        try
        {
            return Convert.FromHexString(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private const int ChecksumLength = 64;
}
