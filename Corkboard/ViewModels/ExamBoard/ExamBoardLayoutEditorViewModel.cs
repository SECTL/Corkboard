using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Exam;
using Corkboard.Core.Exam.Layout;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Services.Config;
using Corkboard.Services.Exam;
using Corkboard.ViewModels.SettingsPages.Exam;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.ExamBoard;

/// <summary>布局编辑窗里的一个状态选项。</summary>
public sealed record ExamBoardStatusOption(ExamStatus Value, string Name);

/// <summary>布局编辑窗里的一个组件选项。</summary>
public sealed record ExamBoardComponentOption(ExamBoardLayoutComponentId Value, string Name);

/// <summary>布局编辑窗里的一个对齐选项。</summary>
public sealed record ExamBoardAlignmentOption(ExamBoardContentAlignment Value, string Name);

/// <summary>
///     独立布局编辑窗的 ViewModel：继承放映窗的 VM，但内容换成示例考试、时间换成假时钟，
///     布局换成一份内存草稿。<see cref="ExamBoardViewModel.LayoutConfig" /> 被草稿覆盖，
///     <see cref="ExamBoardViewModel.ForceCustomLayout" /> 为真，所以整窗始终显示自定义面板。
///     <para>
///         编辑期间**不写配置**：所有改动只落在 <see cref="_draft" /> 上；点「完成」时把整份
///         <see cref="ExamBoardLayoutConfig" /> 一次性赋给配置（ConfigHandlerBase 只落一次盘），
///         点「取消」直接把草稿丢掉。
///     </para>
/// </summary>
public partial class ExamBoardLayoutEditorViewModel : ExamBoardViewModel, IExamBoardLayoutEditingHost
{
    private readonly ExamBoardPreviewTimeSource _previewTime;
    private readonly ExamBoardLayoutConfig _draft;
    private ExamStatus _editStatus = ExamStatus.NotStarted;
    private bool _saved;
    private IReadOnlyList<ExamFontSizeItem> _fontItems = [];

    public ExamBoardLayoutEditorViewModel(
        MainConfigHandler configHandler,
        ExamBoardThemeService theme,
        ExamBoardLayoutService layoutService)
        : base(configHandler, new ExamBoardSampleContentSource(), new ExamBoardPreviewTimeSource(), theme, layoutService)
    {
        _previewTime = (ExamBoardPreviewTimeSource)TimeSource;
        _draft = Config.ExamBoardLayout.Clone();

        foreach (var status in Statuses)
        {
            Subscribe(status);
        }

        _selectedStatus = StatusOptions[0];
        _selectedComponent = ComponentOptions[0];
        _selectedAlignment = AlignmentOptions.First(option => option.Value == SelectedLayout.ContentAlignment);

        RefreshNow();
        _editStatus = CurrentStatus;
        _selectedStatus = StatusOptions.First(option => option.Value == _editStatus);
        RefreshFontItems();
    }

    /// <summary>编辑中的草稿；放映窗的布局服务不碰它。</summary>
    public override ExamBoardLayoutConfig LayoutConfig => _draft;

    /// <summary>即使当前状态没自定义过也用自定义面板渲染，否则拖动时看不到效果。</summary>
    protected override bool ForceCustomLayout => true;

    /// <summary>关闭编辑窗（完成或取消由调用方读 <see cref="IsSaved" /> 判断）。</summary>
    public event EventHandler? CloseRequested;

    /// <summary>点过「完成」时为真；「取消」或直接关窗时为假。</summary>
    public bool IsSaved => _saved;

    public ObservableCollection<ExamBoardStatusOption> StatusOptions { get; } = new()
    {
        new(ExamStatus.NotStarted, CR.Settings_Exam_Layout_Status_NotStarted),
        new(ExamStatus.Upcoming, CR.Settings_Exam_Layout_Status_Upcoming),
        new(ExamStatus.InProgress, CR.Settings_Exam_Layout_Status_InProgress),
        new(ExamStatus.Finished, CR.Settings_Exam_Layout_Status_Finished)
    };

    public ObservableCollection<ExamBoardComponentOption> ComponentOptions { get; } = new()
    {
        new(ExamBoardLayoutComponentId.Title, CR.Settings_Exam_Layout_Component_Title),
        new(ExamBoardLayoutComponentId.Clock, CR.Settings_Exam_Layout_Component_Clock),
        new(ExamBoardLayoutComponentId.ExamInfo, CR.Settings_Exam_Layout_Component_ExamInfo),
        new(ExamBoardLayoutComponentId.ExamTable, CR.Settings_Exam_Layout_Component_ExamTable),
        new(ExamBoardLayoutComponentId.ExitCorner, CR.Settings_Exam_Layout_Component_ExitCorner)
    };

    public ObservableCollection<ExamBoardAlignmentOption> AlignmentOptions { get; } = new()
    {
        new(ExamBoardContentAlignment.TopLeft, CR.Settings_Exam_Layout_Alignment_TopLeft),
        new(ExamBoardContentAlignment.TopCenter, CR.Settings_Exam_Layout_Alignment_Top),
        new(ExamBoardContentAlignment.TopRight, CR.Settings_Exam_Layout_Alignment_TopRight),
        new(ExamBoardContentAlignment.MiddleLeft, CR.Settings_Exam_Layout_Alignment_Left),
        new(ExamBoardContentAlignment.MiddleCenter, CR.Settings_Exam_Layout_Alignment_MiddleCenter),
        new(ExamBoardContentAlignment.MiddleRight, CR.Settings_Exam_Layout_Alignment_Right),
        new(ExamBoardContentAlignment.BottomLeft, CR.Settings_Exam_Layout_Alignment_BottomLeft),
        new(ExamBoardContentAlignment.BottomCenter, CR.Settings_Exam_Layout_Alignment_Bottom),
        new(ExamBoardContentAlignment.BottomRight, CR.Settings_Exam_Layout_Alignment_BottomRight)
    };

    [ObservableProperty] private ExamBoardStatusOption _selectedStatus;

    [ObservableProperty] private ExamBoardComponentOption _selectedComponent;

    [ObservableProperty] private ExamBoardAlignmentOption _selectedAlignment;

    /// <summary>画布上点中了组件才显示右侧属性面板（用户要求：不要常驻）。</summary>
    [ObservableProperty] private bool _hasSelection;

    /// <summary>点击画布上的组件：选中它并展开属性面板；点空白处收起面板。</summary>
    public void SelectComponent(ExamBoardLayoutComponentId? component)
    {
        if (component is null)
        {
            HasSelection = false;
            return;
        }

        SelectedComponent = ComponentOptions.First(option => option.Value == component.Value);
        HasSelection = true;
    }

    /// <summary>收起属性面板（画布空白处或面板右上角那颗 ×）。</summary>
    [RelayCommand]
    private void ClearSelection() => HasSelection = false;

    /// <summary>当前正在编辑的状态布局。</summary>
    public ExamBoardStatusLayout SelectedStatusLayout => _draft.For(_editStatus);

    /// <summary>右侧属性面板绑的就是这一个组件。</summary>
    public ExamBoardComponentLayout SelectedLayout => SelectedStatusLayout.For(SelectedComponent.Value);

    /// <summary>
    ///     选中组件对应的那几项字号（与「设置 → 看板字号」同一张表，见 <see cref="ExamBoardFontSizeCatalog" />）。
    ///     面板里只列这个组件用到的项，拖动滑块松手才落盘。
    /// </summary>
    public IReadOnlyList<ExamFontSizeItem> FontItems
    {
        get => _fontItems;
        private set
        {
            _fontItems = value;
            OnPropertyChanged();
        }
    }

    /// <summary>把面板里正在拖/改的字号草稿写进配置（松手、点完成时调）。</summary>
    public void CommitFontDrafts()
    {
        foreach (var item in FontItems)
        {
            item.Commit();
        }
    }

    public string PreviewTimeText => _previewTime.NowLocal.ToString("HH:mm", CultureInfo.InvariantCulture);

    public string SelectedStatusName => SelectedStatus?.Name ?? string.Empty;

    /// <summary>供窗口构造时指定初始状态（四档之一）。</summary>
    public void SelectStatus(ExamStatus status) =>
        SelectedStatus = StatusOptions.First(option => option.Value == status);

    partial void OnSelectedStatusChanged(ExamBoardStatusOption value)
    {
        if (value is null)
        {
            return;
        }

        _editStatus = value.Value;
        _previewTime.NowLocal = SampleTime(value.Value);
        RefreshNow();
        _editStatus = value.Value;

        SelectedAlignment = AlignmentOptions.First(option => option.Value == SelectedLayout.ContentAlignment);
        OnPropertyChanged(nameof(SelectedStatusLayout));
        OnPropertyChanged(nameof(SelectedLayout));
        OnPropertyChanged(nameof(PreviewTimeText));
        OnPropertyChanged(nameof(SelectedStatusName));
        OnPropertyChanged(nameof(ActiveLayout));
        OnPropertyChanged(nameof(IsCustomLayout));
    }

    partial void OnSelectedComponentChanged(ExamBoardComponentOption value)
    {
        if (value is null)
        {
            return;
        }

        SelectedAlignment = AlignmentOptions.First(option => option.Value == SelectedLayout.ContentAlignment);
        RefreshFontItems();
        OnPropertyChanged(nameof(SelectedLayout));
    }

    partial void OnSelectedAlignmentChanged(ExamBoardAlignmentOption value)
    {
        if (value is null || SelectedLayout.ContentAlignment == value.Value)
        {
            return;
        }

        SelectedLayout.ContentAlignment = value.Value;
    }

    [RelayCommand]
    private void ResetStatus()
    {
        var fresh = ExamBoardLayoutDefaults.CreateStatusLayout(_editStatus);

        switch (_editStatus)
        {
            case ExamStatus.NotStarted:
                _draft.NotStarted = fresh;
                break;
            case ExamStatus.Upcoming:
                _draft.Upcoming = fresh;
                break;
            case ExamStatus.InProgress:
                _draft.InProgress = fresh;
                break;
            default:
                _draft.Finished = fresh;
                break;
        }

        // 状态布局对象被换掉了，要重新挂订阅。
        DetachStatusLayouts();
        foreach (var status in Statuses)
        {
            Subscribe(status);
        }

        OnPropertyChanged(nameof(SelectedStatusLayout));
        OnPropertyChanged(nameof(SelectedLayout));
        OnPropertyChanged(nameof(ActiveLayout));
        OnPropertyChanged(nameof(IsCustomLayout));
    }

    [RelayCommand]
    private void Finish()
    {
        // 字号不属于布局草稿，但松手后可能还有一个键盘改的草稿没提交，点完成时补一次。
        CommitFontDrafts();

        // 整份配置一次替换：ConfigHandlerBase 只落一次盘，也只会重新 Attach 一次。
        Config.ExamBoardLayout = _draft.Clone();
        _saved = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private static IEnumerable<ExamStatus> Statuses
    {
        get
        {
            yield return ExamStatus.NotStarted;
            yield return ExamStatus.Upcoming;
            yield return ExamStatus.InProgress;
            yield return ExamStatus.Finished;
        }
    }

    /// <summary>把假时钟拨到能算出目标状态的时刻（示例考试固定 2026-06-07）。</summary>
    private static DateTime SampleTime(ExamStatus status) => status switch
    {
        ExamStatus.NotStarted => new DateTime(2026, 6, 7, 6, 0, 0),
        ExamStatus.Upcoming => new DateTime(2026, 6, 7, 7, 50, 0),
        ExamStatus.InProgress => new DateTime(2026, 6, 7, 8, 30, 0),
        _ => new DateTime(2026, 6, 7, 16, 30, 0)
    };

    private void RefreshFontItems()
    {
        var ids = ExamBoardFontSizeCatalog.IdsFor(SelectedComponent.Value).ToHashSet(StringComparer.Ordinal);
        FontItems = ExamBoardFontSizeCatalog.Create(Config.ExamBoard)
            .Where(item => ids.Contains(item.Id))
            .ToArray();
    }

    private void Subscribe(ExamStatus status)
    {
        var layout = _draft.For(status);
        layout.PropertyChanged += OnStatusLayoutPropertyChanged;
        layout.Title.PropertyChanged += OnComponentPropertyChanged;
        layout.Clock.PropertyChanged += OnComponentPropertyChanged;
        layout.ExamInfo.PropertyChanged += OnComponentPropertyChanged;
        layout.ExamTable.PropertyChanged += OnComponentPropertyChanged;
        layout.ExitCorner.PropertyChanged += OnComponentPropertyChanged;
    }

    private void DetachStatusLayouts()
    {
        foreach (var status in Statuses)
        {
            var layout = _draft.For(status);
            layout.PropertyChanged -= OnStatusLayoutPropertyChanged;
            layout.Title.PropertyChanged -= OnComponentPropertyChanged;
            layout.Clock.PropertyChanged -= OnComponentPropertyChanged;
            layout.ExamInfo.PropertyChanged -= OnComponentPropertyChanged;
            layout.ExamTable.PropertyChanged -= OnComponentPropertyChanged;
            layout.ExitCorner.PropertyChanged -= OnComponentPropertyChanged;
        }
    }

    private void OnStatusLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ExamBoardStatusLayout.IsCustomized))
        {
            return;
        }

        OnPropertyChanged(nameof(ActiveLayout));
        OnPropertyChanged(nameof(IsCustomLayout));
    }

    private void OnComponentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ExamBoardComponentLayout component)
        {
            return;
        }

        // 第一次动某个组件，就把它所属的状态标成「已自定义」。
        var owner = Statuses.FirstOrDefault(status =>
            ReferenceEquals(_draft.For(status).Title, component) ||
            ReferenceEquals(_draft.For(status).Clock, component) ||
            ReferenceEquals(_draft.For(status).ExamInfo, component) ||
            ReferenceEquals(_draft.For(status).ExamTable, component) ||
            ReferenceEquals(_draft.For(status).ExitCorner, component));

        var ownerLayout = _draft.For(owner);
        if (!ownerLayout.IsCustomized)
        {
            ownerLayout.IsCustomized = true;
        }

        if (owner == _editStatus)
        {
            OnPropertyChanged(nameof(SelectedLayout));
            OnPropertyChanged(nameof(ActiveLayout));
            OnPropertyChanged(nameof(IsCustomLayout));
        }
    }
}