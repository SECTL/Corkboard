using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Attributes;
using Corkboard.Core.Board;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models.SubConfigs.General;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.ViewModels.MainPages.Board;
using Corkboard.Core.Services.Config;
using Corkboard.Services.Board;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels;

/// <summary>
///     主界面壳的 ViewModel。主界面没有侧边导航栏，这里只保留内容区当前页面，
///     外加标题栏上跟主页面有关的动作（当前是排序方式菜单）。
///     <para>
///         排序方式原来在「设置 → 作业板」的下拉框里，现在搬到标题栏：用户是看着板子决定怎么排的，
///     不该为了换个排序再开一遍设置窗口。排布（展示设置）仍留在设置里，两者分工见
///     <c>BoardSettingsPage</c>。
///     </para>
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    public MainViewModel(MainConfigHandler configHandler, BoardReplayService replay) : base(configHandler)
    {
        _replay = replay;
        Config.BoardSettings.PropertyChanged += OnBoardSettingsChanged;
        Config.Basic.PropertyChanged += OnBasicSettingsChanged;
        _replay.PropertyChanged += OnReplayChanged;
    }

    /// <summary>时空回放状态（标题栏的「布置作业 / 回放入口」显隐要看它）。</summary>
    private readonly BoardReplayService _replay;

    [ObservableProperty] private object? _frameContent;
    [ObservableProperty] private bool _isRequestedRestart;
    [ObservableProperty] private PageInfo? _selectedPageInfo;

    /// <summary>
    ///     标题栏排序菜单里的选项，顺序就是菜单里的顺序。与作业板设置页无关：
    ///     排序的入口只有这一处（列表是固定五项，配置里出现越界值时由选中项兜底）。
    /// </summary>
    public IReadOnlyList<BoardSortOption> SortOptions { get; } =
    [
        new(BoardSortMode.CreatedDescending, CR.Board_Sort_CreatedDescending),
        new(BoardSortMode.CreatedAscending, CR.Board_Sort_CreatedAscending),
        new(BoardSortMode.UpdatedDescending, CR.Board_Sort_UpdatedDescending),
        new(BoardSortMode.Subject, CR.Board_Sort_Subject),
        new(BoardSortMode.Manual, CR.Board_Sort_Manual)
    ];

    /// <summary>当前排序方式。配置里若是越界值（旧版本或手改过配置），退回第一项。</summary>
    public BoardSortOption SelectedSortOption =>
        SortOptions.FirstOrDefault(option => option.Mode == Config.BoardSettings.SortMode) ?? SortOptions[0];

    /// <summary>排序按钮的提示：把当前档写进去，不用点开菜单就知道现在按什么排。</summary>
    public string SortTip =>
        string.Format(CultureInfo.CurrentCulture, CR.Board_Sort_Tip, SelectedSortOption.DisplayName);

    /// <summary>
    ///     主窗口右下角那颗「布置作业」浮标画不画：设置里打开之外，窗口处于<b>解锁态</b>时也一并隐藏
    ///     ——那时内容区盖着遮罩、不能编辑作业，浮标只是一颗跟当前态打架的装饰。
    /// </summary>
    public bool ShowCornerAssignButton => Config.Basic.ShowCornerAssignButton && Config.Basic.WindowFixed;

    /// <summary>
    ///     窗口是不是处在「解锁（调整窗口）」态：解锁时内容区盖一层遮罩，
    ///     因为这时候能拖标题栏挪窗口、拉四边改大小，板子上的点按十有八九是误触。
    /// </summary>
    public bool IsWindowAdjusting => !Config.Basic.WindowFixed;

    /// <summary>
    ///     标题栏右上角那几样控件画不画（「设置 → 作业板 → 顶部按钮」）。
    ///     配置开关只决定「允不允许显示」，各自还要叠上原有条件：
    ///     布置作业在回放期间不显示、回放入口没作业时不显示、收纳自己还有开关。
    ///     <para>
    ///         整组还要叠一层「窗口是锁着的」：解锁态是调窗口的时候，布置作业 / 排序 / 时空回放
    ///         三件事动的都是板子本身，那时内容区盖着遮罩、做这些事没意义，索性一起收起来；
    ///         设置、导出、收纳与锁本身留着（它们不碰板子内容）。
    ///     </para>
    /// </summary>
    public bool ShowTitleBarSettings => Config.BoardSettings.ShowTitleBarSettings;

    /// <summary>
    ///     看板模式入口：点一下直接铺全屏（<see cref="App.ShowDisplayBoardWindow" />）。
    ///     只读动作、不碰板子内容，所以不叠「窗口锁着」那一层（与设置 / 导出 / 收纳同档）。
    /// </summary>
    public bool ShowTitleBarDisplay => Config.BoardSettings.ShowTitleBarDisplay;

    /// <summary>排序菜单那顆。</summary>
    public bool ShowTitleBarSort => Config.BoardSettings.ShowTitleBarSort && Config.Basic.WindowFixed;

    /// <summary>布置作业。</summary>
    public bool ShowTitleBarAssign =>
        Config.BoardSettings.ShowTitleBarAssign && !_replay.IsActive && Config.Basic.WindowFixed;

    /// <summary>导出图片。</summary>
    public bool ShowTitleBarExport => Config.BoardSettings.ShowTitleBarExport;

    /// <summary>时空回放入口。</summary>
    public bool ShowTitleBarReplay =>
        Config.BoardSettings.ShowTitleBarReplay && _replay.CanEnterReplay && Config.Basic.WindowFixed;

    /// <summary>收纳（两个开关都开着才显示）。</summary>
    public bool ShowTitleBarDock => Config.BoardSettings.ShowTitleBarDock && Config.BoardSettings.DockEnabled;

    /// <summary>
    ///     标题栏那颗「更多」画不画：排序 / 导出 / 时空回放 / 收纳四件事全收在它的菜单里，
    ///     四个都关掉就没什么可点的了，整颗一起收掉（免得弹出一个空菜单）。
    /// </summary>
    public bool ShowTitleBarMore =>
        ShowTitleBarSort || ShowTitleBarExport || ShowTitleBarReplay || ShowTitleBarDock;

    /// <summary>
    ///     标题栏那颗「锁定」开关现在是不是拨在「锁上」。
    ///     <para>
    ///         直接就是 <see cref="BasicSettingsConfig.WindowFixed" />：锁着 = 位置与大小都钉住、
    ///         板子照常查看与编辑；解锁 = 调整窗口态（能拖标题栏、能拉四边，内容区盖遮罩）。
    ///         这颗开关两边都能拨，因为解锁态窗口照样接收鼠标。
    ///     </para>
    /// </summary>
    public bool IsWindowLocked
    {
        get => Config.Basic.WindowFixed;
        set => Config.Basic.WindowFixed = value;
    }

    /// <summary>那颗锁的提示文案：按当前状态说清楚拨它会怎样。</summary>
    public string LockWindowTip => Config.Basic.WindowFixed
        ? CR.MainView_UnlockWindow
        : CR.MainView_LockWindow;

    // 菜单里的勾选不在这里：菜单项画在弹出层里、拿不到这里的 DataContext，
    // 所以由 MainView 在弹出时按 SelectedSortOption 直接写（见 MainView.RefreshMoreMenu）。

    /// <summary>
    ///     内容区标题。作业板的名称用户可以自己改（<see cref="BoardSettingsConfig.BoardName" />），
    ///     所以默认主页面读配置，其余页面仍用注册时给的名字。
    /// </summary>
    public string PageTitle => SelectedPageInfo is null
        ? string.Empty
        : SelectedPageInfo.Id == AppConsts.DefaultMainPageId
            ? Config.BoardSettings.ResolveBoardName()
            : SelectedPageInfo.Name;

    partial void OnSelectedPageInfoChanged(PageInfo? value)
    {
        OnPropertyChanged(nameof(PageTitle));
    }

    /// <summary>
    ///     标题栏排序菜单里的一项被点了：写进配置。
    ///     <para>
    ///         这里不用手动刷新界面：配置一变，<see cref="OnBoardSettingsChanged" /> 会通知勾选状态，
    ///     作业板页面自己也订阅了这条变化，会按新排序重建列表。
    ///     </para>
    /// </summary>
    [RelayCommand]
    private void SetSortMode(BoardSortMode mode)
    {
        if (Config.BoardSettings.SortMode != mode)
            Config.BoardSettings.SortMode = mode;
    }

    private void OnBoardSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(BoardSettingsConfig.BoardName))
            OnPropertyChanged(nameof(PageTitle));

        if (e.PropertyName is null or nameof(BoardSettingsConfig.SortMode))
        {
            // 提示里写着当前档（勾选由 MainView 在菜单弹出时按 SelectedSortOption 写）。
            OnPropertyChanged(nameof(SelectedSortOption));
            OnPropertyChanged(nameof(SortTip));
        }

        // 顶部按钮的显隐：字段少，逐个判名字不划算，直接连「更多」一起通知。
        OnPropertyChanged(nameof(ShowTitleBarSettings));
        OnPropertyChanged(nameof(ShowTitleBarDisplay));
        OnPropertyChanged(nameof(ShowTitleBarSort));
        OnPropertyChanged(nameof(ShowTitleBarAssign));
        OnPropertyChanged(nameof(ShowTitleBarExport));
        OnPropertyChanged(nameof(ShowTitleBarReplay));
        OnPropertyChanged(nameof(ShowTitleBarDock));
        OnPropertyChanged(nameof(ShowTitleBarMore));
    }

    /// <summary>回放状态变了：只有「布置作业」与「回放入口」（含装着它的「更多」）受它影响。</summary>
    private void OnReplayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BoardReplayService.IsActive) or null)
            OnPropertyChanged(nameof(ShowTitleBarAssign));

        if (e.PropertyName is nameof(BoardReplayService.CanEnterReplay) or nameof(BoardReplayService.IsActive) or null)
        {
            OnPropertyChanged(nameof(ShowTitleBarReplay));
            OnPropertyChanged(nameof(ShowTitleBarMore));
        }
    }

    private void OnBasicSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(BasicSettingsConfig.ShowCornerAssignButton))
            OnPropertyChanged(nameof(ShowCornerAssignButton));

        // 锁定状态牵一发动全身：那颗锁自己的状态与提示、内容区遮罩、
        // 以及整组「动板子」的标题栏按钮（布置作业 / 排序 / 时空回放）与右下角浮标。
        if (e.PropertyName is null or nameof(BasicSettingsConfig.WindowFixed))
        {
            OnPropertyChanged(nameof(IsWindowLocked));
            OnPropertyChanged(nameof(LockWindowTip));
            OnPropertyChanged(nameof(IsWindowAdjusting));
            OnPropertyChanged(nameof(ShowCornerAssignButton));
            OnPropertyChanged(nameof(ShowTitleBarSort));
            OnPropertyChanged(nameof(ShowTitleBarAssign));
            OnPropertyChanged(nameof(ShowTitleBarReplay));
            OnPropertyChanged(nameof(ShowTitleBarMore));
        }
    }

    /// <summary>主界面壳离开可视树时断开订阅（配置是单例，见 BoardPageViewModel.Detach 的同类说明）。</summary>
    public void Detach()
    {
        Config.BoardSettings.PropertyChanged -= OnBoardSettingsChanged;
        Config.Basic.PropertyChanged -= OnBasicSettingsChanged;
        _replay.PropertyChanged -= OnReplayChanged;
    }
}
