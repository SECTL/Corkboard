using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DynamicData;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Controls;
using Corkboard.Core.Enums;
using Corkboard.Core.Extensions;
using Corkboard.Core.Icons;
using Corkboard.Core.Services;
using Corkboard.Helpers;
using Corkboard.ViewModels;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views;

/// <summary>
///     主界面导航壳：从 <see cref="PagesRegistryService" /> 构建菜单，通过键控 DI 取页面实例。
///     <para>
///         导航集合只在构造期写入一次——运行期改集合会破坏 FluentAvalonia 的选择源，
///         需要动态显隐时只切换项的 <c>IsVisible</c>。
///     </para>
/// </summary>
public partial class MainView : ContentPage, IFANavigationPageFactory
{
    private readonly FAFrame? _navigationFrame;
    private readonly FANavigationView? _navigationView;

    public MainView()
    {
        Current = this;
        DataContext = this;
        InitializeComponent();

        _navigationFrame = this.FindControl<FAFrame>("NavigationFrame");
        _navigationView = this.FindControl<FANavigationView>("NavigationView");

        if (_navigationFrame is not null)
            _navigationFrame.NavigationPageFactory = this;

        BuildNavigationMenuItems();
        SelectNavigationItemById(AppConsts.DefaultMainPageId);

        Unloaded += (_, _) =>
        {
            if (ReferenceEquals(Current, this))
                Current = null;
        };
    }

    public static MainView? Current { get; private set; }

    public MainViewModel ViewModel { get; } = IAppHost.GetService<MainViewModel>();
    public bool IsMacOs => OperatingSystem.IsMacOS();

    public Control? GetPage(Type srcType)
    {
        return Activator.CreateInstance(srcType) as Control;
    }

    /// <summary>页面工厂：<see cref="PageInfo.Id" /> 就是键控 DI 的 key。</summary>
    public Control? GetPageFromObject(object target)
    {
        if (target is not PageInfo info) return null;

        var page = IAppHost.Host!.Services.GetKeyedService<UserControl>(info.Id);
        if (page == null)
            return new TextBlock { Text = $"页面 {info.Id} 未找到" };

        return page;
    }

    private void BuildNavigationMenuItems()
    {
        ViewModel.NavigationViewItems.Clear();
        ViewModel.NavigationViewFooterItems.Clear();
        ViewModel.FlattenNavigationItems.Clear();

        ViewModel.NavigationViewItems
            .AddRange(PagesRegistryService.MainItems
                .Where(info => info.Location == PageLocation.Top)
                .ToNavigationViewItems(ViewModel.FlattenNavigationItems));

        ViewModel.NavigationViewFooterItems
            .AddRange(PagesRegistryService.MainItems
                .Where(info => info.Location == PageLocation.Bottom)
                .ToNavigationViewItems(ViewModel.FlattenNavigationItems));

        ViewModel.NavigationViewFooterItems.Add(CreateSettingsNavigationItem());
    }

    /// <summary>
    ///     设置入口打开独立窗口，不参与导航选中，否则左侧高亮会离开仍然可见的当前页面。
    /// </summary>
    public static FANavigationViewItemBase CreateSettingsNavigationItem()
    {
        var settingsPageInfo = new PageInfo(@"settings", FluentIcons.SettingsFilled, null, PageLocation.Bottom)
        {
            Name = CR.Feat_Settings
        };

        var settingsItem = settingsPageInfo.ToNavigationViewItemBase();
        if (settingsItem is FANavigationViewItem navigationItem)
            navigationItem.SelectsOnInvoked = false;

        return settingsItem;
    }

    public void SelectNavigationItemById(string id)
    {
        var info = PagesRegistryService.MainItems.FirstOrDefault(item => item.Id == id);
        if (info != null) CoreNavigate(info);
    }

    private void SelectNavigationItem(PageInfo info)
    {
        var item = ViewModel.FlattenNavigationItems.FirstOrDefault(entry => Equals(entry.Tag, info));

        // 不向 FANavigationView 写入 null 选中项：该过渡会在 FluentAvalonia 待触发标志仍置位时
        // 以 null 触发 ItemInvoked 并抛 NullReferenceException。找不到入口时保留原高亮。
        if (item is not null)
            ViewModel.SelectedNavigationViewItem = item;
    }

    private void CoreNavigate(PageInfo info)
    {
        if (info.Id == "settings")
        {
            App.ShowSettingsWindow();
            return;
        }

        ViewModel.FrameContent = null;
        SelectNavigationItem(info);
        ViewModel.SelectedPageInfo = info;
        if (_navigationFrame is not null)
            UiMotion.NavigateFromObject(_navigationFrame, info);
    }

    private void NavigationView_OnItemInvoked(object? sender, FANavigationViewItemInvokedEventArgs e)
    {
        PageInfo? info = null;

        if (e.InvokedItemContainer is FANavigationViewItem { Tag: PageInfo containerInfo })
            info = containerInfo;
        else if (e.InvokedItem is PageInfo invokedInfo) info = invokedInfo;

        if (info != null) CoreNavigate(info);
    }

    private void TogglePaneButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_navigationView is not null)
            _navigationView.IsPaneOpen = !_navigationView.IsPaneOpen;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
