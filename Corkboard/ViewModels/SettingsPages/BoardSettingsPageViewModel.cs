using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Abstraction.Services;
using Corkboard.Core.Enums;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models.Board;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Services.Config;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages;

/// <summary>
///     作业板设置页的 ViewModel：名称与科目走草稿后提交，作业类型与字段直接改领域服务的集合，
///     内容的默认字号与颜色走草稿 + 防抖。
///     <para>
///         类型与科目存在 <c>board.json</c>（业务数据），由 <c>BoardService</c> 显式落盘，
///         所以这里没有配置管道那种「集合元素不会被自动保存」的问题：改完喊一声
///         <see cref="SaveAll" /> 就行。所有输入框都用 <c>LostFocus</c> 触发它。
///     </para>
/// </summary>
public partial class BoardSettingsPageViewModel : ViewModelBase
{
    private readonly IBoardService _boardService;
    private readonly MainConfigHandler _configHandler;

    public BoardSettingsPageViewModel(MainConfigHandler configHandler, IBoardService boardService)
        : base(configHandler)
    {
        _boardService = boardService;
        _configHandler = configHandler;
        BoardNameDraft = Board.BoardName;
        SubjectsDraft = string.Join("，", _boardService.Subjects);
        _defaultFontSizeDraft = (decimal)BoardContentStyle.ClampFontSize(Board.DefaultContentFontSize);

        // 老版本的 settings.json 里没有色板字段（或者被手改脏了），进页面时先在原地收拾干净，
        // 否则浮窗上会出现透明/重复的色块。
        Board.NormalizePalette();
        ReloadPaletteItems();
    }

    public BoardSettingsConfig Board => Config.BoardSettings;

    /// <summary>作业类型。设置页直接增删改，改完调 <see cref="SaveAll" />。</summary>
    public ObservableCollection<BoardTypeDef> Types => _boardService.Types;

    /// <summary>排布方式下拉项。主页面顶部不再放切换器，排布只在这里选。</summary>
    public IReadOnlyList<BoardLayoutOption> LayoutOptions { get; } =
    [
        new(BoardLayoutMode.SingleColumn, CR.Board_Layout_SingleColumn),
        new(BoardLayoutMode.Block, CR.Board_Layout_Block),
        new(BoardLayoutMode.Flow, CR.Board_Layout_Flow)
    ];

    public IReadOnlyList<BoardSortOption> SortOptions { get; } =
    [
        new(BoardSortMode.CreatedDescending, CR.Settings_Board_Sort_CreatedDescending),
        new(BoardSortMode.CreatedAscending, CR.Settings_Board_Sort_CreatedAscending),
        new(BoardSortMode.UpdatedDescending, CR.Settings_Board_Sort_UpdatedDescending),
        new(BoardSortMode.Subject, CR.Settings_Board_Sort_Subject),
        new(BoardSortMode.Manual, CR.Settings_Board_Sort_Manual)
    ];

    /// <summary>
    ///     作业板名称的输入草稿。**不直接绑配置**：配置每变一次就落一次盘（见 ConfigHandlerBase），
    ///     双向绑到配置会让每敲一个字符写一遍 settings.json。
    /// </summary>
    [ObservableProperty] private string _boardNameDraft;

    /// <summary>科目的输入草稿，逗号分隔。</summary>
    [ObservableProperty] private string _subjectsDraft;

    /// <summary>
    ///     内容默认字号的输入草稿。**不直接双向绑配置**：NumericUpDown 每动一下值就落一次盘
    ///     （<c>ConfigHandlerBase</c> 没有防抖），所以先存草稿，失焦 / 离开页面时再提交。
    /// </summary>
    [ObservableProperty] private decimal? _defaultFontSizeDraft;

    /// <summary>取色器停手多久才把颜色写进配置。拖光谱时每帧都在变，写盘要攒一攒。</summary>
    private static readonly TimeSpan ColorCommitDelay = TimeSpan.FromMilliseconds(400);

    private Color? _defaultColorDraft;
    private DispatcherTimer? _defaultColorCommitTimer;

    /// <summary>
    ///     默认颜色的取色器草稿。理由同字号：每帧都写配置会把 <c>settings.json</c> 写爆。
    /// </summary>
    public Color DefaultColorDraft
    {
        get => _defaultColorDraft ?? Board.DefaultContentColor ?? ResolveThemeForeground();
        set
        {
            if (_defaultColorDraft == value)
                return;

            _defaultColorDraft = value;
            OnPropertyChanged();
            ScheduleColorCommit(value);
        }
    }

    /// <summary>默认颜色是否跟随主题（配置里为空就是跟随），对应设置卡上的开关。</summary>
    public bool FollowThemeColor
    {
        get => Board.DefaultContentColor is null;
        set
        {
            if (value == FollowThemeColor)
                return;

            CancelColorCommit();

            if (value)
            {
                Board.DefaultContentColor = null;
            }
            else
            {
                // 从「跟随主题」切到自定义时，起点取主题当前的前景色——
                // 直接给黑色的话，深色模式下整块作业内容会看不见。
                _defaultColorDraft ??= ResolveThemeForeground();
                Board.DefaultContentColor = _defaultColorDraft;
                OnPropertyChanged(nameof(DefaultColorDraft));
            }

            // 取色器只在「自定义」时显示，换档位要通知这一条。
            OnPropertyChanged();
        }
    }

    /// <summary>主题资源里没有「前景色」这个设置项，只能按当前主题取一次。</summary>
    private static Color ResolveThemeForeground()
    {
        // 条件访问不能带 out 参数，所以先取实例再查资源。
        if (Application.Current is { } application
            && application.TryGetResource("TextFillColorPrimaryBrush", null, out var resource)
            && resource is ISolidColorBrush brush)
        {
            return brush.Color;
        }

        return Colors.Black;
    }

    private void ScheduleColorCommit(Color value)
    {
        _defaultColorCommitTimer?.Stop();

        _defaultColorCommitTimer = new DispatcherTimer { Interval = ColorCommitDelay };
        _defaultColorCommitTimer.Tick += (_, _) =>
        {
            CancelColorCommit();

            // 已经切回「跟随主题」了：别再把手里的颜色写回配置。
            if (!FollowThemeColor)
                Board.DefaultContentColor = value;
        };
        _defaultColorCommitTimer.Start();
    }

    private void CancelColorCommit()
    {
        _defaultColorCommitTimer?.Stop();
        _defaultColorCommitTimer = null;
    }

    /// <summary>把还压在草稿里的颜色立刻写进配置，并停掉待触发的防抖计时。</summary>
    public void FlushColorDraft()
    {
        CancelColorCommit();

        if (_defaultColorDraft is { } draft && !FollowThemeColor && Board.DefaultContentColor != draft)
            Board.DefaultContentColor = draft;
    }

    /// <summary>提交内容的默认样式（字号草稿 + 颜色草稿）。</summary>
    public void CommitContentDefaults()
    {
        var size = DefaultFontSizeDraft is { } draft
            ? BoardContentStyle.ClampFontSize((double)draft)
            : BoardContentStyle.ClampFontSize(Board.DefaultContentFontSize);

        if (Math.Abs(Board.DefaultContentFontSize - size) > double.Epsilon)
            Board.DefaultContentFontSize = size;

        // 草稿被夹过（手输 999 之类）时同步回界面，免得显示的和落盘的不是一个数。
        var normalized = (decimal)size;
        if (DefaultFontSizeDraft != normalized)
            DefaultFontSizeDraft = normalized;

        FlushColorDraft();
    }

    /// <summary>排布下拉选中项。配置里若是越界值（旧版本或手改过配置），就退回第一项。</summary>
    public BoardLayoutOption SelectedLayoutOption
    {
        get => LayoutOptions.FirstOrDefault(option => option.Mode == Board.LayoutMode) ?? LayoutOptions[0];
        set
        {
            if (value is not null)
                Board.LayoutMode = value.Mode;
        }
    }

    /// <summary>下拉选中项。配置里若是越界值（旧版本或手改过配置），就退回第一项。</summary>
    public BoardSortOption SelectedSortOption
    {
        get => SortOptions.FirstOrDefault(option => option.Mode == Board.SortMode) ?? SortOptions[0];
        set
        {
            if (value is not null)
                Board.SortMode = value.Mode;
        }
    }

    /// <summary>加一个空类型。刚建出来没有字段，用户再往里加——「卷子」这种本来就不需要字段。</summary>
    [RelayCommand]
    private void AddType()
    {
        _boardService.Types.Add(new BoardTypeDef());
        _boardService.Save();
    }

    public void RemoveType(BoardTypeDef type)
    {
        if (!_boardService.Types.Remove(type))
            return;

        _boardService.Save();
    }

    public void AddField(BoardTypeDef type)
    {
        type.Fields.Add(new BoardTypeField { Kind = BoardFieldKind.Text });
        _boardService.Save();
    }

    /// <summary>字段对象在类型之间是唯一的，所以按引用找到属主再删，页面不用额外传类型。</summary>
    public void RemoveField(BoardTypeField field)
    {
        foreach (var type in _boardService.Types)
        {
            if (!type.Fields.Remove(field))
                continue;

            _boardService.Save();
            return;
        }
    }

    /// <summary>设置页所有编辑的收口：草稿提交 + 类型/科目落盘。输入框失焦与离开页面时都会调。</summary>
    public void SaveAll()
    {
        CommitBoardName();
        CommitSubjects();
        CommitContentDefaults();
        FlushPaletteCommit();
        _boardService.Save();
    }

    /// <summary>把名称草稿写回配置。配置自己会落盘。</summary>
    public void CommitBoardName()
    {
        var name = BoardNameDraft.Trim();
        if (Board.BoardName != name)
            Board.BoardName = name;
    }

    /// <summary>把科目草稿（逗号分隔，中英文逗号都认）写回领域服务。</summary>
    public void CommitSubjects()
    {
        var parsed = SubjectsDraft
            .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.CurrentCulture)
            .ToList();

        if (parsed.SequenceEqual(_boardService.Subjects))
            return;

        _boardService.Subjects.Clear();
        foreach (var subject in parsed)
            _boardService.Subjects.Add(subject);
    }

    #region 工具浮窗色板

    /// <summary>
    ///     工具浮窗上那排预设色，包成可绑定的行对象给设置页。<c>Color</c> 是结构体，
    ///     直接摆一个 <c>ObservableCollection&lt;Color&gt;</c> 的话取色器改不动它（见
    ///     <see cref="BoardPaletteColorItem" />）。
    ///     <para>
    ///         集合元素也不会被 <c>ConfigHandlerBase</c> 订阅（只订阅对象属性），
    ///         所以每次改动都要显式落盘。
    ///     </para>
    /// </summary>
    public ObservableCollection<BoardPaletteColorItem> PaletteColors { get; } = [];

    /// <summary>色板到 5 个上限了（「新增」按钮据此禁用），上限本身在 <see cref="BoardPalette" /> 里。</summary>
    public bool CanAddPaletteColor => BoardPalette.CanAdd(PaletteColors.Count);

    /// <summary>从配置读一次色板，灌进设置页的行对象。</summary>
    private void ReloadPaletteItems()
    {
        PaletteColors.Clear();
        foreach (var color in Board.ResolvePaletteColors())
            PaletteColors.Add(new BoardPaletteColorItem(color, OnPaletteColorItemChanged));

        OnPropertyChanged(nameof(CanAddPaletteColor));
    }

    /// <summary>某一个色块的取色器动了：防抖之后写回配置。</summary>
    private void OnPaletteColorItemChanged(BoardPaletteColorItem item, Color color)
    {
        var index = PaletteColors.IndexOf(item);
        if (index < 0)
            return;

        SchedulePaletteColorCommit(index, color);
    }

    /// <summary>再加一个预设色。</summary>
    [RelayCommand]
    private void AddPaletteColor()
    {
        if (!CanAddPaletteColor)
            return;

        // 新色从默认色板里挑第一个还没用过的，免得加出来跟已有的撞成一样。
        var used = PaletteColors.Select(item => item.Color).ToList();
        var candidate = BoardPalette.DefaultColors.FirstOrDefault(color => !used.Contains(color));

        PaletteColors.Add(new BoardPaletteColorItem(candidate, OnPaletteColorItemChanged));
        PersistPalette();
    }

    public void RemovePaletteColor(BoardPaletteColorItem item)
    {
        if (!PaletteColors.Remove(item))
            return;

        PersistPalette();
    }

    /// <summary>把某个预设色往左挪一格。</summary>
    public void MovePaletteColorUp(BoardPaletteColorItem item) => MovePaletteColor(item, -1);

    /// <summary>把某个预设色往右挪一格。</summary>
    public void MovePaletteColorDown(BoardPaletteColorItem item) => MovePaletteColor(item, 1);

    private void MovePaletteColor(BoardPaletteColorItem item, int offset)
    {
        var index = PaletteColors.IndexOf(item);
        var target = index + offset;

        if (index < 0 || target < 0 || target >= PaletteColors.Count)
            return;

        PaletteColors.Move(index, target);

        // 顺序也是配置的一部分，挪完要落盘。
        PersistPalette();
    }

    /// <summary>
    ///     取色器拖一次光谱会发上百次变更，攒一下再落盘。按色块下标分别计时，
    ///     这样连改多个色块时不会互相把对方的计时顶掉。
    /// </summary>
    private readonly Dictionary<int, DispatcherTimer> _paletteCommitTimers = [];

    /// <summary>色块颜色变更的入口：防抖之后把行对象的值写回配置。</summary>
    private void SchedulePaletteColorCommit(int index, Color color)
    {
        if (_paletteCommitTimers.Remove(index, out var pending))
            pending.Stop();

        var timer = new DispatcherTimer { Interval = ColorCommitDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _paletteCommitTimers.Remove(index);

            // 期间可能已经删过色块，下标要先确认还在范围内。
            if (index < PaletteColors.Count)
                PersistPalette();
        };

        _paletteCommitTimers[index] = timer;
        timer.Start();
    }

    /// <summary>把还压在防抖里的色板改动立刻落盘（离开页面 / 保存时调）。</summary>
    public void FlushPaletteCommit()
    {
        if (_paletteCommitTimers.Count == 0)
            return;

        foreach (var timer in _paletteCommitTimers.Values)
            timer.Stop();

        _paletteCommitTimers.Clear();
        PersistPalette();
    }

    /// <summary>把界面上的色板写回配置并落盘。</summary>
    private void PersistPalette()
    {
        Board.ApplyPaletteColors(PaletteColors.Select(item => item.Color));
        OnPropertyChanged(nameof(CanAddPaletteColor));
        _configHandler.Save();
    }

    #endregion
}
