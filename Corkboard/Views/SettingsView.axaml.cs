using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DynamicData;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Enums;
using Corkboard.Core.Extensions;
using Corkboard.Core.Services;
using Corkboard.ViewModels;

namespace Corkboard.Views;

/// <summary>设置界面导航壳：与 <see cref="MainView" /> 同一套注册表/键控 DI 机制，额外带一个返回栈。</summary>
public partial class SettingsView : ContentPage, IFANavigationPageFactory
{
    private readonly FAFrame? _navigationFrame;
    private readonly FANavigationView? _navigationView;

    public SettingsView()
    {
        DataContext = this;
        InitializeComponent();

        _navigationFrame = this.FindControl<FAFrame>("NavigationFrame");
        _navigationView = this.FindControl<FANavigationView>("NavigationView");

        if (_navigationFrame is not null)
            _navigationFrame.NavigationPageFactory = this;

        ViewModel.NavigationViewItems
            .AddRange(PagesRegistryService.SettingsItems
                .Where(info => info.Location == PageLocation.Top)
                .ToNavigationViewItems(ViewModel.FlattenNavigationItems));

        ViewModel.NavigationViewFooterItems
            .AddRange(PagesRegistryService.SettingsItems
                .Where(info => info.Location == PageLocation.Bottom)
                .ToNavigationViewItems(ViewModel.FlattenNavigationItems));

        SelectNavigationItemById(AppConsts.DefaultSettingsPageId);
        UpdateBackButtonState();
    }

    public SettingsViewModel ViewModel { get; } = IAppHost.GetService<SettingsViewModel>();

    public Control? GetPage(Type srcType)
    {
        return Activator.CreateInstance(srcType) as Control;
    }

    public Control? GetPageFromObject(object target)
    {
        if (target is not PageInfo info) return null;

        var page = IAppHost.Host!.Services.GetKeyedService<UserControl>(info.Id);
        return page is not null ? page : new TextBlock { Text = $"页面 {info.Id} 未找到" };
    }

    public void SelectNavigationItemById(string id)
    {
        var info = PagesRegistryService.SettingsItems.FirstOrDefault(item => item.Id == id);
        if (info != null)
            Navigate(info, pushHistory: false);
    }

    private void Navigate(PageInfo info, bool pushHistory)
    {
        if (pushHistory && ViewModel.SelectedPageInfo is { } previous && previous.Id != info.Id)
            ViewModel.NavigationHistory.Add(previous);

        var item = ViewModel.FlattenNavigationItems.FirstOrDefault(entry => Equals(entry.Tag, info));
        if (item is not null)
            ViewModel.SelectedNavigationViewItem = item;

        ViewModel.SelectedPageInfo = info;
        if (_navigationFrame is not null)
            _navigationFrame.NavigateFromObject(info);

        UpdateBackButtonState();
    }

    private void UpdateBackButtonState()
    {
        ViewModel.CanGoBack = ViewModel.NavigationHistory.Count > 0;
    }

    private void NavigationView_OnItemInvoked(object? sender, FANavigationViewItemInvokedEventArgs e)
    {
        PageInfo? info = null;

        if (e.InvokedItemContainer is FANavigationViewItem { Tag: PageInfo containerInfo })
            info = containerInfo;
        else if (e.InvokedItem is PageInfo invokedInfo) info = invokedInfo;

        if (info != null) Navigate(info, pushHistory: true);
    }

    private void BackButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.NavigationHistory.Count == 0)
            return;

        var target = ViewModel.NavigationHistory[^1];
        ViewModel.NavigationHistory.RemoveAt(ViewModel.NavigationHistory.Count - 1);
        Navigate(target, pushHistory: false);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
