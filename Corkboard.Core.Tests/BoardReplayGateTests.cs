using Corkboard.Core.Services.Board;

namespace Corkboard.Core.Tests;

/// <summary>
///     回放入口的纯决策：没作业时既不出入口也进不去；有作业时要连点两次才真的进。
///     这两条是用户明确要的行为，单独钉住。
/// </summary>
public class BoardReplayGateTests
{
    [Theory]
    [InlineData(true, false, true)]   // 有作业、没在回放 → 出入口
    [InlineData(true, true, false)]   // 正在回放 → 不再出入口（此时画的是控制条）
    [InlineData(false, false, false)] // 一条作业都没有 → 不出入口
    [InlineData(false, true, false)]
    public void CanEnter_OnlyWhenThereAreStopsAndNotReplaying(bool hasStops, bool isActive, bool expected)
    {
        Assert.Equal(expected, BoardReplayGate.CanEnter(hasStops, isActive));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void Click_WithoutStops_IsIgnored(bool hasStops, bool isArmed)
    {
        // 空板上不该被带进回放：没作业时入口不显示，程序化调用也要被挡住。
        Assert.Equal(BoardReplayClickResult.Ignored, BoardReplayGate.Click(hasStops, isArmed));
    }

    [Fact]
    public void Click_FirstTime_OnlyArms()
    {
        // 第一下不能直接进回放——那样就成了单击进入，超时退回也就无从谈起。
        Assert.Equal(BoardReplayClickResult.Armed, BoardReplayGate.Click(hasStops: true, isArmed: false));
    }

    [Fact]
    public void Click_SecondTime_EntersReplay()
    {
        Assert.Equal(BoardReplayClickResult.EnterReplay, BoardReplayGate.Click(hasStops: true, isArmed: true));
    }

    [Fact]
    public void Click_Sequence_ArmsThenEnters()
    {
        // 完整走一遍两下确认：状态在两次点击之间传递。
        var armed = false;

        var first = BoardReplayGate.Click(hasStops: true, isArmed: armed);
        Assert.Equal(BoardReplayClickResult.Armed, first);
        armed = first == BoardReplayClickResult.Armed;

        var second = BoardReplayGate.Click(hasStops: true, isArmed: armed);
        Assert.Equal(BoardReplayClickResult.EnterReplay, second);
    }

    [Fact]
    public void Click_AfterTimeoutReverted_ArmsAgain()
    {
        // 超时退回后（isArmed 回到 false）再点，仍然是「第一下」，不该直接进。
        Assert.Equal(BoardReplayClickResult.Armed, BoardReplayGate.Click(hasStops: true, isArmed: false));
    }
}
