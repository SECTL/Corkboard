using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Attributes;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Services.Config;
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
    public MainViewModel(MainConfigHandler configHandler) : base(configHandler)
    {
        Config.BoardSettings.PropertyChanged += OnBoardSettingsChanged;
    }

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

    // 菜单里的勾选不在这里：菜单项画在弹出层里、拿不到这里的 DataContext，
    // 所以由 MainView 在弹出时按 SelectedSortOption 直接写（见 MainView.RefreshSortMenuChecks）。

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
    }

    /// <summary>主界面壳离开可视树时断开订阅（配置是单例，见 BoardPageViewModel.Detach 的同类说明）。</summary>
    public void Detach()
    {
        Config.BoardSettings.PropertyChanged -= OnBoardSettingsChanged;
    }
}
