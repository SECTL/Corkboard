using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Corkboard.Core.ControlCenter;
using Corkboard.Core.Models.SubConfigs.ControlCenter;

namespace Corkboard.Core.Tests;

/// <summary>
///     集控接入与退订的设备名语义：
///     设置里留空时必须报电脑名（否则服务端把设备记录落成「未命名设备」，之后连上来也不会改），
///     「清空接入」要向服务端退订这一台设备，而不是只删掉本地凭证。
/// </summary>
public class ControlEnrollmentNameTests
{
    [Fact]
    public async Task Enroll_NullDeviceName_SendsMachineName()
    {
        var handler = new StubHandler(_ => EnrollResponse());
        using var api = CreateApi(handler);
        await using var client = CreateClient(new FakeCredentialProvider(), api, displayName: string.Empty);

        var (ok, errorKey) = await client.EnrollAsync(
            "SC-6M34RQ", "K7Q2-9XMD", null, TestContext.Current.CancellationToken);

        Assert.True(ok, errorKey);
        Assert.Equal(Environment.MachineName, ReadPayload(handler)["deviceName"]?.GetValue<string>());
    }

    [Fact]
    public async Task Enroll_WhitespaceDeviceName_SendsMachineName()
    {
        var handler = new StubHandler(_ => EnrollResponse());
        using var api = CreateApi(handler);
        await using var client = CreateClient(new FakeCredentialProvider(), api, displayName: "   ");

        var (ok, errorKey) = await client.EnrollAsync(
            "SC-6M34RQ", "K7Q2-9XMD", "  ", TestContext.Current.CancellationToken);

        Assert.True(ok, errorKey);
        Assert.Equal(Environment.MachineName, ReadPayload(handler)["deviceName"]?.GetValue<string>());
    }

    [Fact]
    public async Task Enroll_ExplicitDeviceName_IsTrimmedAndWins()
    {
        var handler = new StubHandler(_ => EnrollResponse());
        using var api = CreateApi(handler);
        await using var client = CreateClient(new FakeCredentialProvider(), api, displayName: string.Empty);

        var (ok, errorKey) = await client.EnrollAsync(
            "SC-6M34RQ", "K7Q2-9XMD", "  讲台机  ", TestContext.Current.CancellationToken);

        Assert.True(ok, errorKey);
        Assert.Equal("讲台机", ReadPayload(handler)["deviceName"]?.GetValue<string>());
    }

    [Fact]
    public async Task Enroll_ConfiguredDeviceName_IsTrimmed()
    {
        var handler = new StubHandler(_ => EnrollResponse());
        using var api = CreateApi(handler);
        await using var client = CreateClient(new FakeCredentialProvider(), api, displayName: "  讲台机  ");

        var (ok, errorKey) = await client.EnrollAsync(
            "SC-6M34RQ", "K7Q2-9XMD", null, TestContext.Current.CancellationToken);

        Assert.True(ok, errorKey);
        Assert.Equal("讲台机", ReadPayload(handler)["deviceName"]?.GetValue<string>());
    }

    [Fact]
    public async Task Forget_UnenrollsOnServer_ThenClearsCredential()
    {
        var handler = new StubHandler(_ => Json(
            HttpStatusCode.OK,
            """{"ok":true,"data":{"deviceId":"dev-1","siteId":"SC-6M34RQ","deviceName":"讲台机"}}"""));
        using var api = CreateApi(handler);
        var credentials = new FakeCredentialProvider(Credential());
        await using var client = CreateClient(credentials, api, displayName: string.Empty);

        await client.ForgetEnrollmentAsync();

        Assert.Equal("/control/api/device/unenroll", handler.LastRequest?.RequestUri?.AbsolutePath);
        var payload = ReadPayload(handler);
        Assert.Equal("dev-1", payload["deviceId"]?.GetValue<string>());
        Assert.Equal("sec-1", payload["secret"]?.GetValue<string>());
        Assert.Null(credentials.Current);
        Assert.Equal(1, credentials.ClearCount);
    }

    [Fact]
    public async Task Forget_UnenrollFails_StillClearsCredential()
    {
        var handler = new ThrowingHandler();
        using var api = CreateApi(handler);
        var credentials = new FakeCredentialProvider(Credential());
        await using var client = CreateClient(credentials, api, displayName: string.Empty);

        await client.ForgetEnrollmentAsync();

        Assert.Null(credentials.Current);
        Assert.Equal(1, credentials.ClearCount);
    }

    private static ControlNodeCredential Credential() => new("dev-1", "sec-1", "SC-6M34RQ");

    private static ControlNodeClient CreateClient(
        FakeCredentialProvider credentials,
        ControlNodeEnrollmentApi api,
        string displayName) =>
        new(
            new ControlCenterSettingsConfig
            {
                DisplayName = displayName,
                Enabled = false,
                ServerUrl = "https://example.test"
            },
            credentials: credentials,
            stateStore: new FakeStateStore(),
            transportFactory: new FakeControlNodeTransportFactory(),
            enrollmentApi: api);

    private static ControlNodeEnrollmentApi CreateApi(HttpMessageHandler handler) =>
        new(
            new ControlNodeConnectOptions { ServerUrl = "https://example.test" },
            new HttpClient(handler));

    private static JsonObject ReadPayload(StubHandler handler) =>
        JsonNode.Parse(handler.LastBody ?? "{}") as JsonObject ?? [];

    private static HttpResponseMessage EnrollResponse() => Json(
        HttpStatusCode.Created,
        """{"ok":true,"data":{"deviceId":"dev-1","secret":"sec-1","site":{"publicId":"SC-6M34RQ"}}}""");

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