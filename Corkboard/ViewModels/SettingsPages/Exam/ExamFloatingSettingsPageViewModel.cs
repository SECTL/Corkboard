using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Board;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Exam;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Services.Config;
using Corkboard.Helpers;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Exam;

/// <summary>
///     「最小化与浮窗」设置页：考试情况下把左下角的退出按钮改成最小化（单击 / 长按可选），
///     以及最小化之后那颗浮窗按钮长什么样——文字、大小、停在屏幕哪儿。
///     <para>
///         这几项都是**跨看板**的偏好，所以留在 <c>settings.json</c> 的 <c>exam_board</c> 段里。
///     </para>
///     <para>
///         文本与数值都绑草稿，失焦 / 回车 / 离开页面才提交——<see cref="MainConfigHandler" />
///         每次属性变化都落一次盘，双向绑等于每敲一个字符写一遍 <c>settings.json</c>。
///         开关与下拉是离散动作，直接双向绑配置。
///     </para>
/// </summary>
public partial class ExamFloatingSettingsPageViewModel : ViewModelBase
{
    private readonly MainConfigHandler _configHandler;
    private bool _isAttached;

    public ExamFloatingSettingsPageViewModel(MainConfigHandler configHandler) : base(configHandler)
    {
        _configHandler = configHandler;

        MinimizeModeOptions =
        [
            new(ExamBoardMinimizeMode.InProgress, CR.Settings_Exam_Floating_MinimizeMode_InProgress),
            new(ExamBoardMinimizeMode.InProgressOrUpcoming,
                CR.Settings_Exam_Floating_MinimizeMode_InProgressOrUpcoming),
            new(ExamBoardMinimizeMode.Always, CR.Settings_Exam_Floating_MinimizeMode_Always),
            new(ExamBoardMinimizeMode.Off, CR.Settings_Exam_Floating_MinimizeMode_Off)
        ];

        GestureOptions =
        [
            new(ExamBoardMinimizeGesture.Click, CR.Settings_Exam_Floating_Gesture_Click),
            new(ExamBoardMinimizeGesture.Hold, CR.Settings_Exam_Floating_Gesture_Hold)
        ];

        AnchorOptions = new ObservableCollection<ExamFloatingAnchorOption>(
            Enum.GetValues<DockAnchor>().Select(anchor => new ExamFloatingAnchorOption(anchor, SelectAnchor)));

        ResetDrafts();
    }

    public ExamBoardSettingsConfig Settings => Config.ExamBoard;

    /// <summary>浮窗文字留空时显示的那句默认文案（输入框的提示、以及实际画在按钮上的字）。</summary>
    public string DefaultFloatingText => CR.Settings_Exam_Floating_DefaultText;

    /// <summary>浮窗按钮文字的草稿。</summary>
    [ObservableProperty]
    private string _textDraft = string.Empty;

    /// <summary>浮窗按钮缩放的草稿（百分比）。</summary>
    [ObservableProperty]
    private decimal? _scaleDraft;

    /// <summary>取色器停手多久才把颜色写进配置：拖光谱时每帧都在变，写盘要攒一攒。</summary>
    private static readonly TimeSpan ColorCommitDelay = TimeSpan.FromMilliseconds(400);

    private Color? _textColorDraft;
    private readonly DebounceTimer _textColorCommit = new(ColorCommitDelay);

    /// <summary>
    ///     浮窗文字颜色的草稿。
    ///     <para>
    ///         <b>不直接双向绑配置</b>：<c>ColorPicker</c> 在光谱里拖一下会连发几十次 <c>Color</c> 变化，
    ///         而 <c>ConfigHandlerBase</c> 每次属性变化都落一次盘（没有防抖）。
    ///     </para>
    /// </summary>
    public Color TextColorDraft
    {
        get => _textColorDraft ?? Settings.FloatingTextColor;
        set
        {
            if (_textColorDraft == value || Settings.FloatingTextColor == value)
                return;

            _textColorDraft = value;
            OnPropertyChanged();
            _textColorCommit.Schedule(() => Settings.FloatingTextColor = value);
        }
    }

    /// <summary>当前用的图标：自定义那张只显示文件名，没选时是一句「正在用软件默认图标」。</summary>
    public string IconPathText => string.IsNullOrWhiteSpace(Settings.FloatingIconPath)
        ? CR.Settings_Exam_Floating_IconDefault
        : Path.GetFileName(Settings.FloatingIconPath);

    /// <summary>有没有选过自定义图标（决定「用默认图标」那颗按钮能不能点）。</summary>
    public bool HasCustomIcon => !string.IsNullOrWhiteSpace(Settings.FloatingIconPath);

    /// <summary>「什么时候能最小化」下拉项（默认第一项：只在考试进行中）。</summary>
    public IReadOnlyList<ExamMinimizeModeOption> MinimizeModeOptions { get; }

    public ExamMinimizeModeOption SelectedMinimizeModeOption
    {
        get => MinimizeModeOptions.FirstOrDefault(option => option.Mode == Settings.MinimizeMode)
               ?? MinimizeModeOptions[0];
        set
        {
            if (value is null || Settings.MinimizeMode == value.Mode)
                return;

            Settings.MinimizeMode = value.Mode;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMinimizeEnabled));
        }
    }

    /// <summary>手势那一行只在「允许最小化」时才有意义（`Off` 档等于从来不最小化）。</summary>
    public bool IsMinimizeEnabled => Settings.MinimizeMode != ExamBoardMinimizeMode.Off;

    /// <summary>最小化手势下拉项。</summary>
    public IReadOnlyList<ExamMinimizeGestureOption> GestureOptions { get; }

    public ExamMinimizeGestureOption SelectedGestureOption
    {
        get => GestureOptions.FirstOrDefault(option => option.Gesture == Settings.MinimizeGesture) ?? GestureOptions[0];
        set
        {
            if (value is null || Settings.MinimizeGesture == value.Gesture)
                return;

            Settings.MinimizeGesture = value.Gesture;
            OnPropertyChanged();
        }
    }

    /// <summary>浮窗停屏幕哪一格（九宫格圆点）。</summary>
    public ObservableCollection<ExamFloatingAnchorOption> AnchorOptions { get; }

    /// <summary>九宫格那颗下拉按钮上显示的位置名。</summary>
    public string AnchorText => DockAnchors.Normalize(Settings.FloatingAnchor) switch
    {
        DockAnchor.TopLeft => CR.Settings_Board_Dock_Anchor_TopLeft,
        DockAnchor.TopCenter => CR.Settings_Board_Dock_Anchor_TopCenter,
        DockAnchor.TopRight => CR.Settings_Board_Dock_Anchor_TopRight,
        DockAnchor.MiddleLeft => CR.Settings_Board_Dock_Anchor_MiddleLeft,
        DockAnchor.Center => CR.Settings_Board_Dock_Anchor_Center,
        DockAnchor.MiddleRight => CR.Settings_Board_Dock_Anchor_MiddleRight,
        DockAnchor.BottomLeft => CR.Settings_Board_Dock_Anchor_BottomLeft,
        DockAnchor.BottomRight => CR.Settings_Board_Dock_Anchor_BottomRight,
        _ => CR.Settings_Board_Dock_Anchor_BottomCenter
    };

    /// <summary>挂上配置变化订阅（页面进入可视树时调）。重复调用不会重复挂。</summary>
    public void Attach()
    {
        if (_isAttached)
            return;

        _isAttached = true;
        Settings.PropertyChanged += OnSettingsChanged;
        ResetDrafts();
    }

    /// <summary>断开订阅（页面离开可视树时调）。</summary>
    public void Detach()
    {
        if (!_isAttached)
            return;

        _isAttached = false;
        Settings.PropertyChanged -= OnSettingsChanged;
    }

    /// <summary>把还压在草稿里的数值立刻写进配置（失焦 / 回车 / 离开页面时调）。</summary>
    public void CommitAll()
    {
        CommitText();
        CommitScale();
        FlushColorDrafts();
    }

    /// <summary>选中九宫格里的某一格：立刻写配置（下拉是离散动作，不用草稿）。</summary>
    public void SelectAnchor(DockAnchor anchor)
    {
        var normalized = DockAnchors.Normalize(anchor);
        if (Settings.FloatingAnchor != normalized)
            Settings.FloatingAnchor = normalized;

        RefreshAnchorSelection();
    }

    /// <summary>把用户挑的那张图片记成图标（路径由文件选择器给）。</summary>
    public void SetFloatingIconPath(string? path)
    {
        var normalized = path?.Trim() ?? string.Empty;
        if (Settings.FloatingIconPath != normalized)
            Settings.FloatingIconPath = normalized;

        RefreshIconState();
    }

    /// <summary>改回软件自己的图标。</summary>
    public void ClearFloatingIcon() => SetFloatingIconPath(string.Empty);

    /// <summary>把这一页的配置项一次改回出厂设置。</summary>
    public void ResetToDefault()
    {
        Settings.MinimizeMode = ExamBoardMinimizeMode.InProgress;
        Settings.MinimizeGesture = ExamBoardMinimizeGesture.Click;
        Settings.FloatingButtonText = string.Empty;
        Settings.FloatingScalePercent = ExamBoardFloatingStyle.DefaultScalePercent;
        Settings.FloatingAnchor = DockAnchor.BottomCenter;
        Settings.FloatingTextColor = Colors.White;
        Settings.ShowFloatingIcon = true;
        Settings.FloatingIconPath = string.Empty;
        Settings.FloatingIconOnly = false;

        ResetDrafts();
        _configHandler.Save();
    }

    private void FlushColorDrafts()
    {
        _textColorCommit.Cancel();
        if (_textColorDraft is { } textColor && Settings.FloatingTextColor != textColor)
            Settings.FloatingTextColor = textColor;
    }

    private void RefreshIconState()
    {
        OnPropertyChanged(nameof(IconPathText));
        OnPropertyChanged(nameof(HasCustomIcon));
    }

    private void CommitText()
    {
        var value = ExamBoardFloatingStyle.NormalizeText(TextDraft);
        if (Settings.FloatingButtonText != value)
            Settings.FloatingButtonText = value;

        if (TextDraft != value)
            TextDraft = value;
    }

    private void CommitScale()
    {
        var value = ScaleDraft is { } draft
            ? ExamBoardFloatingStyle.ClampScalePercent((double)draft)
            : ExamBoardFloatingStyle.ClampScalePercent(Settings.FloatingScalePercent);

        if (Math.Abs(Settings.FloatingScalePercent - value) > 0.001)
            Settings.FloatingScalePercent = value;

        var normalized = (decimal)value;
        if (ScaleDraft != normalized)
            ScaleDraft = normalized;
    }

    /// <summary>把草稿整体同步成配置里的值（首次进页面、云端恢复设置之后）。</summary>
    private void ResetDrafts()
    {
        TextDraft = ExamBoardFloatingStyle.NormalizeText(Settings.FloatingButtonText);
        ScaleDraft = (decimal)ExamBoardFloatingStyle.ClampScalePercent(Settings.FloatingScalePercent);

        ResetColorDrafts();
        RefreshIconState();

        OnPropertyChanged(nameof(SelectedGestureOption));
        RefreshAnchorSelection();
    }

    /// <summary>取色器的草稿回到配置里的颜色。</summary>
    private void ResetColorDrafts()
    {
        _textColorDraft = Settings.FloatingTextColor;
        OnPropertyChanged(nameof(TextColorDraft));
    }

    /// <summary>九宫格里那颗圆点的高亮：跟着配置里的位置走。</summary>
    private void RefreshAnchorSelection()
    {
        var current = DockAnchors.Normalize(Settings.FloatingAnchor);
        foreach (var option in AnchorOptions)
            option.IsSelected = option.Anchor == current;

        OnPropertyChanged(nameof(AnchorText));
    }

    /// <summary>配置被外部改了（云端恢复设置）：把草稿重新对齐，免得界面停在旧值上。</summary>
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case null:
                ResetDrafts();
                break;
            case nameof(ExamBoardSettingsConfig.FloatingButtonText):
                TextDraft = ExamBoardFloatingStyle.NormalizeText(Settings.FloatingButtonText);
                break;
            case nameof(ExamBoardSettingsConfig.FloatingScalePercent):
                ScaleDraft = (decimal)ExamBoardFloatingStyle.ClampScalePercent(Settings.FloatingScalePercent);
                break;
            case nameof(ExamBoardSettingsConfig.FloatingTextColor):
                _textColorDraft = Settings.FloatingTextColor;
                OnPropertyChanged(nameof(TextColorDraft));
                break;
            case nameof(ExamBoardSettingsConfig.FloatingIconPath):
                RefreshIconState();
                break;
            case nameof(ExamBoardSettingsConfig.MinimizeMode):
                OnPropertyChanged(nameof(SelectedMinimizeModeOption));
                OnPropertyChanged(nameof(IsMinimizeEnabled));
                break;
            case nameof(ExamBoardSettingsConfig.MinimizeGesture):
                OnPropertyChanged(nameof(SelectedGestureOption));
                break;
            case nameof(ExamBoardSettingsConfig.FloatingAnchor):
                RefreshAnchorSelection();
                break;
        }
    }
}

/// <summary>「什么时候能最小化」下拉项。</summary>
public sealed record ExamMinimizeModeOption(ExamBoardMinimizeMode Mode, string DisplayName);

/// <summary>最小化手势下拉项。</summary>
public sealed record ExamMinimizeGestureOption(ExamBoardMinimizeGesture Gesture, string DisplayName);

/// <summary>浮窗位置九宫格里的一颗圆点（与作业板「收纳」页那颗同一个形状）。</summary>
public partial class ExamFloatingAnchorOption(DockAnchor anchor, Action<DockAnchor> select) : ObservableObject
{
    public DockAnchor Anchor { get; } = anchor;

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Select() => select(Anchor);
}
