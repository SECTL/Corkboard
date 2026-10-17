using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Board;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Services.Config;
using Corkboard.Services.Board;
using Corkboard.ViewModels;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Board;

/// <summary>
///     「收纳」设置页：开关、按钮位置（点示意图上的圆点）、左右 / 上下偏移与按钮大小。
///     <para>
///         位置是离散点击，点一下直接写配置（本来就是一次明确的动作）；
///         偏移与大小是连续输入，走草稿 + 失焦 / 离页提交，避免每敲一个字符就落一次盘
///         （<see cref="MainConfigHandler" /> 没有防抖）。
///     </para>
///     <para>
///         本页由键控 DI 以 transient 创建，而配置是单例：订阅了配置就必须在页面离开可视树时
///         断开（<see cref="Attach" /> / <see cref="Detach" /> 由页面 code-behind 调）。
///     </para>
/// </summary>
public partial class DockSettingsPageViewModel : ViewModelBase
{
    /// <summary>「恢复默认位置」写回的偏移（DIP），与 <see cref="BoardSettingsConfig" /> 的出厂值一致。</summary>
    private const double DefaultDockOffset = 24;

    /// <summary>「恢复默认位置」写回的缩放（%），与 <see cref="BoardSettingsConfig" /> 的出厂值一致。</summary>
    private const double DefaultDockScalePercent = 100;

    private readonly MainConfigHandler _configHandler;
    private bool _isAttached;

    public DockSettingsPageViewModel(MainConfigHandler configHandler, BoardDockService dockService)
        : base(configHandler)
    {
        _configHandler = configHandler;
        Dock = dockService;

        // 九宫格按枚举顺序摆：第一行是上排（左 / 中 / 右），最后一行是下排。
        AnchorOptions = new ObservableCollection<DockAnchorOption>(
            Enum.GetValues<DockAnchor>().Select(anchor => new DockAnchorOption(anchor, SelectAnchor)));

        DockOffsetXDraft = ToOffsetDraft(Settings.DockOffsetX);
        DockOffsetYDraft = ToOffsetDraft(Settings.DockOffsetY);
        DockScaleDraft = ToScaleDraft(Settings.DockScalePercent);
        RefreshAnchorSelection();
    }

    /// <summary>收纳服务：浮钮正在屏幕上时，改位置 / 大小会在落盘后立刻重摆（服务自己订阅配置）。</summary>
    public BoardDockService Dock { get; }

    public BoardSettingsConfig Settings => Config.BoardSettings;

    /// <summary>位置示意图上的九颗圆点（左上 → 右下）。</summary>
    public ObservableCollection<DockAnchorOption> AnchorOptions { get; }

    /// <summary>
    ///     当前贴靠位置的短文案（「右上」「左下」…），给设置页那颗像下拉框的按钮显示用。
    ///     九宫格本身收进浮层里，不占常驻面积。
    /// </summary>
    public string AnchorText => DockAnchors.Normalize(Settings.DockAnchor) switch
    {
        DockAnchor.TopLeft => CR.Settings_Board_Dock_Anchor_TopLeft,
        DockAnchor.TopCenter => CR.Settings_Board_Dock_Anchor_TopCenter,
        DockAnchor.TopRight => CR.Settings_Board_Dock_Anchor_TopRight,
        DockAnchor.MiddleLeft => CR.Settings_Board_Dock_Anchor_MiddleLeft,
        DockAnchor.Center => CR.Settings_Board_Dock_Anchor_Center,
        DockAnchor.MiddleRight => CR.Settings_Board_Dock_Anchor_MiddleRight,
        DockAnchor.BottomLeft => CR.Settings_Board_Dock_Anchor_BottomLeft,
        DockAnchor.BottomCenter => CR.Settings_Board_Dock_Anchor_BottomCenter,
        _ => CR.Settings_Board_Dock_Anchor_BottomRight
    };

    /// <summary>「左右偏移」的草稿：失焦 / 离页时经 <see cref="CommitAll" /> 夹取后写配置。</summary>
    [ObservableProperty]
    private decimal? _dockOffsetXDraft;

    /// <summary>「上下偏移」的草稿。</summary>
    [ObservableProperty]
    private decimal? _dockOffsetYDraft;

    /// <summary>「按钮大小」的草稿（百分比）。</summary>
    [ObservableProperty]
    private decimal? _dockScaleDraft;

    /// <summary>挂上配置变化订阅（页面进入可视树时调）。重复调用不会重复挂。</summary>
    public void Attach()
    {
        if (_isAttached)
            return;

        _isAttached = true;
        Settings.PropertyChanged += OnSettingsChanged;
        RefreshAnchorSelection();
    }

    /// <summary>断开配置变化订阅（页面离开可视树时调）。</summary>
    public void Detach()
    {
        if (!_isAttached)
            return;

        _isAttached = false;
        Settings.PropertyChanged -= OnSettingsChanged;
    }

    /// <summary>
    ///     点了示意图上的某颗圆点：直接写配置。
    ///     写完整理状态是因为配置变化也可能由外部（云端恢复设置）带进来。
    /// </summary>
    public void SelectAnchor(DockAnchor anchor)
    {
        var normalized = DockAnchors.Normalize(anchor);
        if (Settings.DockAnchor != normalized)
            Settings.DockAnchor = normalized;

        RefreshAnchorSelection();
    }

    /// <summary>「恢复默认位置」：靠右下角、内缩 24 DIP、100% 大小。</summary>
    public void ResetToDefault()
    {
        Settings.DockAnchor = DockAnchors.Default;
        Settings.DockOffsetX = DefaultDockOffset;
        Settings.DockOffsetY = DefaultDockOffset;
        Settings.DockScalePercent = DefaultDockScalePercent;

        DockOffsetXDraft = ToOffsetDraft(DefaultDockOffset);
        DockOffsetYDraft = ToOffsetDraft(DefaultDockOffset);
        DockScaleDraft = ToScaleDraft(DefaultDockScalePercent);
        RefreshAnchorSelection();

        _configHandler.Save();
    }

    /// <summary>把三份草稿夹取后写进配置并落盘（失焦、离开页面时调）。</summary>
    public void CommitAll()
    {
        CommitDockOffsetX();
        CommitDockOffsetY();
        CommitDockScale();

        // 配置每个属性变化本来就会自动落盘，这里再存一次是为了「改完立刻收工」的那一下，
        // 与其它设置页的 SaveAll 一致。
        _configHandler.Save();
    }

    private void CommitDockOffsetX()
    {
        var value = DockOffsetXDraft is { } draft
            ? BoardSettingsConfig.ClampDockOffset((double)draft)
            : BoardSettingsConfig.ClampDockOffset(Settings.DockOffsetX);

        if (Settings.DockOffsetX != value)
            Settings.DockOffsetX = value;

        var normalizedDraft = ToOffsetDraft(value);
        if (DockOffsetXDraft != normalizedDraft)
            DockOffsetXDraft = normalizedDraft;
    }

    private void CommitDockOffsetY()
    {
        var value = DockOffsetYDraft is { } draft
            ? BoardSettingsConfig.ClampDockOffset((double)draft)
            : BoardSettingsConfig.ClampDockOffset(Settings.DockOffsetY);

        if (Settings.DockOffsetY != value)
            Settings.DockOffsetY = value;

        var normalizedDraft = ToOffsetDraft(value);
        if (DockOffsetYDraft != normalizedDraft)
            DockOffsetYDraft = normalizedDraft;
    }

    private void CommitDockScale()
    {
        var value = DockScaleDraft is { } draft
            ? BoardSettingsConfig.ClampDockScale((double)draft)
            : BoardSettingsConfig.ClampDockScale(Settings.DockScalePercent);

        if (Settings.DockScalePercent != value)
            Settings.DockScalePercent = value;

        var normalizedDraft = ToScaleDraft(value);
        if (DockScaleDraft != normalizedDraft)
            DockScaleDraft = normalizedDraft;
    }

    /// <summary>配置里的锚点变了（本页点击或外部恢复设置）：把高亮的那颗圆点挪过去。</summary>
    private void RefreshAnchorSelection()
    {
        var current = DockAnchors.Normalize(Settings.DockAnchor);
        foreach (var option in AnchorOptions)
            option.IsSelected = option.Anchor == current;

        OnPropertyChanged(nameof(AnchorText));
    }

    /// <summary>配置变化：外部改了值就把草稿同步过来（与服务端 / 云端恢复走同一条路）。</summary>
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(BoardSettingsConfig.DockAnchor))
            RefreshAnchorSelection();

        if (e.PropertyName is null or nameof(BoardSettingsConfig.DockOffsetX))
            DockOffsetXDraft = ToOffsetDraft(Settings.DockOffsetX);

        if (e.PropertyName is null or nameof(BoardSettingsConfig.DockOffsetY))
            DockOffsetYDraft = ToOffsetDraft(Settings.DockOffsetY);

        if (e.PropertyName is null or nameof(BoardSettingsConfig.DockScalePercent))
            DockScaleDraft = ToScaleDraft(Settings.DockScalePercent);
    }

    private static decimal? ToOffsetDraft(double value) =>
        (decimal)BoardSettingsConfig.ClampDockOffset(value);

    private static decimal? ToScaleDraft(double value) =>
        (decimal)BoardSettingsConfig.ClampDockScale(value);
}

/// <summary>
///     位置示意图上的一颗圆点：记住它代表哪个贴靠位置，以及当前是不是选中的那颗。
///     <para>
///         点击命令建在圆点自己身上，XAML 的 <c>DataTemplate</c> 直接绑 <c>SelectCommand</c>，
///         不用在模板里去找祖先控件要命令。
///     </para>
/// </summary>
public partial class DockAnchorOption(DockAnchor anchor, Action<DockAnchor> select) : ObservableObject
{
    public DockAnchor Anchor { get; } = anchor;

    /// <summary>当前配置里的位置是不是这一颗（由页面 ViewModel 统一刷新）。</summary>
    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Select() => select(Anchor);
}
