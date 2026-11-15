using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Corkboard.Core.ControlCenter;

namespace Corkboard.Core.Tests;

/// <summary>
///     老师布置的作业：拉取接口的 JSON 解析、回执请求体、截止日期解析与
///     纯文本 → 作业板富文本片段（换行分段、特殊字符转义）。
/// </summary>
public class ControlHomeworkAssignmentTests
{
    private static ControlNodeCredential Credential() => new("dev-1", "sec-1", "SC-6M34RQ");

    // ------------------------------------------------------------------
    // JSON 解析
    // ------------------------------------------------------------------

    [Fact]
    public void Pull_ParsesItemsAndClassRequired()
    {
        var data = JsonNode.Parse(
            """
            {"items":[{"id":"a-1","subject":"数学","section":"第一章","content":"做 P12","dueDate":"2026-11-20","createdAt":"2026-11-15T02:00:00Z"},{"id":"a-2","subject":null,"section":null,"content":"","dueDate":"","createdAt":null}],"classRequired":true}
            """)!.AsObject();

        var pull = ControlHomeworkAssignmentPull.TryParse(data);

        Assert.NotNull(pull);
        Assert.True(pull.ClassRequired);
        Assert.Equal(2, pull.Items.Count);

        var first = pull.Items[0];
        Assert.Equal("a-1", first.Id);
        Assert.Equal("数学", first.Subject);
        Assert.Equal("第一章", first.Section);
        Assert.Equal("做 P12", first.Content);
        Assert.Equal("2026-11-20", first.DueDate);
        Assert.Equal(new DateOnly(2026, 11, 20), first.ResolveDueDate());
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 2, 0, 0, TimeSpan.Zero), first.CreatedAt);

        var second = pull.Items[1];
        Assert.Equal("a-2", second.Id);
        Assert.Null(second.Subject);
        Assert.Equal(string.Empty, second.Content);
        Assert.Null(second.ResolveDueDate());
        Assert.Null(second.CreatedAt);
    }

    [Fact]
    public void Pull_NoClassRequired_DefaultsFalse()
    {
        var data = JsonNode.Parse("""{"items":[]}""")!.AsObject();

        var pull = ControlHomeworkAssignmentPull.TryParse(data);

        Assert.NotNull(pull);
        Assert.False(pull.ClassRequired);
        Assert.Empty(pull.Items);
    }

    [Fact]
    public void Pull_ToleratesSnakeCaseAndSkipsItemsWithoutId()
    {
        var data = JsonNode.Parse(
            """
            {"items":[{"id":"a-1","due_date":"2026-11-20","created_at":"2026-11-15T02:00:00Z"},{"subject":"缺 id"},42],"class_required":true}
            """)!.AsObject();

        var pull = ControlHomeworkAssignmentPull.TryParse(data);

        Assert.NotNull(pull);
        Assert.True(pull.ClassRequired);
        Assert.Single(pull.Items);
        Assert.Equal("2026-11-20", pull.Items[0].DueDate);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 2, 0, 0, TimeSpan.Zero), pull.Items[0].CreatedAt);
    }

    [Fact]
    public void Pull_ItemsNotArray_ReturnsNull()
    {
        Assert.Null(ControlHomeworkAssignmentPull.TryParse(JsonNode.Parse("""{"items":{}}""")!.AsObject()));
        Assert.Null(ControlHomeworkAssignmentPull.TryParse(null));
    }

    // ------------------------------------------------------------------
    // 截止日期
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("2026-11-20", true)]
    [InlineData(" 2026-11-20 ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("2026/11/20", false)]
    [InlineData("2026-13-01", false)]
    [InlineData(null, false)]
    public void TryParseDueDate_OnlyAcceptsIsoDate(string? text, bool expected)
    {
        var parsed = ControlHomeworkAssignment.TryParseDueDate(text, out var dueDate);

        Assert.Equal(expected, parsed);
        if (expected)
            Assert.Equal(new DateOnly(2026, 11, 20), dueDate);
    }

    // ------------------------------------------------------------------
    // 纯文本 → HTML
    // ------------------------------------------------------------------

    [Fact]
    public void ToHtml_WrapsEachLineInParagraph()
    {
        Assert.Equal("<p>第一行</p><p>第二行</p>", ControlHomeworkAssignmentHtml.ToHtml("第一行\n第二行"));
    }

    [Fact]
    public void ToHtml_NormalizesCrLfAndCr()
    {
        Assert.Equal("<p>第一行</p><p>第二行</p>", ControlHomeworkAssignmentHtml.ToHtml("第一行\r\n第二行"));
        Assert.Equal("<p>第一行</p><p>第二行</p>", ControlHomeworkAssignmentHtml.ToHtml("第一行\r第二行"));
    }

    [Fact]
    public void ToHtml_BlankLineBecomesEmptyParagraph()
    {
        Assert.Equal(
            "<p>第一段</p><p><br></p><p>第二段</p>",
            ControlHomeworkAssignmentHtml.ToHtml("第一段\n\n第二段"));
    }

    [Fact]
    public void ToHtml_EscapesMarkupCharacters()
    {
        Assert.Equal(
            "<p>&lt;script&gt; &amp; &quot;x&quot; &lt;/script&gt;</p>",
            ControlHomeworkAssignmentHtml.ToHtml("<script> & \"x\" </script>"));
    }

    [Fact]
    public void ToHtml_EmptyText_ReturnsEmptyParagraph()
    {
        Assert.Equal("<p></p>", ControlHomeworkAssignmentHtml.ToHtml(null));
        Assert.Equal("<p></p>", ControlHomeworkAssignmentHtml.ToHtml(string.Empty));
    }

    // ------------------------------------------------------------------
    // 拉取接口
    // ------------------------------------------------------------------

    [Fact]
    public async Task PullSuccess_SendsGetWithQueryCredentials_AndParsesPull()
    {
        const string body = """
                            {"ok":true,"data":{"items":[{"id":"a-1","subject":"数学","section":"第一章","content":"做 P12","dueDate":"2026-11-20","createdAt":"2026-11-15T02:00:00Z"}],"classRequired":true}}
                            """;
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, body));
        using var api = CreateApi(handler);

        var result = await api.PullHomeworkAssignmentsAsync(Credential(), TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.Null(result.ErrorKey);
        Assert.NotNull(result.Pull);
        Assert.True(result.Pull.ClassRequired);
        Assert.Single(result.Pull.Items);
        Assert.Equal("a-1", result.Pull.Items[0].Id);

        var uri = handler.LastRequest!.RequestUri!;
        Assert.Equal("/control/api/device/homework-assignments", uri.AbsolutePath);
        Assert.Equal("?deviceId=dev-1&secret=sec-1", uri.Query);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
    }

    [Fact]
    public async Task Pull_InvalidBody_ReturnsInvalidResponse()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"ok":true,"data":{}}"""));
        using var api = CreateApi(handler);

        var result = await api.PullHomeworkAssignmentsAsync(Credential(), TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Equal(ControlApiErrorKeys.InvalidResponse, result.ErrorKey);
    }

    [Fact]
    public async Task Pull_ServerError_MapsErrorKey()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Forbidden,
            """{"ok":false,"error":{"code":"device_revoked","message":"gone"}}"""));
        using var api = CreateApi(handler);

        var result = await api.PullHomeworkAssignmentsAsync(Credential(), TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Equal(ControlApiErrorKeys.Forbidden, result.ErrorKey);
        Assert.Equal("gone", result.Message);
    }

    [Fact]
    public async Task Pull_IncompleteCredential_DoesNotSend()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));
        using var api = CreateApi(handler);

        var result = await api.PullHomeworkAssignmentsAsync(
            new ControlNodeCredential("dev-1", string.Empty, "SC-6M34RQ"),
            TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Equal(ControlApiErrorKeys.NotEnrolled, result.ErrorKey);
        Assert.Null(handler.LastRequest);
    }

    // ------------------------------------------------------------------
    // 回执接口
    // ------------------------------------------------------------------

    [Fact]
    public async Task AckApplied_PostsNoteId()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"ok":true}"""));
        using var api = CreateApi(handler);

        var result = await api.AckHomeworkAssignmentAsync(
            Credential(),
            "a-1",
            true,
            "7f3c2e9a-0000-0000-0000-000000000001",
            null,
            TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.Equal("/control/api/device/homework-assignments/a-1/ack", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal("?deviceId=dev-1&secret=sec-1", handler.LastRequest.RequestUri.Query);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);

        var sent = JsonNode.Parse(handler.LastBody!)!.AsObject();
        Assert.True(sent["applied"]!.GetValue<bool>());
        Assert.Equal("7f3c2e9a-0000-0000-0000-000000000001", sent["noteId"]!.GetValue<string>());
        // body 按契约只放回执本身，凭证在 query 里。
        Assert.False(sent.ContainsKey("reason"));
        Assert.False(sent.ContainsKey("deviceId"));
    }

    [Fact]
    public async Task AckNotApplied_PostsReason()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"ok":true}"""));
        using var api = CreateApi(handler);

        var result = await api.AckHomeworkAssignmentAsync(
            Credential(),
            "a-1",
            false,
            null,
            "落盘失败",
            TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        var sent = JsonNode.Parse(handler.LastBody!)!.AsObject();
        Assert.False(sent["applied"]!.GetValue<bool>());
        Assert.Equal("落盘失败", sent["reason"]!.GetValue<string>());
        Assert.False(sent.ContainsKey("noteId"));
    }

    [Fact]
    public async Task Ack_ServerError_MapsErrorKey()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Unauthorized,
            """{"ok":false,"error":{"code":"bad_secret"}}"""));
        using var api = CreateApi(handler);

        var result = await api.AckHomeworkAssignmentAsync(
            Credential(), "a-1", true, "note-1", null, TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Equal(ControlApiErrorKeys.BadSecret, result.ErrorKey);
    }

    [Fact]
    public void AckPath_EscapesAssignmentIdAndUsesFixedSuffix()
    {
        Assert.Equal(
            "/control/api/device/homework-assignments/a-1/ack",
            ControlV4Protocol.HomeworkAssignmentAckPath("a-1"));
        Assert.Equal(
            "/control/api/device/homework-assignments/a%2Fb/ack",
            ControlV4Protocol.HomeworkAssignmentAckPath("a/b"));
    }

    // ------------------------------------------------------------------

    private static ControlNodeEnrollmentApi CreateApi(HttpMessageHandler handler) =>
        new(new ControlNodeConnectOptions { ServerUrl = "https://example.test" }, new HttpClient(handler));

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
}