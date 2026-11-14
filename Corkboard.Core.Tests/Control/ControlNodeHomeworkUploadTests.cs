using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Corkboard.Core.ControlCenter;

namespace Corkboard.Core.Tests;

/// <summary>
///     作业快照上传（<c>POST /control/api/device/homework-snapshots</c>）：
///     成功时解析 <c>snapshotId / receivedAt / evicted</c>、错误码到本地化键的映射、
///     网络异常与「没有凭证」的兜底。
/// </summary>
public class ControlNodeHomeworkUploadTests
{
    private static ControlNodeCredential Credential() => new("dev-1", "sec-1", "SC-6M34RQ");

    private static JsonObject Snapshot() => new()
    {
        ["generatedAt"] = "2026-11-15T03:00:00+08:00",
        ["noteCount"] = 1,
        ["truncated"] = false,
        ["subjects"] = new JsonArray()
    };

    [Fact]
    public async Task Upload_Success_ParsesSnapshotIdReceivedAtAndEvicted()
    {
        const string body = """
                            {"ok":true,"data":{"snapshotId":"snap-1","receivedAt":"2026-11-15T03:00:00Z","noteCount":2,"subjectCount":1,"classId":"c-1","className":"一班","bytes":321,"quota":{"used":1,"max":10},"evicted":3}}
                            """;
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, body));
        using var api = CreateApi(handler, new ControlNodeConnectOptions { ServerUrl = "https://example.test" });

        var result = await api.UploadHomeworkSnapshotAsync(
            Credential(), Snapshot(), "c-1", TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.Equal("snap-1", result.SnapshotId);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 3, 0, 0, TimeSpan.Zero), result.ReceivedAt);
        Assert.Equal(3, result.Evicted);
        Assert.Null(result.ErrorKey);

        Assert.Equal("/control/api/device/homework-snapshots", handler.LastRequest!.RequestUri!.AbsolutePath);

        var sent = JsonNode.Parse(handler.LastBody!)!;
        Assert.Equal("dev-1", sent["deviceId"]!.GetValue<string>());
        Assert.Equal("sec-1", sent["secret"]!.GetValue<string>());
        Assert.Equal("c-1", sent["classId"]!.GetValue<string>());
        Assert.Equal(1, sent["snapshot"]!["noteCount"]!.GetValue<int>());
        Assert.Equal("2026-11-15T03:00:00+08:00", sent["snapshot"]!["generatedAt"]!.GetValue<string>());
    }

    [Fact]
    public async Task Upload_NoClass_SendsNullClassId()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created,
            """{"ok":true,"data":{"snapshotId":"snap-1"}}"""));
        using var api = CreateApi(handler);

        var result = await api.UploadHomeworkSnapshotAsync(
            Credential(), Snapshot(), null, TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        var sent = JsonNode.Parse(handler.LastBody!)!.AsObject();
        Assert.True(sent.ContainsKey("classId"));
        Assert.Null(sent["classId"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "bad_secret", ControlApiErrorKeys.BadSecret)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "payload_too_large", ControlApiErrorKeys.PayloadTooLarge)]
    [InlineData(HttpStatusCode.TooManyRequests, "too_many_requests", ControlApiErrorKeys.RateLimited)]
    [InlineData(HttpStatusCode.Forbidden, "device_revoked", ControlApiErrorKeys.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, "device_not_found", ControlApiErrorKeys.DeviceNotFound)]
    [InlineData(HttpStatusCode.BadRequest, "bad_request", ControlApiErrorKeys.BadRequest)]
    public async Task Upload_ServerError_MapsErrorKey(
        HttpStatusCode statusCode,
        string errorCode,
        string expectedErrorKey)
    {
        var handler = new StubHandler(_ => Json(statusCode,
            $$$"""{"ok":false,"error":{"code":"{{{errorCode}}}","message":"nope"}}"""));
        using var api = CreateApi(handler);

        var result = await api.UploadHomeworkSnapshotAsync(
            Credential(), Snapshot(), "c-1", TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Equal(expectedErrorKey, result.ErrorKey);
        Assert.Equal("nope", result.Message);
    }

    [Fact]
    public async Task Upload_HttpStatusWithoutErrorBody_StillMapsByStatus()
    {
        // 中间层（反代 / WAF）挡下来时没有我们的错误体，只能按 HTTP 状态映射。
        var handler = new StubHandler(_ => Json(HttpStatusCode.RequestEntityTooLarge, "{}"));
        using var api = CreateApi(handler);

        var result = await api.UploadHomeworkSnapshotAsync(
            Credential(), Snapshot(), null, TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Equal(ControlApiErrorKeys.PayloadTooLarge, result.ErrorKey);
    }

    [Fact]
    public async Task Upload_NetworkException_MapsNetworkKey()
    {
        using var api = CreateApi(new ThrowingHandler());

        var result = await api.UploadHomeworkSnapshotAsync(
            Credential(), Snapshot(), null, TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Equal(ControlApiErrorKeys.Network, result.ErrorKey);
    }

    [Fact]
    public async Task Upload_IncompleteCredential_DoesNotSendAndReportsNotEnrolled()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, "{}"));
        using var api = CreateApi(handler);

        var result = await api.UploadHomeworkSnapshotAsync(
            new ControlNodeCredential("dev-1", string.Empty, "SC-6M34RQ"),
            Snapshot(),
            null,
            TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Equal(ControlApiErrorKeys.NotEnrolled, result.ErrorKey);
        Assert.Null(handler.LastRequest);
    }

    private static ControlNodeEnrollmentApi CreateApi(
        HttpMessageHandler handler,
        ControlNodeConnectOptions? options = null) =>
        new(options ?? new ControlNodeConnectOptions(), new HttpClient(handler));

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return respond(request);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("boom");
    }
}