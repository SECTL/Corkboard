namespace Corkboard.Core.Services.Board;

/// <summary>「时空回放」入口一次点击的结果。</summary>
public enum BoardReplayClickResult
{
    /// <summary>没有可回放的作业，这次点击不产生任何效果。</summary>
    Ignored,

    /// <summary>第一次点击：进入预备状态，等第二次点击确认。</summary>
    Armed,

    /// <summary>第二次点击：确认进入回放。</summary>
    EnterReplay
}

/// <summary>
///     回放入口的纯决策，和具体控件、计时器无关：
///     <list type="bullet">
///         <item>入口只在**有作业**时才出现——空板上没有「过去」可看，留着按钮只会点进一个空页面；</item>
///         <item>进入要**连点两次**：第一次只是预备，第二次才真的进。回放会把整块板子切成历史快照，
///               误触代价不小，所以做成两下确认。</item>
///     </list>
///     超时退回由调用方计时（见 <c>BoardReplayService</c>）：预备状态过期后调一次 <c>Disarm</c> 即可。
/// </summary>
public static class BoardReplayGate
{
    /// <summary>入口是否该出现：有作业、且当前没在回放。</summary>
    public static bool CanEnter(bool hasStops, bool isActive) => hasStops && !isActive;

    /// <summary>把一次点击映射成动作。<paramref name="isArmed" /> 表示此前是否已经点过第一下。</summary>
    public static BoardReplayClickResult Click(bool hasStops, bool isArmed)
    {
        if (!hasStops)
            return BoardReplayClickResult.Ignored;

        return isArmed ? BoardReplayClickResult.EnterReplay : BoardReplayClickResult.Armed;
    }
}
