using System.Diagnostics;
using System.Text.Json.Nodes;
using Corkboard.Core.ControlCenter;

namespace Corkboard.Core.Tests;

/// <summary>
///     会话层（状态机 / 握手 / 指令回执 / 期望状态 / 踢出）的单测。
///     用假长连接把服务端换成进程内桩，不需要真的 WebSocket 服务端。
/// </summary>
public class ControlNodeSessionTests
{
    private static readonly ControlNodeCredential Credential = new("dev-1", "secret-1", "SC-AAAAAA");

    private static ControlNodeConnectOptions Options(params TimeSpan[] backoff) => new()
    {
        ServerUrl = "https://control.test",
        ConnectTimeout = TimeSpan.FromSeconds(2),
        HandshakeTimeout = TimeSpan.FromSeconds(2),
        RequestTimeout = TimeSpan.FromSeconds(2),
        ReconnectBackoff = backoff.Length == 0 ? ControlNodeConnectOptions.DefaultBackoff : backoff
    };

    private static ControlNodeSession CreateSession(
        FakeControlNodeTransportFactory factory,
        FakeCredentialProvider? credentials = null,
        FakeStateStore? stateStore = null,
        ControlNodeConnectOptions? options = null) =>
        new(options ?? Options(), credentials, stateStore, factory, deviceName: "podium");

    [Fact]
    public async Task StateMachine_UnpairedThenConnectingThenOnline()
    {
        var credentials = new FakeCredentialProvider();
        var factory = new FakeControlNodeTransportFactory();
        await using var session = CreateSession(factory, credentials);
        var transitions = new List<ControlNodeLinkStatus>();
        session.StateChanged += (_, state) =>
        {
            lock (transitions)
                transitions.Add(state.Status);
        };

        Assert.False(await session.StartAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ControlNodeLinkStatus.Unpaired, session.State.Status);
        Assert.True(session.State.NeedsEnrollment);
        Assert.Equal(0, factory.ConnectCount);

        // 用户补上凭证后，后台循环应当自己接进来，不需要再点一次「启动」。
        credentials.Current = Credential;
        await TestWait.UntilAsync(() => session.State.Status == ControlNodeLinkStatus.Online);

        Assert.True(session.IsConnected);
        Assert.Equal(1, factory.ConnectCount);
        lock (transitions)
        {
            Assert.Contains(ControlNodeLinkStatus.Connecting, transitions);
            Assert.Contains(ControlNodeLinkStatus.Online, transitions);
        }

        await session.StopAsync();
    }

    [Fact]
    public async Task Hello_CarriesAppliedRevisionCapabilitiesAndRestoresClass()
    {
        var factory = new FakeControlNodeTransportFactory();
        var stateStore = new FakeStateStore { State = new ControlNodeState(42, "class-9") };
        await using var session = CreateSession(factory, new FakeCredentialProvider(Credential), stateStore);

        Assert.True(await session.StartAsync(TestContext.Current.CancellationToken));

        var hello = Assert.Single(factory.Last.FramesOf(ControlTopics.Hello));
        var payload = hello.Payload!;
        Assert.Equal(ControlV4Protocol.Version, payload["protocol"]!.GetValue<int>());
        Assert.Equal("dev-1", payload["device_id"]!.GetValue<string>());
        Assert.Equal("secret-1", payload["secret"]!.GetValue<string>());
        Assert.Equal(42, payload["applied_state_revision"]!.GetValue<long>());
        Assert.Equal(ControlNodeConnectOptions.DefaultPlatform, payload["platform"]!.GetValue<string>());

        var capabilities = payload["capabilities"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        Assert.Contains(ControlV4Protocol.CapabilityStatusRead, capabilities);

        // 连上后自动重占上次的班。
        await TestWait.UntilAsync(() => factory.Last.FramesOf(ControlTopics.ClassSelect).Length == 1);
        var select = Assert.Single(factory.Last.FramesOf(ControlTopics.ClassSelect));
        Assert.Equal("class-9", select.Payload!["class_id"]!.GetValue<string>());

        await session.StopAsync();
    }

    [Fact]
    public async Task Command_AcksFirstThenReturnsResultWithCommandId()
    {
        var factory = new FakeControlNodeTransportFactory();
        await using var session = CreateSession(factory, new FakeCredentialProvider(Credential));
        var executed = 0;
        session.RegisterHandler("refreshNotice", (invocation, _) =>
        {
            Interlocked.Increment(ref executed);
            return Task.FromResult<object?>(new JsonObject
            {
                ["shown"] = true,
                ["title"] = invocation.Args["title"]?.GetValue<string>()
            });
        });

        Assert.True(await session.StartAsync(TestContext.Current.CancellationToken));
        await TestWait.UntilAsync(() => session.State.Status == ControlNodeLinkStatus.Online);
        var transport = factory.Last;

        await transport.PushEventAsync(ControlTopics.Command, new JsonObject
        {
            ["command_id"] = "cmd-1",
            ["cmd"] = "refreshNotice",
            ["args"] = new JsonObject { ["title"] = "hello" },
            ["deadline_ms"] = 5000
        });

        await TestWait.UntilAsync(() => transport.FramesOf(ControlTopics.CommandResult).Length == 1);

        // 两帧的 id 都必须回填 command_id（协议 §3.3）。
        var ack = Assert.Single(transport.FramesOf(ControlTopics.CommandAck));
        Assert.Equal("cmd-1", ack.Id);
        Assert.Equal(ControlFrameTypes.Request, ack.Type);
        Assert.True(ack.Payload!["accepted"]!.GetValue<bool>());

        var result = Assert.Single(transport.FramesOf(ControlTopics.CommandResult));
        Assert.Equal("cmd-1", result.Id);
        Assert.True(result.Payload!["ok"]!.GetValue<bool>());
        Assert.Equal("hello", result.Payload!["data"]!["title"]!.GetValue<string>());
        Assert.Equal(1, executed);

        await session.StopAsync();
    }

    [Fact]
    public async Task DuplicateCommandId_IsNotExecutedTwice()
    {
        var factory = new FakeControlNodeTransportFactory();
        await using var session = CreateSession(factory, new FakeCredentialProvider(Credential));
        var executed = 0;
        session.RegisterHandler("refreshNotice", (_, _) =>
        {
            Interlocked.Increment(ref executed);
            return Task.FromResult<object?>(new JsonObject { ["shown"] = true });
        });

        Assert.True(await session.StartAsync(TestContext.Current.CancellationToken));
        await TestWait.UntilAsync(() => session.State.Status == ControlNodeLinkStatus.Online);
        var transport = factory.Last;
        var payload = new JsonObject
        {
            ["command_id"] = "cmd-dup",
            ["cmd"] = "refreshNotice",
            ["deadline_ms"] = 5000
        };

        await transport.PushEventAsync(ControlTopics.Command, payload.DeepClone().AsObject());
        await TestWait.UntilAsync(() => transport.FramesOf(ControlTopics.CommandResult).Length == 1);

        // 服务端没收到 result 又投一次：必须重投缓存结果，不能重复执行。
        await transport.PushEventAsync(ControlTopics.Command, payload.DeepClone().AsObject());
        await TestWait.UntilAsync(() => transport.FramesOf(ControlTopics.CommandResult).Length == 2);

        Assert.Equal(1, executed);
        Assert.Equal(2, transport.FramesOf(ControlTopics.CommandAck).Length);
        Assert.Equal("cmd-dup", transport.FramesOf(ControlTopics.CommandResult)[1].Id);

        await session.StopAsync();
    }

    [Fact]
    public async Task Command_UnknownCapability_IsRejectedWithoutResult()
    {
        var factory = new FakeControlNodeTransportFactory();
        await using var session = CreateSession(factory, new FakeCredentialProvider(Credential));
        Assert.True(await session.StartAsync(TestContext.Current.CancellationToken));
        await TestWait.UntilAsync(() => session.State.Status == ControlNodeLinkStatus.Online);
        var transport = factory.Last;

        await transport.PushEventAsync(ControlTopics.Command, new JsonObject
        {
            ["command_id"] = "cmd-unknown",
            ["cmd"] = "pushImage",
            ["deadline_ms"] = 5000
        });

        await TestWait.UntilAsync(() => transport.FramesOf(ControlTopics.CommandAck).Length == 1);
        var ack = Assert.Single(transport.FramesOf(ControlTopics.CommandAck));
        Assert.False(ack.Payload!["accepted"]!.GetValue<bool>());
        Assert.Equal(ControlErrorCodes.CapabilityUnsupported, ack.Payload!["code"]!.GetValue<string>());
        Assert.Empty(transport.FramesOf(ControlTopics.CommandResult));

        await session.StopAsync();
    }

    [Fact]
    public async Task Command_WhenRemoteControlDisabled_IsRejected()
    {
        var factory = new FakeControlNodeTransportFactory();
        await using var session = CreateSession(factory, new FakeCredentialProvider(Credential));
        var executed = 0;
        session.RegisterHandler("refreshNotice", (_, _) =>
        {
            Interlocked.Increment(ref executed);
            return Task.FromResult<object?>(null);
        });
        Assert.True(await session.StartAsync(TestContext.Current.CancellationToken));
        await TestWait.UntilAsync(() => session.State.Status == ControlNodeLinkStatus.Online);
        session.IsRemoteControlAllowed = false;
        var transport = factory.Last;

        await transport.PushEventAsync(ControlTopics.Command, new JsonObject
        {
            ["command_id"] = "cmd-disabled",
            ["cmd"] = "refreshNotice"
        });

        await TestWait.UntilAsync(() => transport.FramesOf(ControlTopics.CommandAck).Length == 1);
        var ack = Assert.Single(transport.FramesOf(ControlTopics.CommandAck));
        Assert.False(ack.Payload!["accepted"]!.GetValue<bool>());
        Assert.Equal(ControlErrorCodes.LocalRemoteDisabled, ack.Payload!["code"]!.GetValue<string>());
        Assert.Equal(0, executed);

        await session.StopAsync();
    }

    [Fact]
    public async Task Command_Expired_IsRejectedWithoutExecuting()
    {
        var factory = new FakeControlNodeTransportFactory();
        await using var session = CreateSession(factory, new FakeCredentialProvider(Credential));
        var executed = 0;
        session.RegisterHandler("refreshNotice", (_, _) =>
        {
            Interlocked.Increment(ref executed);
            return Task.FromResult<object?>(null);
        });
        Assert.True(await session.StartAsync(TestContext.Current.CancellationToken));
        await TestWait.UntilAsync(() => session.State.Status == ControlNodeLinkStatus.Online);
        var transport = factory.Last;

        await transport.PushEventAsync(ControlTopics.Command, new JsonObject
        {
            ["command_id"] = "cmd-expired",
            ["cmd"] = "refreshNotice",
            ["expires_at"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds()
        });

        await TestWait.UntilAsync(() => transport.FramesOf(ControlTopics.CommandAck).Length == 1);
        var ack = Assert.Single(transport.FramesOf(ControlTopics.CommandAck));
        Assert.False(ack.Payload!["accepted"]!.GetValue<bool>());
        Assert.Equal(ControlErrorCodes.Expired, ack.Payload!["code"]!.GetValue<string>());
        Assert.Equal(0, executed);
        Assert.Empty(transport.FramesOf(ControlTopics.CommandResult));

        await session.StopAsync();
    }

    [Fact]
    public async Task Command_SlowHandler_ClientRepliesTimeoutBeforeDeadline()
    {
        var factory = new FakeControlNodeTransportFactory();
        await using var session = CreateSession(factory, new FakeCredentialProvider(Credential));
        session.RegisterHandler("reloadConfig", async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return null;
        });
        Assert.True(await session.StartAsync(TestContext.Current.CancellationToken));
        await TestWait.UntilAsync(() => session.State.Status == ControlNodeLinkStatus.Online);
        var transport = factory.Last;
        var stopwatch = Stopwatch.StartNew();

        await transport.PushEventAsync(ControlTopics.Command, new JsonObject
        {
            ["command_id"] = "cmd-slow",
            ["cmd"] = "reloadConfig",
            ["deadline_ms"] = 600
        });

        await TestWait.UntilAsync(() => transport.FramesOf(ControlTopics.CommandResult).Length == 1, timeoutMs: 3000);
        stopwatch.Stop();

        var result = Assert.Single(transport.FramesOf(ControlTopics.CommandResult));
        Assert.Equal("cmd-slow", result.Id);
        Assert.False(result.Payload!["ok"]!.GetValue<bool>());
        Assert.Equal(ControlErrorCodes.Timeout, result.Payload!["code"]!.GetValue<string>());
        // deadline − 200ms 就代回，别等到 deadline 之后（服务端此时已经不收了）。
        Assert.InRange(stopwatch.ElapsedMilliseconds, 0, 3000);

        await session.StopAsync();
    }

    [Fact]
    public async Task DesiredState_AppliesOnlyNewerRevisions()
    {
        var factory = new FakeControlNodeTransportFactory();
        var stateStore = new FakeStateStore();
        await using var session = CreateSession(factory, new FakeCredentialProvider(Credential), stateStore);
        Assert.True(await session.StartAsync(TestContext.Current.CancellationToken));
        await TestWait.UntilAsync(() => session.State.Status == ControlNodeLinkStatus.Online);
        var transport = factory.Last;

        await transport.PushEventAsync(ControlTopics.DesiredState, new JsonObject
        {
            ["revision"] = 100,
            ["class_id"] = "class-a",
            ["draw_locked"] = false
        });
        await TestWait.UntilAsync(() => session.AppliedRevision == 100);
        Assert.Equal("class-a", session.DesiredClassId);
        Assert.False(session.DrawLocked);
        Assert.True(stateStore.SaveCount > 0);

        // 旧 revision 必须丢弃（重连后服务端可能把旧的补投一遍）。
        await transport.PushEventAsync(ControlTopics.DesiredState, new JsonObject
        {
            ["revision"] = 50,
            ["class_id"] = "class-old"
        });
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(100, session.AppliedRevision);
        Assert.Equal("class-a", session.DesiredClassId);

        await transport.PushEventAsync(ControlTopics.DesiredState, new JsonObject
        {
            ["revision"] = 200,
            ["class_id"] = "class-b",
            ["draw_locked"] = true
        });
        await TestWait.UntilAsync(() => session.AppliedRevision == 200);
        Assert.Equal("class-b", session.DesiredClassId);
        Assert.True(session.DrawLocked);

        await session.StopAsync();
    }

    [Fact]
    public async Task Evicted_SupersededKeepsCredential_RevokedClearsIt()
    {
        var factory = new FakeControlNodeTransportFactory();
        var credentials = new FakeCredentialProvider(Credential);
        await using var session = CreateSession(factory, credentials);
        Assert.True(await session.StartAsync(TestContext.Current.CancellationToken));
        await TestWait.UntilAsync(() => session.State.Status == ControlNodeLinkStatus.Online);
        var transport = factory.Last;

        await transport.PushEventAsync(ControlTopics.Evicted, new JsonObject
        {
            ["reason"] = "superseded",
            ["class_id"] = "class-a"
        });
        await TestWait.UntilAsync(() => session.State.Status == ControlNodeLinkStatus.Superseded);
        Assert.Equal(ControlDetailKeys.Superseded, session.State.DetailKey);
        Assert.Null(session.SelectedClassId);
        Assert.NotNull(credentials.Current);
        Assert.Equal(0, credentials.ClearCount);

        // 被移除与「被顶掉」不同：凭证必须清掉，否则会拿作废的 secret 无限重连。
        await transport.PushEventAsync(ControlTopics.Evicted, new JsonObject { ["reason"] = "revoked" });
        await TestWait.UntilAsync(() => session.State.Status == ControlNodeLinkStatus.Revoked);
        Assert.Equal(ControlDetailKeys.Revoked, session.State.DetailKey);
        Assert.Null(credentials.Current);
        Assert.Equal(1, credentials.ClearCount);

        await session.StopAsync();
    }

    [Fact]
    public async Task TerminalHandshake_Revoked_ClearsCredentialAndStopsRetrying()
    {
        var factory = new FakeControlNodeTransportFactory { HelloRejectCode = ControlErrorCodes.DeviceRevoked };
        var credentials = new FakeCredentialProvider(Credential);
        await using var session = CreateSession(factory, credentials);

        Assert.False(await session.StartAsync(TestContext.Current.CancellationToken));
        await TestWait.UntilAsync(() => session.State.Status == ControlNodeLinkStatus.Revoked);

        Assert.Equal(1, credentials.ClearCount);
        Assert.Null(credentials.Current);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        // 终态不再重连（退避里没有意义，只会一直撞同一面墙）。
        Assert.Equal(1, factory.ConnectCount);

        await session.StopAsync();
    }

    [Fact]
    public async Task Reconnect_AfterDrop_ComesBackOnlineWithHelloAgain()
    {
        var factory = new FakeControlNodeTransportFactory();
        await using var session = CreateSession(
            factory,
            new FakeCredentialProvider(Credential),
            options: Options(TimeSpan.FromMilliseconds(50)));
        Assert.True(await session.StartAsync(TestContext.Current.CancellationToken));
        await TestWait.UntilAsync(() => session.State.Status == ControlNodeLinkStatus.Online);

        factory.Last.Close(1006, "test drop");

        await TestWait.UntilAsync(() => factory.ConnectCount >= 2 && session.State.Status == ControlNodeLinkStatus.Online);
        Assert.Single(factory.Last.FramesOf(ControlTopics.Hello));

        await session.StopAsync();
    }
}
