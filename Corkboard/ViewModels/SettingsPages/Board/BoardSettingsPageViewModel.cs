using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Board;
using Corkboard.Core.Controls;
using Corkboard.Core.Enums;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Services.Config;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Board;

/// <summary>
///     「作业板 → 常规」设置页的 ViewModel：名称、排布、拖动排序、区块宽度、删除前确认与界面入口。
///     <para>
///         名称输入框除了失焦，还会在停手 400ms 后自动提交（用户不用按回车），区块宽度靠
///         <c>LostFocus</c>。内容的默认样式 / 字号 / 色板搬到「内容与样式」页，
///         手写、科目与分类、清理、历史、收纳与导出也都各有自己的子页。
///     </para>
/// </summary>
public partial class BoardSettingsPageViewModel : ViewModelBase
{
    public BoardSettingsPageViewModel(MainConfigHandler configHandler)
        : base(configHandler)
    {
        BoardNameDraft = Board.BoardName;
        _blockMinWidthDraft = (decimal)BoardBlockStyle.ClampMinItemWidth(Board.BlockMinWidth);
    }

    public BoardSettingsConfig Board => Config.BoardSettings;

    /// <summary>
    ///     排布（展示设置）下拉项。主页面顶部不放切换器，排布只在这里选；
    ///     排序方式已经搬到主界面标题栏的排序按钮上，所以这里没有排序项。
    ///     <para>
    ///         区块排在最前：它是默认值，<see cref="SelectedLayoutOption" /> 兜底也取第 0 项。
    ///     </para>
    /// </summary>
    public IReadOnlyList<BoardLayoutOption> LayoutOptions { get; } =
    [
        new(BoardLayoutMode.Block, CR.Board_Layout_Block),
        new(BoardLayoutMode.Columns, CR.Board_Layout_SingleColumn)
    ];

    /// <summary>
    ///     作业板名称的输入草稿。**不直接绑配置**：配置每变一次就落一次盘（见 ConfigHandlerBase），
    ///     双向绑到配置会让每敲一个字符写一遍 settings.json。
    /// </summary>
    [ObservableProperty] private string _boardNameDraft;

    /// <summary>
    ///     区块最小宽度的输入草稿（DIP）。**不直接双向绑配置**：NumericUpDown 每动一下值就落一次盘
    ///     （<c>ConfigHandlerBase</c> 没有防抖），所以先存草稿，失焦 / 离开页面时再提交。
    /// </summary>
    [ObservableProperty] private decimal? _blockMinWidthDraft;

    /// <summary>
    ///     名称输入框停手多久才提交。它同样「一敲就变」，
    ///     而提交就意味着落一次盘，所以也要攒一攒——用户因此不用按回车。
    /// </summary>
    private static readonly TimeSpan TextCommitDelay = TimeSpan.FromMilliseconds(400);

    private DispatcherTimer? _boardNameCommitTimer;

    /// <summary>
    ///     提交区块最小宽度。手输越界值时夹回合法区间，并把夹过的值同步回界面，
    ///     免得显示的和落盘的不是一个数。
    /// </summary>
    public void CommitBlockMinWidth()
    {
        var width = BlockMinWidthDraft is { } draft
            ? BoardBlockStyle.ClampMinItemWidth((double)draft)
            : BoardBlockStyle.ClampMinItemWidth(Board.BlockMinWidth);

        if (Math.Abs(Board.BlockMinWidth - width) > double.Epsilon)
            Board.BlockMinWidth = width;

        var normalized = (decimal)width;
        if (BlockMinWidthDraft != normalized)
            BlockMinWidthDraft = normalized;
    }

    /// <summary>
    ///     排布下拉选中项。配置里若是越界值或老版本的「通铺」，先按
    ///     <see cref="BoardLayoutModes.Normalize" /> 折算成界面上真的提供的两种，再匹配下拉项。
    /// </summary>
    public BoardLayoutOption SelectedLayoutOption
    {
        get => LayoutOptions.FirstOrDefault(option => option.Mode == BoardLayoutModes.Normalize(Board.LayoutMode))
               ?? LayoutOptions[0];
        set
        {
            if (value is not null)
                Board.LayoutMode = value.Mode;

            // 「区块宽度」那张卡只在区块排布下有意义，换排布要通知这一条。
            OnPropertyChanged(nameof(IsBlockLayout));
        }
    }

    /// <summary>
    ///     当前是不是「区块」排布。「区块宽度」那张卡只在区块排布下起作用，
    ///     成排 / 通铺排布里没有「单块基准宽度」这回事，所以整张卡隐藏。
    /// </summary>
    public bool IsBlockLayout => BoardLayoutModes.Normalize(Board.LayoutMode) == BoardLayoutMode.Block;

    /// <summary>设置页所有编辑的收口：草稿提交。输入框失焦与离开页面时都会调。</summary>
    public void SaveAll()
    {
        CommitBoardName();
        CommitBlockMinWidth();

        // 草稿上面都提交过了，压在防抖里的计时就别再触发一次（重复提交会白写一遍盘）。
        CancelTextCommits();
    }

    /// <summary>把名称草稿写回配置。配置自己会落盘。</summary>
    public void CommitBoardName()
    {
        var name = BoardNameDraft.Trim();
        if (Board.BoardName != name)
            Board.BoardName = name;
    }

    /// <summary>
    ///     名称草稿动一下就在 400ms 后提交：用户不用按回车，改完停手就生效。
    ///     提交会落盘，所以不能每敲一个字符写一次 <c>settings.json</c>。
    /// </summary>
    partial void OnBoardNameDraftChanged(string value)
    {
        // 构造期会把草稿初始化成配置里的名字，别为此空排一次提交。
        if (value.Trim() == Board.BoardName)
            return;

        _boardNameCommitTimer = ScheduleTextCommit(_boardNameCommitTimer, CommitBoardName);
    }

    /// <summary>
    ///     排一次「停手后提交」：先把上一次的计时顶掉，再起一个新的。
    ///     新计时器由调用点存回字段——lambda 里不能捕获 <c>ref</c> 参数，所以这里不用引用传递。
    /// </summary>
    private DispatcherTimer ScheduleTextCommit(DispatcherTimer? pending, Action commit)
    {
        pending?.Stop();

        var timer = new DispatcherTimer { Interval = TextCommitDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            commit();
        };
        timer.Start();

        return timer;
    }

    /// <summary>停掉还压在防抖里的名称提交（<see cref="SaveAll" /> 已经把草稿提交过了）。</summary>
    private void CancelTextCommits()
    {
        _boardNameCommitTimer?.Stop();
        _boardNameCommitTimer = null;
    }
}
