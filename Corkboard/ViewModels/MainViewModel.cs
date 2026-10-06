using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;
using Corkboard.Core.Attributes;
using Corkboard.Core.Services.Config;

namespace Corkboard.ViewModels;

/// <summary>主界面导航壳的 ViewModel。</summary>
public partial class MainViewModel(MainConfigHandler configHandler) : ViewModelBase(configHandler)
{
    [ObservableProperty] private object? _frameContent;
    [ObservableProperty] private bool _isRequestedRestart;
    [ObservableProperty] private FANavigationViewItemBase? _selectedNavigationViewItem;
    [ObservableProperty] private PageInfo? _selectedPageInfo;
    [ObservableProperty] private bool _isNavPaneToggleButtonVisible;

    /// <summary>扁平化的全部导航项，用于按 <see cref="PageInfo" /> 反查选中项。</summary>
    public ObservableCollection<FANavigationViewItemBase> FlattenNavigationItems { get; } = [];

    public ObservableCollection<FANavigationViewItemBase> NavigationViewItems { get; } = [];
    public ObservableCollection<FANavigationViewItemBase> NavigationViewFooterItems { get; } = [];
}
