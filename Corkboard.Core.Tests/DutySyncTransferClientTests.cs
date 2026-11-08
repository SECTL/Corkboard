using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Corkboard.Core.DutyRoster.Import;
using Corkboard.Core.DutyRoster.Transfer;

namespace Corkboard.Core.Tests;

/// <summary>
///     <see cref="DutySyncTransferClient" /> 的单测：用一个内存假服务端模拟 SecRandom Sync 的
///     创建 / 上传 / 取回 / 撤销流程，验证协议字段、加密闭环与错误分类。不联网。
/// </summary>
public sealed class DutySyncTransferClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task QuickTransferRoundTripsThroughFakeServer()
    {
        await using var server = new FakeSyncServer();
        var client = CreateClient(server);

        var info = await client.CreateAsync(CreateDocument(), DutyCloudTransferMode.QuickQr, cancellationToken: Token);
        Assert.Equal(DutyCloudTransferMode.QuickQr, info.Mode);
        Assert.Null(info.SessionCode);
        Assert.NotNull(info.PairingUrl);
        Assert.StartsWith($"{DutySyncTransferClient.PublicUrl}/#t=", info.PairingUrl);
        Assert.Equal(2, info.RecordCount);
        Assert.True(info.PayloadBytes > 0);
        Assert.True(server.LastCreateRequest!.Value.GetProperty("mode").GetString() == "quick");

        var imported = await client.ImportQuickAsync(info.PairingUrl!, Token);
        AssertDocumentEqual(CreateDocument(), imported);

        // 撤销：DELETE 一次，带上上传令牌。
        await client.RevokeAsync(info, Token);
        Assert.True(server.RevokedTransferId == info.TransferId);
    }

    [Fact]
    public async Task SessionTransferRoundTripsThroughFakeServer()
    {
        await using var server = new FakeSyncServer();
        var client = CreateClient(server);

        var info = await client.CreateAsync(CreateDocument(), DutyCloudTransferMode.SessionCode, cancellationToken: Token);
        Assert.Equal(DutyCloudTransferMode.SessionCode, info.Mode);
        Assert.True(DutySessionCode.IsValid(info.SessionCode));
        Assert.Null(info.PairingUrl);

        var expectedHash = DutySessionCode.Sha256Hex(info.SessionCode!);
        Assert.Equal(expectedHash, server.LastCreateRequest!.Value.GetProperty("sessionCodeHash").GetString());

        var imported = await client.ImportSessionAsync(info.SessionCode!, Token);
        AssertDocumentEqual(CreateDocument(), imported);

        // 会话码里带空格/短横线也要能归一化后导入。
        var messy = info.SessionCode!;
        var withSeparators = $"{messy[..4]}-{messy[4..8]} {messy[8..]}";
        var importedAgain = await client.ImportSessionAsync(withSeparators, Token);
        AssertDocumentEqual(CreateDocument(), importedAgain);
    }

    [Fact]
    public async Task UnknownSessionCodeIsReportedAsInvalidSessionCode()
    {
        await using var server = new FakeSyncServer();
        var client = CreateClient(server);

        var exception = await Assert.ThrowsAsync<DutyTransferException>(() =>
            client.ImportSessionAsync("ABCDEFGHJKLM", Token));
        Assert.Equal(DutyTransferError.InvalidSessionCode, exception.Error);
    }

    [Fact]
    public async Task ShortSessionCodeIsRejectedBeforeAnyRequest()
    {
        await using var server = new FakeSyncServer();
        var client = CreateClient(server);

        var exception = await Assert.ThrowsAsync<DutyTransferException>(() => client.ImportSessionAsync("ABC", Token));
        Assert.Equal(DutyTransferError.InvalidSessionCode, exception.Error);
        Assert.Equal(0, server.RequestCount);
    }

    [Fact]
    public async Task MissingPairingUrlIsReportedAsInvalidPairingCode()
    {
        await using var server = new FakeSyncServer();
        var client = CreateClient(server);

        var exception = await Assert.ThrowsAsync<DutyTransferException>(() =>
            client.ImportQuickAsync("https://example.com/#t=short&k=AAAA", Token));
        Assert.Equal(DutyTransferError.InvalidPairingCode, exception.Error);
    }

    [Fact]
    public async Task ExpiredPairingIsReportedAsInvalidPairingCode()
    {
        await using var server = new FakeSyncServer();
        var client = CreateClient(server);

        var transferId = new string('a', DutyQuickPairing.TransferIdLength);
        var key = new byte[DutyQuickPairing.KeyLength];
        var url = DutyQuickPairing.Build(DutySyncTransferClient.PublicUrl, transferId, key);

        var exception = await Assert.ThrowsAsync<DutyTransferException>(() => client.ImportQuickAsync(url, Token));
        Assert.Equal(DutyTransferError.InvalidPairingCode, exception.Error);
    }

    [Fact]
    public async Task ServerErrorIsReportedAsServer()
    {
        await using var server = new FakeSyncServer { FailCreate = true };
        var client = CreateClient(server);

        var exception = await Assert.ThrowsAsync<DutyTransferException>(() =>
            client.CreateAsync(CreateDocument(), DutyCloudTransferMode.QuickQr, cancellationToken: Token));
        Assert.Equal(DutyTransferError.Server, exception.Error);
    }

    private static DutySyncTransferClient CreateClient(FakeSyncServer server) =>
        new(new HttpClient(server) { BaseAddress = new Uri($"{DutySyncTransferClient.PublicUrl}/") });

    private static void AssertDocumentEqual(RosterTransferDocument expected, RosterTransferDocument actual)
    {
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.FileName, actual.FileName);
        Assert.Equal(expected.Rows, actual.Rows);
    }

    private static RosterTransferDocument CreateDocument() => new(
        OfflineQrProtocol.DocumentVersion,
        RosterTransferKind.DutyRoster,
        "值日名单.secrandom-roster.json",
        [
            new RosterTransferRow(true, "01", "张三", "男", "第一组", "一组"),
            new RosterTransferRow(false, "02", "李四", "女", null, null)
        ]);

    /// <summary>内存假服务端：把信封存在字典里，按 SecRandom Sync 的路径与头字段响应。</summary>
    private sealed class FakeSyncServer : HttpMessageHandler, IAsyncDisposable
    {
        private readonly Dictionary<string, (string UploadToken, byte[] Payload)> _transfers = new();
        private readonly Dictionary<string, string> _sessions = new();
        private int _nextId;

        public bool FailCreate { get; init; }

        public int RequestCount { get; private set; }

        public JsonElement? LastCreateRequest { get; private set; }

        public string? RevokedTransferId { get; private set; }

        public ValueTask DisposeAsync()
        {
            _transfers.Clear();
            _sessions.Clear();
            return ValueTask.CompletedTask;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');

            if (request.Method == HttpMethod.Post && path == "v1/transfers")
            {
                if (FailCreate)
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);

                var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                LastCreateRequest = json.RootElement.Clone();
                var id = $"transfer{Interlocked.Increment(ref _nextId):D16}";
                var token = $"token-{_nextId}";
                _transfers[id] = (token, []);
                var sessionHash = json.RootElement.TryGetProperty("sessionCodeHash", out var hashElement) &&
                                  hashElement.ValueKind == JsonValueKind.String
                    ? hashElement.GetString()
                    : null;
                if (sessionHash is not null)
                    _sessions[sessionHash] = id;

                return Json(new { id, uploadToken = token, expiresAt = DateTimeOffset.UtcNow.AddDays(30) });
            }

            if (request.Method == HttpMethod.Put && path.StartsWith("v1/transfers/", StringComparison.Ordinal) &&
                path.EndsWith("/payload", StringComparison.Ordinal))
            {
                var id = path["v1/transfers/".Length..^"/payload".Length];
                if (!_transfers.TryGetValue(id, out var transfer))
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                if (!request.Headers.TryGetValues("X-SecRandom-Upload-Token", out var tokens) ||
                    tokens.FirstOrDefault() != transfer.UploadToken)
                {
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                }

                var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                _transfers[id] = (transfer.UploadToken, body);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            if (request.Method == HttpMethod.Get && path.StartsWith("v1/transfers/", StringComparison.Ordinal))
            {
                var id = path["v1/transfers/".Length..];
                return _transfers.TryGetValue(id, out var transfer) && transfer.Payload.Length > 0
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(transfer.Payload)
                    }
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Delete && path.StartsWith("v1/transfers/", StringComparison.Ordinal))
            {
                var id = path["v1/transfers/".Length..];
                if (!_transfers.Remove(id))
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                RevokedTransferId = id;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Post && path == "v1/sessions/resolve")
            {
                var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var hash = json.RootElement.GetProperty("sessionCodeHash").GetString()!;
                if (!_sessions.TryGetValue(hash, out var id) ||
                    !_transfers.TryGetValue(id, out var transfer) || transfer.Payload.Length == 0)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(transfer.Payload) };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json")
        };
    }
}

