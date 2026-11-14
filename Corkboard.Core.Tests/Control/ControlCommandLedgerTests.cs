using System.Text.Json.Nodes;
using Corkboard.Core.ControlCenter;

namespace Corkboard.Core.Tests;

/// <summary>指令幂等表（LRU + TTL）：同一个 <c>command_id</c> 重投不重复执行。</summary>
public class ControlCommandLedgerTests
{
    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void TryGet_ReturnsRememberedOutcome()
    {
        var ledger = new ControlCommandLedger();

        Assert.True(ledger.TryRemember("cmd-1", ControlCommandOutcome.Success(new JsonObject { ["ok"] = true })));
        Assert.True(ledger.TryGet("cmd-1", out var cached));
        Assert.NotNull(cached);
        Assert.True(cached!.Ok);
        Assert.True(cached.Data["ok"]!.GetValue<bool>());
    }

    [Fact]
    public void TryRemember_SecondTimeForSameCommandIdReturnsFalse()
    {
        var ledger = new ControlCommandLedger();
        var first = ControlCommandOutcome.Success(new JsonObject());
        var second = ControlCommandOutcome.Failure(ControlErrorCodes.Failed);

        Assert.True(ledger.TryRemember("cmd-1", first));
        Assert.False(ledger.TryRemember("cmd-1", second));

        Assert.True(ledger.TryGet("cmd-1", out var cached));
        Assert.True(cached!.Ok);
    }

    [Fact]
    public void TryGet_MissesAfterTimeToLive()
    {
        var clock = new FakeClock();
        var ledger = new ControlCommandLedger(timeToLive: TimeSpan.FromMinutes(1), timeProvider: clock);
        ledger.TryRemember("cmd-1", ControlCommandOutcome.Success(new JsonObject()));

        clock.Now = clock.Now.AddSeconds(30);
        Assert.True(ledger.TryGet("cmd-1", out _));

        clock.Now = clock.Now.AddSeconds(31);
        Assert.False(ledger.TryGet("cmd-1", out var cached));
        Assert.Null(cached);
    }

    [Fact]
    public void TryGet_EvictsLeastRecentlyUsedEntry()
    {
        var ledger = new ControlCommandLedger(capacity: 2);
        ledger.TryRemember("cmd-a", ControlCommandOutcome.Success(new JsonObject()));
        ledger.TryRemember("cmd-b", ControlCommandOutcome.Success(new JsonObject()));

        // 摸一下 a，把它提到最近使用；再塞 c 应淘汰 b。
        Assert.True(ledger.TryGet("cmd-a", out _));
        ledger.TryRemember("cmd-c", ControlCommandOutcome.Success(new JsonObject()));

        Assert.True(ledger.TryGet("cmd-a", out _));
        Assert.False(ledger.TryGet("cmd-b", out _));
        Assert.True(ledger.TryGet("cmd-c", out _));
        Assert.Equal(2, ledger.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyCommandId_IsNeverRemembered(string? commandId)
    {
        var ledger = new ControlCommandLedger();

        Assert.False(ledger.TryRemember(commandId, ControlCommandOutcome.Failure(ControlErrorCodes.Failed)));
        Assert.False(ledger.TryGet(commandId, out _));
        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public void Clear_DropsEverything()
    {
        var ledger = new ControlCommandLedger();
        ledger.TryRemember("cmd-1", ControlCommandOutcome.Success(new JsonObject()));

        ledger.Clear();

        Assert.Equal(0, ledger.Count);
        Assert.False(ledger.TryGet("cmd-1", out _));
    }
}
