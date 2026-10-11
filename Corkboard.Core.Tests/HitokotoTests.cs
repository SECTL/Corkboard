using System.Net;
using System.Text;
using System.Text.Json;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Models.SubConfigs.Hitokoto;
using Corkboard.Core.Services.Hitokoto;

namespace Corkboard.Core.Tests;

/// <summary>一言配置、请求参数、屏蔽词与 HTTP 获取的单测，不访问真实接口。</summary>
public class HitokotoTests
{
    [Fact]
    public void Settings_DefaultsToDisabledWithAllCategories()
    {
        var settings = new HitokotoSettingsConfig();

        Assert.False(settings.Enabled);
        Assert.Equal(5, settings.RefreshMinutes);
        Assert.Empty(settings.Categories);
        Assert.Empty(settings.BlockedWords);
        Assert.True(settings.ShowSource);
        Assert.True(settings.ShowAuthor);
    }

    [Fact]
    public void Settings_AreObservableAndRoundTripWithConfigOptions()
    {
        var settings = new HitokotoSettingsConfig();
        var changes = new List<string?>();
        settings.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        settings.Enabled = true;
        settings.RefreshMinutes = 12;
        settings.Categories = "acl";
        settings.BlockedWords = "第一行\nSecond";
        settings.ShowSource = false;
        settings.ShowAuthor = false;

        Assert.Equal(new[] { "Enabled", "RefreshMinutes", "Categories", "BlockedWords", "ShowSource", "ShowAuthor" },
            changes);
        var json = JsonSerializer.Serialize(settings, ConfigServiceBase.JsonOptions);
        Assert.Contains("\"refresh_minutes\": 12", json);
        Assert.Contains("\"blocked_words\"", json);
        Assert.Contains("\"show_source\": false", json);
        Assert.Contains("\"show_author\": false", json);

        var restored = JsonSerializer.Deserialize<HitokotoSettingsConfig>(json, ConfigServiceBase.JsonOptions)!;
        Assert.True(restored.Enabled);
        Assert.Equal(12, restored.RefreshMinutes);
        Assert.Equal("acl", restored.Categories);
        Assert.Equal(settings.BlockedWords, restored.BlockedWords);
        Assert.False(restored.ShowSource);
        Assert.False(restored.ShowAuthor);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("mzABCZ 1&?", "")]
    [InlineData(" aalc-lbmzA? ", "&c=a&c=l&c=c&c=b")]
    [InlineData("abcdefghijkl", "&c=a&c=b&c=c&c=d&c=e&c=f&c=g&c=h&c=i&c=j&c=k&c=l")]
    public void BuildRequestUri_UsesOnlyDistinctLowercaseCategories(string? categories, string categoryQuery)
    {
        var uri = HitokotoPolicy.BuildRequestUri(categories);

        Assert.Equal($"https://v1.hitokoto.cn/?encode=json&max_length=120{categoryQuery}", uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("A BAD sentence", "Book", "Writer", "\r\n  bad \t\nunused", true)]
    [InlineData("A sentence", "BAD book", "Writer", "  bad  ", true)]
    [InlineData("A sentence", "Book", "Bad Writer", "unused\rbAD", true)]
    [InlineData("正文", null, "含屏蔽词的作者", "屏蔽词", true)]
    [InlineData("A sentence", null, null, null, false)]
    [InlineData("A sentence", "Book", "Writer", "\r\n \t \n", false)]
    [InlineData("alpha", null, null, "alpha beta", false)]
    [InlineData("A sentence", "Book", "Writer", "other", false)]
    public void IsBlocked_ChecksTextAndMetadataIgnoringCase(string text, string? source, string? author,
        string? words, bool expected)
    {
        Assert.Equal(expected, HitokotoPolicy.IsBlocked(new HitokotoSentence(text, source, author), words));
    }

    [Fact]
    public async Task GetAsync_MapsApiFieldsAndUsesSettingsCategories()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(JsonResponse("""
            { "id": 123, "hitokoto": "今日也要加油", "from": "一本书", "from_who": "作者" }
            """)));
        using var http = new HttpClient(handler);
        var settings = new HitokotoSettingsConfig { Categories = "aael" };

        var sentence = await new HitokotoClient(http).GetAsync(settings, TestContext.Current.CancellationToken);

        Assert.Equal(new HitokotoSentence("今日也要加油", "一本书", "作者"), sentence);
        Assert.Equal(HitokotoPolicy.BuildRequestUri("ael"), Assert.Single(handler.RequestUris));
    }

    [Theory]
    [InlineData("""{ "hitokoto": "正文", "from": null, "from_who": null }""")]
    [InlineData("""{ "hitokoto": "正文" }""")]
    public async Task GetAsync_AllowsNullOrMissingMetadata(string json)
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(json)));
        using var http = new HttpClient(handler);

        var sentence = await new HitokotoClient(http).GetAsync(new HitokotoSettingsConfig(),
            TestContext.Current.CancellationToken);

        Assert.NotNull(sentence);
        Assert.Equal("正文", sentence.Text);
        Assert.Null(sentence.Source);
        Assert.Null(sentence.Author);
        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task GetAsync_RetriesBlockedTextAndHiddenMetadataUntilAccepted()
    {
        var responses = new Queue<string>([
            """{ "hitokoto": "BAD sentence" }""",
            """{ "hitokoto": "A sentence", "from": "BAD book", "from_who": "BAD writer" }""",
            """{ "hitokoto": "可显示的正文", "from": "来源", "from_who": "作者" }"""
        ]);
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(responses.Dequeue())));
        using var http = new HttpClient(handler);
        var settings = new HitokotoSettingsConfig { BlockedWords = "bad", ShowSource = false, ShowAuthor = false };

        var sentence = await new HitokotoClient(http).GetAsync(settings, TestContext.Current.CancellationToken);

        Assert.Equal(new HitokotoSentence("可显示的正文", "来源", "作者"), sentence);
        Assert.Equal(3, handler.RequestUris.Count);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullAfterThreeBlockedResponses()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse("""{ "hitokoto": "blocked" }""")));
        using var http = new HttpClient(handler);

        var sentence = await new HitokotoClient(http).GetAsync(new HitokotoSettingsConfig { BlockedWords = "blocked" },
            TestContext.Current.CancellationToken);

        Assert.Null(sentence);
        Assert.Equal(3, handler.RequestUris.Count);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{ "hitokoto": null }""")]
    [InlineData("""{ "hitokoto": "" }""")]
    [InlineData("""{ "hitokoto": " \t\r\n " }""")]
    public async Task GetAsync_NeverReturnsAnEmptySentence(string json)
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(json)));
        using var http = new HttpClient(handler);

        var sentence = await new HitokotoClient(http).GetAsync(new HitokotoSettingsConfig(),
            TestContext.Current.CancellationToken);

        Assert.Null(sentence);
        Assert.Equal(3, handler.RequestUris.Count);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "hitokoto": 123 }""")]
    public async Task GetAsync_PropagatesJsonErrorsWithoutRetry(string json)
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(json)));
        using var http = new HttpClient(handler);

        await Assert.ThrowsAsync<JsonException>(() => new HitokotoClient(http).GetAsync(new HitokotoSettingsConfig(),
            TestContext.Current.CancellationToken));

        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task GetAsync_PropagatesNetworkErrorsWithoutRetry()
    {
        var error = new HttpRequestException("网络失败");
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromException<HttpResponseMessage>(error));
        using var http = new HttpClient(handler);

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new HitokotoClient(http).GetAsync(new HitokotoSettingsConfig(), TestContext.Current.CancellationToken));

        Assert.Same(error, thrown);
        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task GetAsync_PropagatesHttpErrorsWithoutRetry()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var http = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new HitokotoClient(http).GetAsync(new HitokotoSettingsConfig(), TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Single(handler.RequestUris);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task GetAsync_ForwardsCancellationOnEveryAttempt(int cancelOnAttempt)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var attempts = 0;
        var handler = new FakeHttpMessageHandler((_, token) =>
        {
            if (++attempts == cancelOnAttempt)
            {
                cancellation.Cancel();
                Assert.True(token.IsCancellationRequested);
                token.ThrowIfCancellationRequested();
            }
            return Task.FromResult(JsonResponse("""{ "hitokoto": "blocked" }"""));
        });
        using var http = new HttpClient(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new HitokotoClient(http).GetAsync(new HitokotoSettingsConfig { BlockedWords = "blocked" }, cancellation.Token));

        Assert.Equal(cancelOnAttempt, handler.RequestUris.Count);
    }

    [Fact]
    public async Task GetAsync_CanCancelWhileWaitingToRetry()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse("""{ "hitokoto": "blocked" }""")));
        using var http = new HttpClient(handler);

        var fetch = new HitokotoClient(http).GetAsync(new HitokotoSettingsConfig { BlockedWords = "blocked" },
            cancellation.Token);
        Assert.False(fetch.IsCompleted);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch);
        Assert.Single(handler.RequestUris);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            RequestUris.Add(request.RequestUri!);
            return respond(request, cancellationToken);
        }
    }
}
