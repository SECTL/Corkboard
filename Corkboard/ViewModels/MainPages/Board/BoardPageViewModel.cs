using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Board;
using Corkboard.Core.Enums;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Notes;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Models.SubConfigs.General;
using Corkboard.Core.Homework;
using Corkboard.Core.RichText;
using Corkboard.Core.Services.Config;
using Corkboard.Services.Board;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.MainPages.Board;

/// <summary>
///     作业板主页面：按配置排序、按配置排布显示作业列表。
///     <para>
///         页面上没有工具条：排布在「设置 → 作业板」里用下拉框选，布置作业的入口在
///         主界面标题栏上，表单是 <see cref="HomeworkAssignmentFormViewModel" />，由壳的页面级弹层显示
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

    /// <summary>导出结果的轻提示挂多久（秒）。成功与失败都只闪一下，不占地方。</summary>
    private static readonly TimeSpan ExportHintDuration = TimeSpan.FromSeconds(3);

    /// <summary>导出提示的自动消失计时；页面离开可视树时要停掉（见 <see cref="Detach" />）。</summary>
    private DispatcherTimer? _exportHintTimer;

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
    public ObservableCollection<BoardSectionItem> SubjectGroups { get; } = [];

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
    ///     区块排布里单个区块的基准宽度（DIP）：一行能排几块由它决定。
    ///     用户在「设置 → 作业板 → 区块宽度」里填，这里过一遍夹取再交给面板。
    /// </summary>
    public double BlockMinWidth =>
        BoardBlockStyle.ClampMinItemWidth(Config.BoardSettings.BlockMinWidth);

    /// <summary>
    ///     区块宽度上限：自适应时由基准宽度推出来（默认 300 → 420），关掉自适应则等于基准宽度
    ///     ——面板于是按固定宽度排，一行放不下才换行。开关见「设置 → 作业板 → 区块宽度自适应」。
    /// </summary>
    public double BlockMaxWidth => BoardBlockStyle.ResolveMaxItemWidth(
        BlockMinWidth,
        Config.BoardSettings.BlockWidthAdaptive);

    /// <summary>删除前是否要确认，页面在删之前问一次。</summary>
    public bool ConfirmBeforeDelete => Config.BoardSettings.ConfirmBeforeDelete;

    /// <summary>内容的默认字号（设置 → 作业板 → 内容默认样式）。作业自己没定整篇字号时用它。</summary>
    public double ContentDefaultFontSize =>
        RichTextStyle.ClampFontSize(Config.BoardSettings.DefaultContentFontSize);

    /// <summary>内容的默认颜色；为空表示跟随主题前景色。</summary>
    public Color? ContentDefaultColor => Config.BoardSettings.DefaultContentColor;

    /// <summary>科目标题的字号（设置 → 作业板 → 字号）。记在科目区块项上，设置一改就重建列表。</summary>
    public double SubjectFontSize =>
        BoardTextStyle.ClampSubjectFontSize(Config.BoardSettings.SubjectFontSize);

    /// <summary>二级分类标题的字号（设置 → 科目与分类 → 分类标题字号）。同样记在列表项上。</summary>
    public double SectionFontSize =>
        BoardTextStyle.ClampSectionFontSize(Config.BoardSettings.SectionFontSize);

    /// <summary>作业条上小字（类型与字段值、截止日期）的字号。同样记在列表项上。</summary>
    public double NoteMetaFontSize =>
        BoardTextStyle.ClampMetaFontSize(Config.BoardSettings.NoteMetaFontSize);

    /// <summary>是否在回放中。为真时页面顶部给出提示、并且不显示删除按钮。</summary>
    public bool IsReplaying => _replay.IsActive;

    /// <summary>
    ///     作业条右侧那两颗按钮（编辑 / 删除）画不画。两种情况都不画：
    ///     回放看的是历史快照、不接受增删；点击穿透开着时整窗收不到鼠标，画出来只是两颗点不动的图标。
    ///     <para>值记在每个列表项上（<see cref="BoardNoteCardItem.CanModify" />），所以变化时要重建列表。</para>
    /// </summary>
    public bool CanModifyNotes => !_replay.IsActive && !Config.Basic.ClickThrough;

    /// <summary>
    ///     导出图片按钮画不画。空板上没什么可导的；点击穿透开着时整窗点不动，画出来只是一颗点不动的图标。
    ///     回放里允许导出（导的是当前看到的这份历史快照）。
    /// </summary>
    public bool CanExportImage => HasNotes && !Config.Basic.ClickThrough;

    /// <summary>导出结果的轻提示（成功 / 失败），几秒后自己消失。</summary>
    [ObservableProperty] private string _exportHint = string.Empty;

    /// <summary>轻提示有没有内容，模板据此显隐那一行。单独给一个属性是为了不用值转换器。</summary>
    [ObservableProperty] private bool _hasExportHint;

    /// <summary>显示一条导出提示并排好自动消失。</summary>
    public void ShowExportHint(string text)
    {
        ExportHint = text;
        HasExportHint = text.Length > 0;

        _exportHintTimer ??= new DispatcherTimer { Interval = ExportHintDuration };
        _exportHintTimer.Stop();
        _exportHintTimer.Tick -= OnExportHintTick;
        _exportHintTimer.Tick += OnExportHintTick;
        _exportHintTimer.Start();
    }

    private void OnExportHintTick(object? sender, EventArgs e)
    {
        _exportHintTimer?.Stop();
        ExportHint = string.Empty;
        HasExportHint = false;
    }

    /// <summary>回放提示文案，例如「回放中：停在 2026-10-06，板上有 5 项作业」。</summary>
    public string ReplayBanner => _replay.BannerText;

    /// <summary>
    ///     空板提示：平时指向标题栏右上角的「布置作业」；点击穿透开着时那个按钮整块隐藏，
    ///     得改口让用户先解锁，否则提示指向一个看不见的按钮。
    /// </summary>
    public string EmptyHint => Config.Basic.ClickThrough ? CR.Board_EmptyHintLocked : CR.Board_EmptyHint;

    [RelayCommand]
    private void DeleteNote(BoardNoteCardItem? item)
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

        _exportHintTimer?.Stop();
        _exportHintTimer = null;
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
        // 点击穿透影响三处：空板提示的指向（标题栏那一整块会隐藏）、作业条右侧的编辑/删除按钮，
        // 以及右上角的导出按钮。
        if (e.PropertyName is null or nameof(BasicSettingsConfig.ClickThrough))
        {
            OnPropertyChanged(nameof(EmptyHint));
            OnPropertyChanged(nameof(CanExportImage));
            RebuildNotes();
        }
    }

    private void OnBoardSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 排序方式、默认字号、默认颜色、科目标题与作业条小字的字号都会影响列表内容：
        // 前者改排序，其余几个改渲染出来的样子（字号记在列表项上，所以只能重建）。
        if (e.PropertyName is null
            or nameof(BoardSettingsConfig.SortMode)
            or nameof(BoardSettingsConfig.DefaultContentFontSize)
            or nameof(BoardSettingsConfig.DefaultContentColor)
            or nameof(BoardSettingsConfig.SubjectFontSize)
            or nameof(BoardSettingsConfig.SectionFontSize)
            or nameof(BoardSettingsConfig.NoteMetaFontSize))
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

        if (e.PropertyName is null
            or nameof(BoardSettingsConfig.BlockMinWidth)
            or nameof(BoardSettingsConfig.BlockWidthAdaptive))
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

        // 平时把「已清理」的作业滤掉：清理只是让它从板子上消失，数据还在原处（见 Note.CleanedAt）。
        // 回放看的是历史，那时候它还在板子上，所以照常显示。
        List<Note> visible = _replay.IsActive
            ? [.. filtered]
            : [.. filtered.Where(note => note.IsOnBoard)];

        SubjectGroups.Clear();
        foreach (var group in NoteGrouping.GroupWithSections(
                     visible, Config.BoardSettings.SortMode, _boardService.SubjectOrder, _boardService.Subjects,
                     subject => _boardService.GetSections(subject).Select(section => section.Name).ToList()))
        {
            SubjectGroups.Add(new BoardSectionItem(
                group.Subject,
                group.Subject.Length > 0 ? group.Subject : CR.Board_Uncategorized,
                group.Sections
                    .Select(section => new BoardSubsectionItem(
                        section.IsUncategorized ? CR.Board_Uncategorized : section.Name,
                        !section.IsUncategorized,
                        section.Notes.Select(BuildItem).ToList(),
                        SectionFontSize))
                    .ToList(),
                !_replay.IsActive,
                SubjectFontSize));
        }

        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(CanExportImage));
    }

    private BoardNoteCardItem BuildItem(Note note)
    {
        // 截止日期按「今天」算文案。跨零点后不主动刷新，但任何一次数据/设置变动都会重建列表，
        // 真放着不管一整夜也就是「今天截止」晚一天变成「已过期 1 天」。
        var today = DateOnly.FromDateTime(DateTime.Now);
        var dueText = note.DueDate is { } due ? HomeworkDueDateFormatter.Describe(due, today) : string.Empty;
        var isOverdue = note.DueDate is { } overdueDue && HomeworkDueDateFormatter.IsOverdue(overdueDue, today);

        // 手写作业：内容是笔迹 JSON，卡片画只读的笔迹预览；富文本那条路只对普通作业生效。
        var isHandwriting = note.ContentKind == RichTextContentKind.Handwriting;
        var handwriting = isHandwriting ? note.Handwriting : null;

        return new BoardNoteCardItem(
            note,
            // 内容是真源（HTML 片段），默认字号/颜色在控件里逐段补成行内样式，这里只把设置值传下去。
            note.Content?.Trim() ?? string.Empty,
            ContentDefaultFontSize,
            ContentDefaultColor,
            NoteMetaFontSize,
            CanModifyNotes,
            dueText,
            isOverdue,
            handwriting,
            isHandwriting ? BoardHandwritingAspect.Resolve(handwriting) : BoardHandwritingAspect.DefaultAspect,
            isHandwriting,
            // 标签整理好再交给模板：旧数据可能带空白项，也不希望模板自己去重。
            (IReadOnlyList<string>?)Note.NormalizeTags(note.Tags) ?? []);
    }
}
