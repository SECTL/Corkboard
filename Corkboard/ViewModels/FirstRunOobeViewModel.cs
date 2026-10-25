using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Models.SubConfigs.General;
using Corkboard.Core.Models.SubConfigs.Personalized;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Services.FirstRun;
using Corkboard.Platforms.Abstractions;
using Corkboard.ViewModels.SettingsPages;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels;

/// <summary>
///     首次运行引导（OOBE）窗口的 ViewModel：语言 / 协议 / 作业板 / 外观 / 启动 / 汇总。
/// </summary>
/// <remarks>
///     <para>
///         这里只做「把用户的选择写进配置」——写下去就落盘，与设置页改同一批键，所以引导里选过的东西
///         在设置里看到的就是同一份值，没有第二套状态。
///     </para>
///     <para>
///         走哪几步由 <see cref="FirstRunOobeService.Requirement" /> 决定：整段引导是六步，
///         协议改版后只补「协议」一步（见 <see cref="BuildSteps" />）。
///     </para>
/// </remarks>
public partial class FirstRunOobeViewModel : ObservableObject
{
    /// <summary>取色器的草稿停手多久才写进配置（拖光谱时每帧都在变，写盘要攒一攒）。</summary>
    private static readonly TimeSpan ThemeColorCommitDelay = TimeSpan.FromMilliseconds(400);

    private readonly IAutostartService _autostart;
    private readonly MainConfigHandler _configHandler;
    private readonly FirstRunOobeService _oobe;
    private readonly IReadOnlyList<OobeStepKind> _steps;

    private DispatcherTimer? _themeColorCommitTimer;
    private Color? _themeColorDraft;

    public FirstRunOobeViewModel(
        MainConfigHandler configHandler,
        FirstRunOobeService oobe,
        IAutostartService autostart)
    {
        _configHandler = configHandler;
        _oobe = oobe;
        _autostart = autostart;

        _steps = BuildSteps(oobe.Requirement);
        _selectedStep = 0;

        // 「协议改版后只补法律那一步」时，老用户上一轮已经勾过了，不该再让勾一遍：
        // 两个版本号都到齐了就直接放行（下面两个勾选框仍然可以取消，只是默认已勾）。
        var basic = _configHandler.Data.Basic;
        _acceptedPrivacyPolicy = basic.AcceptedPrivacyPolicyVersion >= FirstRunOobeService.CurrentPrivacyPolicyVersion;
        _acceptedEula = basic.AcceptedEulaVersion >= FirstRunOobeService.CurrentEulaVersion;
    }

    /// <summary>窗口里画的图标（欢迎页那颗与顶栏那颗共用一份，见 <c>AppIcon</c>）。</summary>
    public static Avalonia.Media.Imaging.Bitmap AppIcon => Services.Desktop.AppIcon.LoadBitmap();

    public AppearanceSettingsConfig Appearance => _configHandler.Data.Appearance;

    public BoardSettingsConfig Board => _configHandler.Data.BoardSettings;

    public BasicSettingsConfig Basic => _configHandler.Data.Basic;

    [ObservableProperty] private int _selectedStep;

    [ObservableProperty] private bool _acceptedPrivacyPolicy;

    [ObservableProperty] private bool _acceptedEula;

    /// <summary>底栏那行红字：目前只有「开机自启没设成功」会用到。</summary>
    [ObservableProperty] private string? _errorMessage;

    /// <summary>语言下拉项的来源：只有中文和英文，显示名固定用各自的写法。</summary>
    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new(LanguageMode.ChineseSimplified, CR.Settings_Basic_Language_ChineseSimplified),
        new(LanguageMode.English, CR.Settings_Basic_Language_English)
    ];

    /// <summary>
    ///     界面语言。改这一项要重启才对全部界面生效——引导本身也是界面，所以交给窗口去重启：
    ///     引导还没走完（<c>GuideCompleted</c> 仍为假），重启后回到第一步，只是换了语言。
    /// </summary>
    public LanguageOption SelectedLanguage
    {
        get => LanguageOptions.FirstOrDefault(option => option.Mode == Basic.Language) ?? LanguageOptions[0];
        set
        {
            if (value is null || value.Mode == Basic.Language)
                return;

            Basic.Language = value.Mode;
            LanguageChangeRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>语言改了：由窗口重启一次应用（引导会以新语言重新开始）。</summary>
    public event EventHandler? LanguageChangeRequested;

    /// <summary>主题模式下拉项。</summary>
    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new(ThemeMode.FollowSystem, CR.Settings_Appearance_Theme_FollowSystem),
        new(ThemeMode.Light, CR.Settings_Appearance_Theme_Light),
        new(ThemeMode.Dark, CR.Settings_Appearance_Theme_Dark)
    ];

    /// <summary>主题色来源下拉项。</summary>
    public IReadOnlyList<ThemeColorModeOption> ThemeColorModeOptions { get; } =
    [
        new(ThemeColorMode.Default, CR.Settings_Appearance_ThemeColorMode_Default),
        new(ThemeColorMode.Custom, CR.Settings_Appearance_ThemeColorMode_Custom)
    ];

    public ThemeOption SelectedTheme
    {
        get => ThemeOptions.FirstOrDefault(option => option.Mode == Appearance.Theme) ?? ThemeOptions[0];
        set
        {
            if (value is null || value.Mode == Appearance.Theme)
                return;

            Appearance.Theme = value.Mode;
            ApplyAppearance();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CompletedThemeText));
        }
    }

    public ThemeColorModeOption SelectedThemeColorMode
    {
        get => ThemeColorModeOptions.FirstOrDefault(option => option.Mode == Appearance.ThemeColorMode)
               ?? ThemeColorModeOptions[0];
        set
        {
            if (value is null || value.Mode == Appearance.ThemeColorMode)
                return;

            Appearance.ThemeColorMode = value.Mode;
            ApplyAppearance();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustomThemeColor));
            OnPropertyChanged(nameof(CompletedThemeText));
        }
    }

    /// <summary>取色器只在「自定义」时显示。</summary>
    public bool IsCustomThemeColor => Appearance.ThemeColorMode == ThemeColorMode.Custom;

    /// <summary>
    ///     取色器的草稿值：拖光谱会连发几十次变化，而配置一改就落一次盘，
    ///     所以先进草稿，停手 <see cref="ThemeColorCommitDelay" /> 之后才写配置（与外观设置页同一套）。
    /// </summary>
    public Color ThemeColorDraft
    {
        get => _themeColorDraft ?? Appearance.ThemeColor;
        set
        {
            if (_themeColorDraft == value || Appearance.ThemeColor == value)
                return;

            _themeColorDraft = value;
            OnPropertyChanged();
            ScheduleThemeColorCommit(value);
        }
    }

    /// <summary>当前平台是否实现了开机自启：没实现的平台整行隐藏（不留一个点不动的开关）。</summary>
    public bool IsAutostartSupported => _autostart.IsSupported;

    /// <summary>
    ///     开机自启。它不是纯配置：开关一动就立刻写注册表 / 桌面项 / LaunchAgent，
    ///     写失败就把开关拨回去并在底栏说明（与设置页同一套规矩，绝不把失败当成功落盘）。
    /// </summary>
    public bool Autostart
    {
        get => Basic.Autostart;
        set
        {
            if (value == Basic.Autostart)
                return;

            Basic.Autostart = value;
            if (_autostart.TrySetEnabled(value, out var error))
            {
                ErrorMessage = null;
            }
            else
            {
                Basic.Autostart = !value;
                ErrorMessage = string.IsNullOrWhiteSpace(error)
                    ? CR.Oobe_AutostartFailed
                    : $"{CR.Oobe_AutostartFailed}（{error}）";
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(CompletedStartupText));
        }
    }

    /// <summary>启动时显不显示主窗口。</summary>
    public bool ShowStartupWindow
    {
        get => Basic.ShowStartupWindow;
        set
        {
            if (value == Basic.ShowStartupWindow)
                return;

            Basic.ShowStartupWindow = value;
            OnPropertyChanged();
        }
    }

    // ---------------------------------------------------------------------------------------
    // 步骤
    // ---------------------------------------------------------------------------------------

    public bool IsWelcomeStep => IsStep(OobeStepKind.Welcome);

    public bool IsLegalStep => IsStep(OobeStepKind.Legal);

    public bool IsBoardStep => IsStep(OobeStepKind.Board);

    public bool IsAppearanceStep => IsStep(OobeStepKind.Appearance);

    public bool IsStartupStep => IsStep(OobeStepKind.Startup);

    public bool IsCompleteStep => IsStep(OobeStepKind.Complete);

    /// <summary>只有一步时（协议改版的补确认）不画「第几步」与上一步。</summary>
    public bool HasMultipleSteps => _steps.Count > 1;

    public bool CanGoPrevious => SelectedStep > 0;

    public bool IsLastStep => SelectedStep >= _steps.Count - 1;

    public string NextText => IsLastStep ? CR.Oobe_Finish : CR.Oobe_Next;

    public string StepProgress =>
        string.Format(CultureInfo.CurrentCulture, CR.Oobe_StepProgress, SelectedStep + 1, _steps.Count);

    /// <summary>
    ///     这一步能不能往下走：目前只有「协议」那一步要卡两个勾选框。
    ///     其余步骤都是「选不选都行，选的每一件以后都能改」。
    /// </summary>
    public bool CanGoNext => !IsLegalStep || (AcceptedPrivacyPolicy && AcceptedEula);

    // ---------------------------------------------------------------------------------------
    // 汇总（最后一步那一页）
    // ---------------------------------------------------------------------------------------

    public string CompletedLanguageText => SelectedLanguage.DisplayName;

    public string CompletedBoardText => Board.BoardEnabled ? CR.Oobe_BoardOn : CR.Oobe_BoardOff;

    public string CompletedThemeText => IsCustomThemeColor
        ? $"{SelectedTheme.DisplayName} · {FormatColor(Appearance.ThemeColor)}"
        : SelectedTheme.DisplayName;

    public string CompletedStartupText => Basic.Autostart ? CR.Oobe_AutostartOn : CR.Oobe_AutostartOff;

    public string CompletedLegalText => CR.Oobe_CompletedLegalValue;

    [RelayCommand]
    private void Next()
    {
        if (IsLastStep || !CanGoNext)
            return;

        ErrorMessage = null;
        SelectedStep++;
    }

    [RelayCommand]
    private void Previous()
    {
        if (SelectedStep <= 0)
            return;

        ErrorMessage = null;
        SelectedStep--;
    }

    /// <summary>
    ///     收尾：把压着的草稿写进配置，再让服务记下「引导走完、协议已确认」。
    ///     返回假表示这一步还没完成（窗口不关）。
    /// </summary>
    public bool TryFinish()
    {
        if (!CanGoNext)
            return false;

        FlushThemeColorDraft();
        _oobe.Complete();
        return true;
    }

    /// <summary>把压在防抖里的主题色立刻写进配置（窗口关闭 / 收尾前调用，免得丢掉最后一次选择）。</summary>
    public void FlushThemeColorDraft()
    {
        _themeColorCommitTimer?.Stop();
        _themeColorCommitTimer = null;

        if (_themeColorDraft is { } draft && Appearance.ThemeColor != draft)
        {
            Appearance.ThemeColor = draft;
            ApplyAppearance();
        }
    }

    private static IReadOnlyList<OobeStepKind> BuildSteps(OobeRequirement requirement)
    {
        return requirement == OobeRequirement.LegalOnly
            ?
            [
                // 协议改版：只补确认这一步，不把整段引导再放一遍。
                OobeStepKind.Legal
            ]
            :
            [
                OobeStepKind.Welcome,
                OobeStepKind.Legal,
                OobeStepKind.Board,
                OobeStepKind.Appearance,
                OobeStepKind.Startup,
                OobeStepKind.Complete
            ];
    }

    private bool IsStep(OobeStepKind kind)
    {
        return SelectedStep >= 0 && SelectedStep < _steps.Count && _steps[SelectedStep] == kind;
    }

    partial void OnSelectedStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsWelcomeStep));
        OnPropertyChanged(nameof(IsLegalStep));
        OnPropertyChanged(nameof(IsBoardStep));
        OnPropertyChanged(nameof(IsAppearanceStep));
        OnPropertyChanged(nameof(IsStartupStep));
        OnPropertyChanged(nameof(IsCompleteStep));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(NextText));
        OnPropertyChanged(nameof(StepProgress));
        OnPropertyChanged(nameof(CanGoNext));

        // 汇总页退出再进来时值可能已经变了，进来那一刻重新读一遍。
        OnPropertyChanged(nameof(CompletedLanguageText));
        OnPropertyChanged(nameof(CompletedBoardText));
        OnPropertyChanged(nameof(CompletedThemeText));
        OnPropertyChanged(nameof(CompletedStartupText));
        OnPropertyChanged(nameof(CompletedLegalText));
    }

    partial void OnAcceptedPrivacyPolicyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanGoNext));
    }

    partial void OnAcceptedEulaChanged(bool value)
    {
        OnPropertyChanged(nameof(CanGoNext));
    }

    private void ApplyAppearance()
    {
        // 与设置页一样：配置写完立刻应用一次，引导里就能当场看到深浅色与主题色的变化。
        if (Application.Current is App app)
            app.RefreshAppearanceSettings();
    }

    private void ScheduleThemeColorCommit(Color value)
    {
        _themeColorCommitTimer?.Stop();

        _themeColorCommitTimer = new DispatcherTimer { Interval = ThemeColorCommitDelay };
        _themeColorCommitTimer.Tick += (_, _) =>
        {
            _themeColorCommitTimer?.Stop();
            _themeColorCommitTimer = null;

            if (_themeColorDraft is { } draft && Appearance.ThemeColor != draft)
            {
                Appearance.ThemeColor = draft;
                ApplyAppearance();
                OnPropertyChanged(nameof(CompletedThemeText));
            }
        };
        _themeColorCommitTimer.Start();
    }

    private static string FormatColor(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}

/// <summary>引导里的一个步骤。</summary>
public enum OobeStepKind
{
    Welcome = 0,
    Legal = 1,
    Board = 2,
    Appearance = 3,
    Startup = 4,
    Complete = 5
}
