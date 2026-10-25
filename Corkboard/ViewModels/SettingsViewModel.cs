using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;
using Corkboard.Core.Attributes;

namespace Corkboard.ViewModels;

/// <summary>设置界面的导航壳 ViewModel。</summary>
public partial class SettingsViewModel : ObservableRecipient
{
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private object? _frameContent;
    [ObservableProperty] private FANavigationViewItemBase? _selectedNavigationViewItem;
    [ObservableProperty] private PageInfo? _selectedPageInfo;
    [ObservableProperty] private bool _isRequestedRestart;

    /// <summary>右侧抽屉是否打开（关于页的贡献人员名单用它）。</summary>
    [ObservableProperty] private bool _isDrawerOpen;

    /// <summary>抽屉里放什么。由需要抽屉的页面在打开前塞进来（见 <see cref="Views.SettingsView.OpenDrawer" />）。</summary>
    [ObservableProperty] private object? _drawerContent;

    public bool IsMacOs => OperatingSystem.IsMacOS();

    public ObservableCollection<FANavigationViewItemBase> FlattenNavigationItems { get; } = [];
    public ObservableCollection<FANavigationViewItemBase> NavigationViewItems { get; } = [];
    public ObservableCollection<FANavigationViewItemBase> NavigationViewFooterItems { get; } = [];

    /// <summary>设置页返回栈。</summary>
    public ObservableCollection<PageInfo> NavigationHistory { get; } = [];
}
