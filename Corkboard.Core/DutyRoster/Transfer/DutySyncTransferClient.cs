using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Corkboard.Core.DutyRoster.Import;

namespace Corkboard.Core.DutyRoster.Transfer;

/// <summary>云传输的两种模式（离线二维码不经过网络，不在这里）。</summary>
public enum DutyCloudTransferMode
{
    /// <summary>扫码快速传输：二维码里直接带配对链接，密钥在 URL fragment 里。</summary>
    QuickQr = 0,

    /// <summary>会话码传输：12 位会话码，密钥由 PBKDF2 派生。</summary>
    SessionCode = 1
}

/// <summary>一次云传输的句柄（创建成功后的返回值；<see cref="RevokeAsync" /> 需要前两项）。</summary>
public sealed record DutyCloudTransferInfo(
    DutyCloudTransferMode Mode,
    string? TransferId,
    string? UploadToken,
    string? PairingUrl,
    string? SessionCode,
    DateTimeOffset ExpiresAt,
    long PayloadBytes,
    int RecordCount);

/// <summary>云传输失败的分类，界面按它出文案（不要把英文原文直接丢给用户）。</summary>
public enum DutyTransferError
{
    /// <summary>网络不可用、超时或连接被中断。</summary>
    Network = 0,

    /// <summary>会话码无效、已过期或对方还没上传完。</summary>
    InvalidSessionCode = 1,

    /// <summary>配对链接无效或已过期。</summary>
    InvalidPairingCode = 2,

    /// <summary>拿到内容了但解不开（密钥不对、内容被改过、格式不支持）。</summary>
    InvalidPayload = 3,

    /// <summary>服务端返回了其它错误。</summary>
    Server = 4
}

/// <summary>云传输失败。界面只需要 <see cref="Error" />，<see cref="Exception.Message" /> 供日志/排障。</summary>
public sealed class DutyTransferException(DutyTransferError error, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public DutyTransferError Error { get; } = error;
}

/// <summary>
///     SecRandom Sync 匿名传输客户端（与上游 SecRandom 的 <c>RosterSyncTransferService</c> 协议兼容）。
///     <para>
///         没有登录、没有 OAuth、没有重试：客户端把「AES-256-GCM 加密后的信封」一次性 PUT 上去，
///         对方用配对链接或会话码一次性取回；服务端只存密文与会话码哈希，30 天后自动过期，上限 1 MiB。
///         断网等于失败一次，界面要给「重试」入口。
///     </para>
///     <para>
///         加密与压缩都在 <see cref="DutyTransferCipher" /> / <see cref="DutyRosterArchive" /> 里，
///         本类只负责 HTTP 编排；600k 次 PBKDF2 在发送/接收时各跑一次，已用 <see cref="Task.Run" /> 挪到后台。
///     </para>
/// </summary>
public sealed class DutySyncTransferClient(HttpClient httpClient)
{
    /// <summary>服务端地址（与上游 SecRandom 同一个中继；换地址要两边一起换）。</summary>
    public const string PublicUrl = "https://secrandom-sync.sectl.cn";

    private const string RosterContentType = "roster";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>把名单上传成一次云传输；失败时会尽力撤销已创建的传输。</summary>
    public async Task<DutyCloudTransferInfo> CreateAsync(
        RosterTransferDocument document,
        DutyCloudTransferMode mode,
        IReadOnlyDictionary<string, byte[]>? extraEntries = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (mode is not (DutyCloudTransferMode.QuickQr or DutyCloudTransferMode.SessionCode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "离线二维码不经过云传输");

        var archive = DutyRosterArchive.Create(document, extraEntries);
        var keyMaterial = mode == DutyCloudTransferMode.QuickQr
            ? RandomNumberGenerator.GetBytes(32)
            : System.Text.Encoding.UTF8.GetBytes(DutySessionCode.Create());
        var sessionCode = mode == DutyCloudTransferMode.SessionCode
            ? System.Text.Encoding.UTF8.GetString(keyMaterial)
            : null;

        // PBKDF2 是纯 CPU 活，放到后台线程做，别卡 UI。
        var envelope = await Task.Run(() => mode == DutyCloudTransferMode.QuickQr
            ? DutyTransferCipher.EncryptWithRawKey(archive, keyMaterial)
            : DutyTransferCipher.EncryptWithSessionCode(archive, sessionCode!), cancellationToken);

        var payload = DutyTransferCipher.Serialize(envelope);
        var sessionCodeHash = sessionCode is null ? null : DutySessionCode.Sha256Hex(sessionCode);
        var createRequest = new CreateRequest(
            mode == DutyCloudTransferMode.QuickQr ? "quick" : "session",
            payload.LongLength,
            Convert.ToHexString(SHA256.HashData(payload)),
            sessionCodeHash,
            RosterContentType);

        using var createResponse = await SendAsync(() => httpClient.PostAsJsonAsync("v1/transfers", createRequest,
            JsonOptions, cancellationToken), cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(createResponse, DutyTransferError.Server, "创建云传输失败", cancellationToken)
            .ConfigureAwait(false);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateResponse>(JsonOptions, cancellationToken)
                          .ConfigureAwait(false)
                      ?? throw new DutyTransferException(DutyTransferError.Server, "服务端返回了空的传输信息");

        try
        {
            using var payloadContent = new ByteArrayContent(payload);
            payloadContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            payloadContent.Headers.ContentLength = payload.LongLength;
            using var uploadRequest = new HttpRequestMessage(HttpMethod.Put,
                $"v1/transfers/{Uri.EscapeDataString(created.Id)}/payload")
            {
                Content = payloadContent
            };
            uploadRequest.Headers.Add("X-SecRandom-Upload-Token", created.UploadToken);
            using var uploadResponse = await SendAsync(
                    () => httpClient.SendAsync(uploadRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
            await EnsureSuccessAsync(uploadResponse, DutyTransferError.Server, "上传云传输内容失败", cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            var incomplete = new DutyCloudTransferInfo(mode, created.Id, created.UploadToken, null, null,
                created.ExpiresAt, payload.LongLength, document.Rows.Count);
            try
            {
                await RevokeAsync(incomplete, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // 撤销失败没关系：服务端到期会自动清掉，这是最后一道兜底。
            }

            throw;
        }

        var pairingUrl = mode == DutyCloudTransferMode.QuickQr
            ? DutyQuickPairing.Build(PublicUrl, created.Id, keyMaterial)
            : null;
        return new DutyCloudTransferInfo(mode, created.Id, created.UploadToken, pairingUrl, sessionCode,
            created.ExpiresAt, payload.LongLength, document.Rows.Count);
    }

    /// <summary>撤销一次传输（关闭抽屉时调用；404 视为已经过期，不报错）。</summary>
    public async Task RevokeAsync(DutyCloudTransferInfo transfer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        if (string.IsNullOrWhiteSpace(transfer.TransferId) || string.IsNullOrWhiteSpace(transfer.UploadToken))
            return;

        using var request = new HttpRequestMessage(HttpMethod.Delete,
            $"v1/transfers/{Uri.EscapeDataString(transfer.TransferId)}");
        request.Headers.Add("X-SecRandom-Upload-Token", transfer.UploadToken);
        using var response = await SendAsync(() => httpClient.SendAsync(request, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            await EnsureSuccessAsync(response, DutyTransferError.Server, "撤销云传输失败", cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>用扫到的配对链接取回名单。</summary>
    public async Task<RosterTransferDocument> ImportQuickAsync(string pairingUrl,
        CancellationToken cancellationToken = default)
    {
        if (!DutyQuickPairing.TryParse(pairingUrl, out var transferId, out var key))
            throw new DutyTransferException(DutyTransferError.InvalidPairingCode, "这不是有效的名单配对链接");

        using var response = await SendAsync(
                () => httpClient.GetAsync($"v1/transfers/{Uri.EscapeDataString(transferId)}",
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new DutyTransferException(DutyTransferError.InvalidPairingCode, "配对链接已过期或已被撤销");
        await EnsureSuccessAsync(response, DutyTransferError.Server, "下载云传输失败", cancellationToken)
            .ConfigureAwait(false);

        var envelope = await ReadEnvelopeAsync(response.Content, cancellationToken).ConfigureAwait(false);
        return await DecryptAsync(envelope, key, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>用 12 位会话码取回名单。</summary>
    public async Task<RosterTransferDocument> ImportSessionAsync(string sessionCode,
        CancellationToken cancellationToken = default)
    {
        var normalized = DutySessionCode.Normalize(sessionCode);
        if (!DutySessionCode.IsValid(normalized))
            throw new DutyTransferException(DutyTransferError.InvalidSessionCode, "会话码必须是 12 位字母或数字");

        using var response = await SendAsync(
                () => httpClient.PostAsJsonAsync("v1/sessions/resolve",
                    new ResolveSessionRequest(DutySessionCode.Sha256Hex(normalized)), JsonOptions, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new DutyTransferException(DutyTransferError.InvalidSessionCode, "会话码无效或已过期");
        await EnsureSuccessAsync(response, DutyTransferError.Server, "用会话码取回名单失败", cancellationToken)
            .ConfigureAwait(false);

        var envelope = await ReadEnvelopeAsync(response.Content, cancellationToken).ConfigureAwait(false);
        return await DecryptAsync(envelope, System.Text.Encoding.UTF8.GetBytes(normalized), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<RosterTransferDocument> DecryptAsync(DutyTransferEnvelope envelope, byte[] keyMaterial,
        CancellationToken cancellationToken)
    {
        try
        {
            var archive = await Task.Run(() => DutyTransferCipher.Decrypt(envelope, keyMaterial), cancellationToken)
                .ConfigureAwait(false);
            return DutyRosterArchive.Extract(archive);
        }
        catch (InvalidDataException exception)
        {
            throw new DutyTransferException(DutyTransferError.InvalidPayload,
                $"云传输内容无效：{exception.Message}", exception);
        }
    }

    private static async Task<T> SendAsync<T>(Func<Task<T>> send, CancellationToken cancellationToken)
    {
        try
        {
            return await send().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DutyTransferException(DutyTransferError.Network, "连接云服务超时");
        }
        catch (HttpRequestException exception)
        {
            throw new DutyTransferException(DutyTransferError.Network, $"无法连接云服务：{exception.Message}", exception);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, DutyTransferError error, string action,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new DutyTransferException(error, $"{action}（HTTP {(int)response.StatusCode}）：{body}");
    }

    private static async Task<DutyTransferEnvelope> ReadEnvelopeAsync(HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is { } length)
            DutyTransferLimits.EnsurePayloadSize(length, "云传输响应");

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length > DutyTransferLimits.MaxPayloadBytes - read)
                throw new DutyTransferException(DutyTransferError.InvalidPayload, "云传输响应超过 1 MiB 上限");
            buffer.Write(chunk, 0, read);
        }

        try
        {
            return DutyTransferCipher.Deserialize(buffer.ToArray());
        }
        catch (InvalidDataException exception)
        {
            throw new DutyTransferException(DutyTransferError.InvalidPayload, exception.Message, exception);
        }
    }

    private sealed record CreateRequest(
        string Mode,
        long PayloadLength,
        string PayloadSha256,
        string? SessionCodeHash,
        string ContentType);

    private sealed record CreateResponse(string Id, string UploadToken, DateTimeOffset ExpiresAt);

    private sealed record ResolveSessionRequest(string SessionCodeHash);
}