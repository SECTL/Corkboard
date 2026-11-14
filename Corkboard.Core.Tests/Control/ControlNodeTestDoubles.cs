using System.Text.Json.Nodes;
using System.Threading.Channels;
using Corkboard.Core.ControlCenter;

namespace Corkboard.Core.Tests;

/// <summary>
///     进程内的假长连接：发的帧都记下来交给测试断言，测试也能反向往里塞帧。
///     <para>
///         会话层只交换已序列化的 JSON 字符串（<see cref="IControlNodeTransport" />），
///         所以单测不需要起真的 WebSocket 服务端。
///     </para>
/// </summary>
internal sealed class FakeControlNodeTransport : IControlNodeTransport
{
    private readonly Channel<string> _inbox = Channel.CreateUnbounded<string>();
    private readonly List<ControlFrame> _sent = [];

    /// <summary>收到客户端发出的帧时的服务端桩（可为 <c>null</c>）。</summary>
    public Func<ControlFrame, Task>? OnSend { get; set; }

    /// <summary>客户端已经发出来的帧（按发送顺序）。</summary>
    public IReadOnlyList<ControlFrame> Sent
    {
        get
        {
            lock (_sent)
                return _sent.ToArray();
        }
    }

    public string? CloseReason { get; private set; }

    public int? CloseCode { get; private set; }

    /// <summary>取某个 topic 的帧。</summary>
    public ControlFrame[] FramesOf(string topic) => Sent.Where(frame => frame.Topic == topic).ToArray();

    public async Task SendAsync(string frameJson, CancellationToken cancellationToken)
    {
        var frame = ControlV4Protocol.TryParse(frameJson)
                    ?? throw new InvalidOperationException("测试收到无法解析的帧：" + frameJson);

        lock (_sent)
            _sent.Add(frame);

        if (OnSend is not null)
            await OnSend(frame).ConfigureAwait(false);
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _inbox.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    /// <summary>往客户端推一条事件帧。</summary>
    public ValueTask PushEventAsync(string topic, JsonObject payload) =>
        PushAsync(new ControlFrame { Type = ControlFrameTypes.Event, Topic = topic, Payload = payload });

    /// <summary>推一帧原始帧（测试里也用来推握手回包）。</summary>
    public ValueTask PushAsync(ControlFrame frame) =>
        _inbox.Writer.WriteAsync(ControlV4Protocol.Serialize(frame));

    /// <summary>模拟服务端断开：之后 <see cref="ReceiveAsync" /> 返回 <c>null</c>。</summary>
    public void Close(int? code = null, string? reason = null)
    {
        CloseCode = code;
        CloseReason = reason;
        _inbox.Writer.TryComplete();
    }

    public ValueTask DisposeAsync()
    {
        _inbox.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

/// <summary>假长连接工厂：每次 <c>ConnectAsync</c> 建一条 <see cref="FakeControlNodeTransport" />。
/// 默认自动回 hello / ping / class.select / class.release / tree.get。</summary>
internal sealed class FakeControlNodeTransportFactory : IControlNodeTransportFactory
{
    private readonly List<FakeControlNodeTransport> _transports = [];

    /// <summary>握手回包载荷；<c>null</c> 用默认的成功载荷。</summary>
    public JsonObject? HelloReply { get; set; }

    /// <summary>握手直接回 <c>ok:false</c> 时用的错误码（<c>bad_secret</c> / <c>device_revoked</c> …）。</summary>
    public string? HelloRejectCode { get; set; }

    /// <summary>建连一律失败（模拟连不上）。</summary>
    public bool FailConnect { get; set; }

    public int ConnectCount
    {
        get
        {
            lock (_transports)
                return _transports.Count;
        }
    }

    /// <summary>最近一条连接。</summary>
    public FakeControlNodeTransport Last
    {
        get
        {
            lock (_transports)
                return _transports[^1];
        }
    }

    public Task<IControlNodeTransport> ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        if (FailConnect)
            throw new HttpRequestException("test: connect refused");

        var transport = new FakeControlNodeTransport();
        transport.OnSend = frame => OnSendAsync(transport, frame);
        lock (_transports)
            _transports.Add(transport);

        return Task.FromResult<IControlNodeTransport>(transport);
    }

    private Task OnSendAsync(FakeControlNodeTransport transport, ControlFrame frame)
    {
        switch (frame.Topic)
        {
            case ControlTopics.Hello when HelloRejectCode is not null:
                return transport.PushAsync(ControlFrame.Response(frame.Id, false, HelloRejectCode))
                    .AsTask();

            case ControlTopics.Hello:
                return transport
                    .PushAsync(ControlFrame.Response(frame.Id, true, payload: HelloReply ?? DefaultHelloReply()))
                    .AsTask();

            case ControlTopics.Ping:
            case ControlTopics.ClassSelect:
            case ControlTopics.ClassRelease:
            case ControlTopics.TreeGet:
                return transport.PushAsync(ControlFrame.Response(frame.Id, true, payload: new JsonObject()))
                    .AsTask();

            default:
                return Task.CompletedTask;
        }
    }

    /// <summary>与真实服务端同形状的握手回包。</summary>
    public static JsonObject DefaultHelloReply() => new()
    {
        ["protocol"] = ControlV4Protocol.Version,
        ["device"] = new JsonObject
        {
            ["id"] = "dev-1",
            ["name"] = "podium",
            ["platform"] = ControlNodeConnectOptions.DefaultPlatform,
            ["site_id"] = "SC-AAAAAA"
        },
        ["capabilities"] = new JsonArray(ControlV4Protocol.CapabilityStatusRead, "refreshNotice"),
        ["tree_version"] = 1,
        ["limits"] = new JsonObject { ["cmdTimeoutMs"] = 5000 },
        ["heartbeat_seconds"] = 25,
        ["offline_after_seconds"] = 75,
        ["desired_state_revision"] = 0,
        ["server_time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    };
}

/// <summary>内存里的凭证.</summary>
internal sealed class FakeCredentialProvider : IControlNodeCredentialProvider
{
    public FakeCredentialProvider(ControlNodeCredential? credential = null) => Current = credential;

    public ControlNodeCredential? Current { get; set; }

    /// <summary>被清空过几次（吊销必须清凭证）。</summary>
    public int ClearCount { get; private set; }

    public Task<ControlNodeCredential?> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Current);

    public Task SaveAsync(ControlNodeCredential credential, CancellationToken cancellationToken = default)
    {
        Current = credential;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Current = null;
        ClearCount++;
        return Task.CompletedTask;
    }
}

/// <summary>内存里的 node-state。</summary>
internal sealed class FakeStateStore : IControlNodeStateStore
{
    public ControlNodeState State { get; set; } = ControlNodeState.Empty;

    public int SaveCount { get; private set; }

    public ControlNodeState Load() => State;

    public void Save(ControlNodeState state)
    {
        State = state;
        SaveCount++;
    }
}

/// <summary>异步轮询等待（测试里等状态机走到目标状态）。</summary>
internal static class TestWait
{
    public static async Task UntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return;

            await Task.Delay(10).ConfigureAwait(false);
        }

        Assert.Fail($"等待条件超时（{timeoutMs}ms）");
    }
}
