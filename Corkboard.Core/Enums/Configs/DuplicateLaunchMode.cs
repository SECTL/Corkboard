namespace Corkboard.Core.Enums.Configs;

/// <summary>
///     Corkboard 已经有一份在跑的时候，**再启动一次**该打开什么。
///     <para>
///         用户的原话是「如果有进程正在运行了，再一次打开的话，是打开哪一个窗口」——默认直接给出
///         考试的看板窗口（<see cref="ExamBoardPanel" />），双击图标就是「把看板摆到眼前」，
///         而不是先问一句再点一下。<see cref="Prompt" /> 是原来那个只有「重启 / 取消」的询问窗。
///     </para>
///     <para>
///         ⚠️ 按**数字**落盘（配置管道没有字符串枚举转换器），只能往后追加成员，不能改已有数值。
///         默认值就是 0 那一档，配置里没有这个键时也落到它。
///     </para>
/// </summary>
public enum DuplicateLaunchMode
{
    /// <summary>打开考试看板窗口（默认）：那份列着所有看板、每行一颗「放映」的窗口。</summary>
    ExamBoardPanel = 0,

    /// <summary>直接全屏放映当前看板。</summary>
    ExamBoardFullScreen = 1,

    /// <summary>打开看板模式（展示屏）。</summary>
    DisplayBoard = 2,

    /// <summary>总是问一句：「Corkboard 已经在运行」+「重启 / 取消」。</summary>
    Prompt = 3
}
