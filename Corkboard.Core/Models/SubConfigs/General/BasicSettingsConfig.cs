using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Enums.Configs;

namespace Corkboard.Core.Models.SubConfigs.General;

/// <summary>基础设置：语言、启动行为、窗口尺寸记忆。</summary>
public partial class BasicSettingsConfig : ObservableObject
{
    [ObservableProperty] private LanguageMode _language = LanguageMode.ChineseSimplified;
    [ObservableProperty] private bool _showStartupWindow = true;
    [ObservableProperty] private bool _autoSaveWindowSize = true;
    [ObservableProperty] private bool _backgroundResident = true;
    [ObservableProperty] private bool _urlProtocol;

    // 与用户开关分开存放：关掉「记住窗口尺寸」时要保留上一次的尺寸。
    [ObservableProperty] private double _mainWindowWidth = 1200;
    [ObservableProperty] private double _mainWindowHeight = 800;
    [ObservableProperty] private bool _mainWindowMaximized;
    [ObservableProperty] private double _settingsWindowWidth = 1000;
    [ObservableProperty] private double _settingsWindowHeight = 720;
    [ObservableProperty] private bool _settingsWindowMaximized;

    // 隐藏配置项：首次运行引导与协议确认状态。
    [ObservableProperty] private bool _guideCompleted;
    [ObservableProperty] private int _acceptedEulaVersion;
}
