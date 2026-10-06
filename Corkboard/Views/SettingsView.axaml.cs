using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DynamicData;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Corkboard.Core;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Controls;
using Corkboard.Core.Enums;
using Corkboard.Core.Extensions;
using Corkboard.Core.Services;
using Corkboard.Platforms.Abstractions;
using Corkboard.ViewModels;
using CR = Corkboard.Core.Langs.Common.Resources;
using LR = Corkboard.Langs.SettingsView.Resources;

namespace Corkboard.Views;

/// <summary>设置界面导航壳：与 <see cref="MainView" /> 同一套注册表/键控 DI 机制，额外带一个返回栈。</summary>
public partial class SettingsView : ContentPage, IFANavigationPageFactory
{
    /// <summary>标题栏右侧动作区与系统按钮之间留的空隙，别贴在一起。</summary>
    private const double SystemButtonsGap = 8;

    private readonly FAFrame? _navigationFrame;
    private readonly FANavigationView? _navigationView;

    /// <summary>标题栏右侧动作区（见 <see cref="OnLoadedForSystemButtonsReserve" />）。</summary>
    private readonly StackPanel? _titleBarActionArea;

    /// <summary>
    ///     平台窗口能力：标题栏右侧要让出多少给窗口自己的系统按钮。
    ///     控件构造期取一次（见 docs/project_rules.md 的静态服务定位规矩）。
    /// </summary>
    private readonly IWindowFeatureService? _windowFeatures;

    /// <summary>Debug 构建的版本水印是否已经挂上（见 <see cref="OnLoadedForDevelopmentAdorner" />）。</summary>
    private bool _isDevelopmentAdornerAdded;

    /// <summary>
    ///     当前设置界面实例。设置页（transient）需要它来请求重启提示，
    ///     因为提示按钮画在设置界面的标题栏上。
    /// </summary>
    public static SettingsView? Current { get; private set; }

    public SettingsView()
    {
        Current = this;
        DataContext = this;
        InitializeComponent();

        _navigationFrame = this.FindControl<FAFrame>("NavigationFrame");
        _navigationView = this.FindControl<FANavigationView>("NavigationView");
        _titleBarActionArea = this.FindControl<StackPanel>("TitleBarActionArea");
        _windowFeatures = IAppHost.TryGetService<IWindowFeatureService>();

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

        // 初始选中同样要等导航容器实例化之后再设置，见 OnLoadedForInitialNavigation。
        Loaded += OnLoadedForInitialNavigation;
        Loaded += OnLoadedForSystemButtonsReserve;
        Loaded += OnLoadedForDevelopmentAdorner;
        UpdateBackButtonState();
        Unloaded += (_, _) =>
        {
            if (ReferenceEquals(Current, this))
                Current = null;
        };
    }

    public SettingsViewModel ViewModel { get; } = IAppHost.GetService<SettingsViewModel>();

    /// <summary>
    ///     初始选中推迟到首次布局之后。FluentAvalonia 的 <c>FANavigationView.AnimateSelectionChanged</c>
    ///     在「选中项还没实例化出容器」时会把自己无限重新投递到 Dispatcher，UI 线程被自己的消息循环饿死，
    ///     窗口随即被系统判定为无响应（设置窗口一打开就卡死）。先走完一次布局再改选中项即可避开。
    /// </summary>
    private void OnLoadedForInitialNavigation(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedForInitialNavigation;

        Dispatcher.UIThread.Post(
            () =>
            {
                _navigationView?.UpdateLayout();
                SelectNavigationItemById(AppConsts.DefaultSettingsPageId);
            },
            DispatcherPriority.Background);
    }

    /// <summary>
    ///     标题栏右端那一带是窗口自己的系统按钮（最小化 / 最大化 / 关闭），而本窗口的内容
    ///     一直铺到标题栏底下。把系统按钮的宽度让出来，右侧动作区（「需要重启」提示）才会正好落在
    ///     最小化按钮左边，而不是被它们压住。宽度由平台实现给出（Windows 查 DWM，
    ///     其它平台没有系统按钮时是 0，此时退化成贴着右边、不留空位）。
    /// </summary>
    private void OnLoadedForSystemButtonsReserve(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedForSystemButtonsReserve;

        if (_windowFeatures is null
            || TopLevel.GetTopLevel(this) is not Window window
            || window.TryGetPlatformHandle() is not { } handle)
        {
            return;
        }

        var reserved = _windowFeatures.GetSystemCaptionButtonWidth(
            new PlatformWindowHandle(handle.Handle, handle.HandleDescriptor));
        if (reserved <= 0)
            return;

        _titleBarActionArea?.SetCurrentValue(MarginProperty, new Thickness(0, 0, reserved + SystemButtonsGap, 0));
    }

    /// <summary>
    ///     Debug 构建的左下角版本水印：往壳的 <c>AdornerLayer</c> 里塞一个
    ///     <see cref="DevelopmentBuildAdorner" />，它铺满整壳、不吃命中测试，只画一行字。
    ///     与上游 SecRandom-C 一致；Release 构建下 <see cref="GlobalConstants.IsDevelopment" />
    ///     为 false，整个方法直接返回。
    /// </summary>
    private void OnLoadedForDevelopmentAdorner(object? sender, RoutedEventArgs e)
    {
        if (!GlobalConstants.IsDevelopment || _isDevelopmentAdornerAdded || Content is not Control element)
            return;

        // AdornerLayer 由窗口模板提供；取不到就什么都不做（不抛、也不反复重试同一次加载）。
        if (AdornerLayer.GetAdornerLayer(element) is not { } layer)
            return;

        var adorner = new DevelopmentBuildAdorner();
        layer.Children.Add(adorner);
        AdornerLayer.SetAdornedElement(adorner, this);
        _isDevelopmentAdornerAdded = true;
    }

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
        {
            // 分组折叠时子项没有容器，选择动画会无限重投递（窗口卡死），先展开祖先后再走一次布局。
            // 设置项当前是平铺的（没有 groupId），这条只是分组回来时的保险。
            if (ViewModel.NavigationViewItems.ExpandGroupsContaining(item))
                _navigationView?.UpdateLayout();

            ViewModel.SelectedNavigationViewItem = item;
        }

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

    /// <summary>顶部「收纳」：折叠 / 展开左侧设置导航栏。</summary>
    private void TogglePaneButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_navigationView is not null)
            _navigationView.IsPaneOpen = !_navigationView.IsPaneOpen;
    }

    #region 重启引导

    /// <summary>防止同时弹出两个重启确认框。</summary>
    private bool _isShowingRestartDialog;

    /// <summary>
    ///     某个设置改了、但只有重启才生效时调用：标题栏右侧亮出「需要重启」按钮
    ///     （紧挨着窗口的最小化按钮左边，见 <see cref="OnLoadedForSystemButtonsReserve" />）。
    ///     与上游 SecRandom-C 一致——只亮提示，不打断用户。
    /// </summary>
    public void RequestRestartApp()
    {
        ViewModel.IsRequestedRestart = true;
    }

    private void RestartButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = ShowRestartDialogAsync();
    }

    private async Task ShowRestartDialogAsync()
    {
        if (_isShowingRestartDialog || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        _isShowingRestartDialog = true;
        try
        {
            var result = await new FAContentDialog
            {
                Title = LR.SettingsView_NeedsRestarting,
                Content = LR.SettingsView_NeedsRestartingMessage,
                PrimaryButtonText = LR.SettingsView_RestartNow,
                CloseButtonText = CR.Common_Cancel,
                DefaultButton = FAContentDialogButton.Primary
            }.ShowAsync(topLevel);

            if (result == FAContentDialogResult.Primary)
                App.Current.Restart();
        }
        finally
        {
            _isShowingRestartDialog = false;
        }
    }

    #endregion

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
