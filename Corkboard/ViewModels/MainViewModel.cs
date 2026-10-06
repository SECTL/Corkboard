using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Attributes;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Services.Config;

namespace Corkboard.ViewModels;

/// <summary>
///     主界面壳的 ViewModel。主界面没有侧边导航栏，这里只保留内容区当前页面。
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

    private void OnBoardSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(BoardSettingsConfig.BoardName))
            OnPropertyChanged(nameof(PageTitle));
    }

    /// <summary>主界面壳离开可视树时断开订阅（配置是单例，见 BoardPageViewModel.Detach 的同类说明）。</summary>
    public void Detach()
    {
        Config.BoardSettings.PropertyChanged -= OnBoardSettingsChanged;
    }
}
