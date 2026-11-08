namespace Corkboard.Core.Actions;

/// <summary>
///     一块可以被行动操作的目标看板（作业板 / 考试看板 / 看板模式 / 值日板……）。
///     <para>
///         <see cref="Id" /> 是冻结的字符串标识（见 <see cref="BoardTargetIds" />），规则步骤里存的就是它；
///         显示名 / 是否可用由实现方（App 层）按当前状态给出，行动域只认 Id。
///     </para>
/// </summary>
public interface IBoardTarget
{
    /// <summary>冻结的目标标识（如 <c>exam-board</c>）。</summary>
    string Id { get; }

    /// <summary>用户看到的显示名。</summary>
    string DisplayName { get; }

    /// <summary>此刻能不能对它执行动作（窗口能力不支持 / 平台不允许时为假）。</summary>
    bool IsAvailable { get; }

    /// <summary>不可用的原因（可用时为空），给执行记录与设置页提示用。</summary>
    string? UnavailableReason { get; }

    /// <summary>此刻可不可能处于显示状态。</summary>
    bool IsVisible { get; }

    /// <summary>此刻是不是全屏状态。</summary>
    bool IsFullScreen { get; }

    /// <summary>显示这块看板（已显示时应当幂等）。</summary>
    void Show();

    /// <summary>隐藏这块看板（未显示时应当幂等）。</summary>
    void Hide();
}

/// <summary>
///     目标看板注册表：行动执行器按 <see cref="IBoardTarget.Id" /> 找目标。
///     <para>
///         App 层在 DI 里注册实现；<see cref="Changed" /> 在目标增删或「是否可用」变化时触发，
///         设置页的「可用目标」列表与执行器的缓存都靠它刷新。
///     </para>
/// </summary>
public interface IBoardTargetRegistry
{
    /// <summary>当前全部目标（顺序稳定：注册顺序）。</summary>
    IReadOnlyList<IBoardTarget> Targets { get; }

    /// <summary>按标识找目标；找不到返回 <see langword="null" />。</summary>
    IBoardTarget? Find(string id);

    /// <summary>目标集合或可用性变化时触发。</summary>
    event EventHandler? Changed;
}

/// <summary>
///     冻结的目标标识常量 —— 规则步骤、执行记录、日程项里存的就是这些字符串，改名等于改数据。
/// </summary>
public static class BoardTargetIds
{
    /// <summary>作业板（桌面上的主窗口 / 停靠形态）。</summary>
    public const string Homework = "homework-board";

    /// <summary>作业板的「home-work」别名（历史叫法，两个标识都指向作业板）。</summary>
    public const string HomeWork = "home-work";

    /// <summary>考试看板（全屏考试窗口及浮窗）。</summary>
    public const string ExamBoard = "exam-board";

    /// <summary>看板模式（全屏大时钟 + 一言 / 天气）。</summary>
    public const string DisplayBoard = "display-board";

    /// <summary>值日板（预留：目前没有实现，规则里选中时按「不可用」跳过）。</summary>
    public const string DutyBoard = "duty-board";

    /// <summary>全部合法标识（顺序固定）。</summary>
    public static readonly IReadOnlyList<string> All =
        [Homework, HomeWork, ExamBoard, DisplayBoard, DutyBoard];
}

