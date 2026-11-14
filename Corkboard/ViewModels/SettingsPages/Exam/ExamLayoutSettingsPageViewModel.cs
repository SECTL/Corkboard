using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Exam;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Services.Config;
using Corkboard.Services.Exam;
using Corkboard.ViewModels;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Exam;

/// <summary>设置页「布局」里的一行：一档考试状态的布局状态与两个动作。</summary>
public partial class ExamLayoutStatusRow : ObservableObject
{
    private readonly Action<ExamStatus> _edit;
    private readonly Action<ExamStatus> _reset;

    public ExamLayoutStatusRow(ExamStatus status, string name, Action<ExamStatus> edit, Action<ExamStatus> reset)
    {
        Status = status;
        Name = name;
        _edit = edit;
        _reset = reset;
        EditCommand = new RelayCommand(() => _edit(Status));
        ResetCommand = new RelayCommand(() => _reset(Status));
    }

    public ExamStatus Status { get; }

    public string Name { get; }

    [ObservableProperty] private string _stateText = string.Empty;

    [ObservableProperty] private bool _isCustomized;

    public IRelayCommand EditCommand { get; }

    public IRelayCommand ResetCommand { get; }
}

/// <summary>
///     设置 → 考试看板 → 布局：四档状态各自的布局状态（默认 / 已自定义），
///     以及对每一档打开独立的布局编辑窗、恢复默认。
/// </summary>
public partial class ExamLayoutSettingsPageViewModel : ViewModelBase
{
    private readonly ExamBoardLayoutService _layoutService;

    public ExamLayoutSettingsPageViewModel(MainConfigHandler configHandler, ExamBoardLayoutService layoutService)
        : base(configHandler)
    {
        _layoutService = layoutService;
        Rows = new ObservableCollection<ExamLayoutStatusRow>
        {
            new(ExamStatus.NotStarted, CR.Settings_Exam_Layout_Status_NotStarted, Edit, Reset),
            new(ExamStatus.Upcoming, CR.Settings_Exam_Layout_Status_Upcoming, Edit, Reset),
            new(ExamStatus.InProgress, CR.Settings_Exam_Layout_Status_InProgress, Edit, Reset),
            new(ExamStatus.Finished, CR.Settings_Exam_Layout_Status_Finished, Edit, Reset)
        };

        RefreshRows();
    }

    public ObservableCollection<ExamLayoutStatusRow> Rows { get; }

    /// <summary>页面进可视树时挂订阅；transient VM + 单例服务，离开时必须断开。</summary>
    public void Attach()
    {
        _layoutService.Changed += OnLayoutChanged;
        RefreshRows();
    }

    public void Detach() => _layoutService.Changed -= OnLayoutChanged;

    private void Edit(ExamStatus status) => App.ShowExamBoardLayoutEditorWindow(status);

    private void Reset(ExamStatus status)
    {
        Config.ExamBoardLayout.For(status).IsCustomized = false;
        RefreshRows();
    }

    private void OnLayoutChanged(object? sender, EventArgs e) => RefreshRows();

    private void RefreshRows()
    {
        foreach (var row in Rows)
        {
            var layout = Config.ExamBoardLayout.For(row.Status);
            row.IsCustomized = layout.IsCustomized;
            row.StateText = layout.IsCustomized
                ? CR.Settings_Exam_Layout_State_Customized
                : CR.Settings_Exam_Layout_State_Default;
        }
    }
}