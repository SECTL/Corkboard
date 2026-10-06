using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Windowing;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Services.Config;

namespace Corkboard.Views;

/// <summary>
///     桌面窗口外壳。<paramref name="scope" /> 决定它承载主界面还是设置界面，
///     并决定尺寸记忆写到配置的哪一组字段上。
/// </summary>
public partial class MainWindow : FAAppWindow
{
    private readonly string _scope;
    private double _lastNormalWidth = 1200;
    private double _lastNormalHeight = 800;

    public MainWindow() : this(AppConsts.MainWindowScope)
    {
    }

    public MainWindow(string scope)
    {
        _scope = scope;
        InitializeComponent();

        TitleBar.Height = 48;
        TitleBar.ExtendsContentIntoTitleBar = true;

        Content = scope == AppConsts.SettingsWindowScope ? new SettingsView() : new MainView();

        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        Closing += OnClosing;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (IAppHost.TryGetService<MainConfigHandler>() is not { } handler)
            return;

        var basic = handler.Data.Basic;
        if (_scope == AppConsts.SettingsWindowScope)
        {
            _lastNormalWidth = basic.SettingsWindowWidth;
            _lastNormalHeight = basic.SettingsWindowHeight;
            if (basic.SettingsWindowMaximized)
                WindowState = WindowState.Maximized;
            else
                ApplySize(basic.SettingsWindowWidth, basic.SettingsWindowHeight);
        }
        else
        {
            _lastNormalWidth = basic.MainWindowWidth;
            _lastNormalHeight = basic.MainWindowHeight;
            if (basic.MainWindowMaximized)
                WindowState = WindowState.Maximized;
            else
                ApplySize(basic.MainWindowWidth, basic.MainWindowHeight);
        }
    }

    private void ApplySize(double width, double height)
    {
        Width = Math.Max(MinWidth, width);
        Height = Math.Max(MinHeight, height);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // 最大化/全屏时 Bounds 是屏幕尺寸，不能拿去当"上次普通尺寸"。
        if (WindowState != WindowState.Normal)
            return;

        _lastNormalWidth = Math.Max(MinWidth, e.NewSize.Width);
        _lastNormalHeight = Math.Max(MinHeight, e.NewSize.Height);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (IAppHost.TryGetService<MainConfigHandler>() is not { } handler)
            return;

        var basic = handler.Data.Basic;
        if (!basic.AutoSaveWindowSize)
            return;

        if (_scope == AppConsts.SettingsWindowScope)
        {
            basic.SettingsWindowWidth = _lastNormalWidth;
            basic.SettingsWindowHeight = _lastNormalHeight;
            basic.SettingsWindowMaximized = WindowState == WindowState.Maximized;
        }
        else
        {
            basic.MainWindowWidth = _lastNormalWidth;
            basic.MainWindowHeight = _lastNormalHeight;
            basic.MainWindowMaximized = WindowState == WindowState.Maximized;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
