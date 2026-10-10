using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Abstraction.Services;
using Corkboard.Core.Enums;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models.Board;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Models.SubConfigs.General;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Services.Board;
using Corkboard.Services.Ui;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.MainPages;

/// <summary>
///     作业板主页面：按配置排序、按配置排布显示作业列表。
///     <para>
///         页面上没有工具条：排布在「设置 → 作业板」里用下拉框选，布置作业的入口在
///         主界面标题栏上，表单是 <see cref="BoardAssignmentFormViewModel" />，由壳的页面级弹层显示
///         （见 <c>PageOverlayService</c>）：遮罩要盖住标题栏，就必须挂在壳那一层。
///     </para>
///     <para>
///         时空回放（<see cref="BoardReplayService" />）在壳那一层控制，这里订阅它：
///         回放期间显示的是「到某天为止已经存在的作业」，并且不允许编辑。
///     </para>
/// </summary>
public partial class BoardPageViewModel : ViewModelBase
{
    private readonly IBoardService _boardService;
    private readonly BoardReplayService _replay;

    /// <summary>手动排序收口期间挂起重建，见 <see cref="CommitSubjectOrder" />。</summary>
    private bool _isCommittingSubjectOrder;

    public BoardPageViewModel(MainConfigHandler configHandler, IBoardService boardService,
        BoardReplayService replay)
        : base(configHandler)
    {
        _boardService = boardService;
        _replay = replay;
        _boardService.Changed += OnBoardChanged;
        Config.BoardSettings.PropertyChanged += OnBoardSettingsChanged;
        Config.Basic.PropertyChanged += OnBasicSettingsChanged;
        _replay.PropertyChanged += OnReplayChanged;

        RebuildNotes();
    }

    /// <summary>已按当前排序方式合并的科目区块。</summary>
    public ObservableCollection<BoardSubjectItem> SubjectGroups { get; } = [];

    public bool HasNotes => SubjectGroups.Count > 0;

    /// <summary>
    ///     当前排布方式，页面据此切换容器面板与样式。配置里那份老「通铺」值会折算成区块，
    ///     所以页面只认两种排布（见 <see cref="BoardLayoutModes.Normalize" />）。
    /// </summary>
    public BoardLayoutMode LayoutMode => BoardLayoutModes.Normalize(Config.BoardSettings.LayoutMode);

    /// <summary>单列：一张便签一行，页面会限宽居中。</summary>
    public bool IsSingleColumnLayout => LayoutMode == BoardLayoutMode.SingleColumn;

    /// <summary>区块：等宽区块按可用宽度自适应分栏。</summary>
    public bool IsBlockLayout => LayoutMode == BoardLayoutMode.Block;

    /// <summary>
    ///     区块排布里单个区块的最小宽度（DIP）：一行能排几块由它决定。
    ///     用户在「设置 → 作业板 → 区块宽度」里填，这里过一遍夹取再交给面板。
    /// </summary>
    public double BlockMinWidth =>
        BoardBlockStyle.ClampMinItemWidth(Config.BoardSettings.BlockMinWidth);

    /// <summary>区块宽度上限：由最小宽度推出来（默认 300 → 420），没有单独的设置项。</summary>
    public double BlockMaxWidth => BoardBlockStyle.ResolveMaxItemWidth(BlockMinWidth);

    /// <summary>删除前是否要确认，页面在删之前问一次。</summary>
    public bool ConfirmBeforeDelete => Config.BoardSettings.ConfirmBeforeDelete;

    /// <summary>内容的默认字号（设置 → 作业板 → 内容默认样式）。作业自己没定整篇字号时用它。</summary>
    public double ContentDefaultFontSize =>
        BoardContentStyle.ClampFontSize(Config.BoardSettings.DefaultContentFontSize);

    /// <summary>内容的默认颜色；为空表示跟随主题前景色。</summary>
    public Color? ContentDefaultColor => Config.BoardSettings.DefaultContentColor;

    /// <summary>是否在回放中。为真时页面顶部给出提示、并且不显示删除按钮。</summary>
    public bool IsReplaying => _replay.IsActive;

    /// <summary>回放提示文案，例如「回放中：停在 2026-10-06，板上有 5 项作业」。</summary>
    public string ReplayBanner => _replay.BannerText;

    /// <summary>
    ///     空板提示：平时指向标题栏右上角的「布置作业」；点击穿透开着时那个按钮整块隐藏，
    ///     得改口让用户先解锁，否则提示指向一个看不见的按钮。
    /// </summary>
    public string EmptyHint => Config.Basic.ClickThrough ? CR.Board_EmptyHintLocked : CR.Board_EmptyHint;

    [RelayCommand]
    private void DeleteNote(BoardNoteItem? item)
    {
        // 回放看的是历史快照，此时不接受增删。
        if (item is null || _replay.IsActive)
            return;

        _boardService.Remove(item.Note.Id);
    }

    /// <summary>
    ///     拖动过程中把区块挪到新位置（页面在指针移动时逐帧调它）。
    ///     <para>只动内存里的集合给即时反馈，落盘留到松手时的 <see cref="CommitSubjectOrder" />。</para>
    /// </summary>
    public void MoveSubjectGroup(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex
            || fromIndex < 0 || fromIndex >= SubjectGroups.Count
            || toIndex < 0 || toIndex >= SubjectGroups.Count)
        {
            return;
        }

        SubjectGroups.Move(fromIndex, toIndex);
    }

    /// <summary>
    ///     松手后收口：按界面上的先后记下手动顺序并切成「手动」排序方式。
    ///     <para>
    ///         两个动作都会触发重建，所以先挂起、最后统一重建一次，
    ///         免得中间那一下按旧顺序把拖好的区块弹回去。
    ///     </para>
    /// </summary>
    public void CommitSubjectOrder()
    {
        if (_replay.IsActive)
            return;

        _isCommittingSubjectOrder = true;
        try
        {
            _boardService.SetSubjectOrder(SubjectGroups.Select(group => group.Key));
            Config.BoardSettings.SortMode = BoardSortMode.Manual;
        }
        finally
        {
            _isCommittingSubjectOrder = false;
        }

        RebuildNotes();
    }

    /// <summary>
    ///     页面离开可视树时断开订阅。
    ///     <para>
    ///         本 ViewModel 是 transient 的，而 <see cref="IBoardService" /> 与配置是单例：
    ///         不断开的话每开一次页面就往单例上多挂一个处理器，配置改动会被重复处理、页面也回收不掉。
    ///     </para>
    /// </summary>
    public void Detach()
    {
        _boardService.Changed -= OnBoardChanged;
        Config.BoardSettings.PropertyChanged -= OnBoardSettingsChanged;
        Config.Basic.PropertyChanged -= OnBasicSettingsChanged;
        _replay.PropertyChanged -= OnReplayChanged;
    }

    private void OnBoardChanged(object? sender, EventArgs e)
    {
        // 类型可能被设置页改过，但表单只在打开时构建，所以这里只刷新列表。
        RebuildNotes();
    }

    private void OnReplayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null
            or nameof(BoardReplayService.IsActive)
            or nameof(BoardReplayService.SelectedStop))
        {
            OnPropertyChanged(nameof(IsReplaying));
            OnPropertyChanged(nameof(ReplayBanner));
            RebuildNotes();
        }
    }

    private void OnBasicSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 只有点击穿透会影响这条提示（它决定标题栏右上角那一整块显示不显示）。
        if (e.PropertyName is null or nameof(BasicSettingsConfig.ClickThrough))
            OnPropertyChanged(nameof(EmptyHint));
    }

    private void OnBoardSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 排序方式、默认字号、默认颜色都会影响列表内容：前两个改排序，后两个改渲染出来的样子。
        if (e.PropertyName is null
            or nameof(BoardSettingsConfig.SortMode)
            or nameof(BoardSettingsConfig.DefaultContentFontSize)
            or nameof(BoardSettingsConfig.DefaultContentColor))
        {
            RebuildNotes();
        }

        if (e.PropertyName is null or nameof(BoardSettingsConfig.LayoutMode))
        {
            OnPropertyChanged(nameof(LayoutMode));
            OnPropertyChanged(nameof(IsSingleColumnLayout));
            OnPropertyChanged(nameof(IsBlockLayout));
        }

        if (e.PropertyName is null or nameof(BoardSettingsConfig.ConfirmBeforeDelete))
            OnPropertyChanged(nameof(ConfirmBeforeDelete));

        if (e.PropertyName is null or nameof(BoardSettingsConfig.BlockMinWidth))
        {
            OnPropertyChanged(nameof(BlockMinWidth));
            OnPropertyChanged(nameof(BlockMaxWidth));
        }

        if (e.PropertyName is null or nameof(BoardSettingsConfig.DefaultContentFontSize))
            OnPropertyChanged(nameof(ContentDefaultFontSize));

        if (e.PropertyName is null or nameof(BoardSettingsConfig.DefaultContentColor))
            OnPropertyChanged(nameof(ContentDefaultColor));
    }

    private void RebuildNotes()
    {
        // 拖动收口时两条路径（服务变更、排序方式变更）都会走到这里，统一挂起、由收口方重建一次。
        if (_isCommittingSubjectOrder)
            return;

        // 回放期间只显示「到那一天为止已经存在」的作业。
        var filtered = _replay.Filter(_boardService.Notes);

        // 平时把「已清理」的作业滤掉：清理只是让它从板子上消失，数据还在原处（见 BoardNote.CleanedAt）。
        // 回放看的是历史，那时候它还在板子上，所以照常显示。
        List<BoardNote> visible = _replay.IsActive
            ? [.. filtered]
            : [.. filtered.Where(note => note.IsOnBoard)];

        SubjectGroups.Clear();
        foreach (var group in BoardNoteGrouping.Group(
                     visible, Config.BoardSettings.SortMode, _boardService.SubjectOrder))
        {
            SubjectGroups.Add(new BoardSubjectItem(
                group.Key,
                group.Key.Length > 0 ? group.Key : CR.Board_Uncategorized,
                group.Select(BuildItem).ToList(),
                !_replay.IsActive));
        }

        OnPropertyChanged(nameof(HasNotes));
    }

    private BoardNoteItem BuildItem(BoardNote note)
    {
        var type = _boardService.FindType(note.TypeId);
        var details = new List<string>();
        if (type is not null) details.Add(type.DisplayName);

        if (type is not null)
        {
            foreach (var field in type.Fields)
            {
                if (!note.Values.TryGetValue(field.Id.ToString(), out var raw) || string.IsNullOrWhiteSpace(raw))
                    continue;

                var value = FormatFieldValue(field.Kind, raw.Trim());
                // 页数区间的值自带 P 前缀，再加字段名就啰嗦了。
                details.Add(field.Kind == BoardFieldKind.PageRange ? value : $"{field.DisplayLabel} {value}");
            }
        }

        // 截止日期按「今天」算文案。跨零点后不主动刷新，但任何一次数据/设置变动都会重建列表，
        // 真放着不管一整夜也就是「今天截止」晚一天变成「已过期 1 天」。
        var today = DateOnly.FromDateTime(DateTime.Now);
        var dueText = note.DueDate is { } due ? BoardDueDateFormatter.Describe(due, today) : string.Empty;
        var isOverdue = note.DueDate is { } overdueDue && BoardDueDateFormatter.IsOverdue(overdueDue, today);

        return new BoardNoteItem(
            note,
            string.Join(" · ", details),
            // 内容是真源（HTML 片段），默认字号/颜色在控件里逐段补成行内样式，这里只把设置值传下去。
            note.Content?.Trim() ?? string.Empty,
            ContentDefaultFontSize,
            ContentDefaultColor,
            !_replay.IsActive,
            dueText,
            isOverdue);
    }

    /// <summary>把原始值文本按字段类型格式化成给人看的样子。</summary>
    private static string FormatFieldValue(BoardFieldKind kind, string raw)
    {
        if (kind != BoardFieldKind.PageRange)
            return raw;

        var separator = raw.IndexOf('-');
        if (separator < 0)
            return raw;

        var from = raw[..separator];
        var to = raw[(separator + 1)..];

        // 只填了一边就只显示那一边，免得出现「P–15」这种半截东西。
        if (from.Length == 0 || to.Length == 0)
            return from.Length > 0 ? from : to;

        return string.Format(CultureInfo.CurrentCulture, CR.Board_PageRangeFormat, from, to);
    }
}
