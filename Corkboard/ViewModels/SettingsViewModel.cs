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

    public bool IsMacOs => OperatingSystem.IsMacOS();

    public ObservableCollection<FANavigationViewItemBase> FlattenNavigationItems { get; } = [];
    public ObservableCollection<FANavigationViewItemBase> NavigationViewItems { get; } = [];
    public ObservableCollection<FANavigationViewItemBase> NavigationViewFooterItems { get; } = [];

    /// <summary>设置页返回栈。</summary>
    public ObservableCollection<PageInfo> NavigationHistory { get; } = [];
}
