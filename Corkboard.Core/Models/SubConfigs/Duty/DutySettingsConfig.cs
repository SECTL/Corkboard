using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.DutyRoster;

namespace Corkboard.Core.Models.SubConfigs.Duty;

/// <summary>
///     值日表的**界面偏好**（存 <c>settings.json</c>）。名册、小组、职务、排班规则都属于业务数据，
///     在 <c>data/duty/duty.json</c> 里，不要往这里塞。
/// </summary>
public partial class DutySettingsConfig : ObservableObject
{
    /// <summary>是否启用值日表（关掉后入口隐藏，数据保留）。</summary>
    [ObservableProperty] private bool _enabled = true;

    /// <summary>周视图的起始星期（0 = 周日 … 6 = 周六，默认周一）。</summary>
    [ObservableProperty] private int _weekStart = (int)DayOfWeek.Monday;

    /// <summary>打开值日表窗口时默认显示的视图。</summary>
    [ObservableProperty] private DutyViewMode _defaultView = DutyViewMode.Week;

    /// <summary>周/月视图里高亮今天。</summary>
    [ObservableProperty] private bool _highlightToday = true;

    /// <summary>显示图例（今天/节假日/补班/兑换/加罚/固定人员徽标）。</summary>
    [ObservableProperty] private bool _showLegend = true;

    /// <summary>导出图片的缩放倍数（1–4）。</summary>
    [ObservableProperty] private int _exportScale = 2;

    /// <summary>导出时把徽标一起画上。</summary>
    [ObservableProperty] private bool _exportWithBadges = true;

    /// <summary>读盘后夹取越界值。</summary>
    public void Normalize()
    {
        if (WeekStart is < 0 or > 6)
            WeekStart = (int)DayOfWeek.Monday;

        if (DefaultView is not (DutyViewMode.Week or DutyViewMode.Month))
            DefaultView = DutyViewMode.Week;

        ExportScale = Math.Clamp(ExportScale, 1, 4);
    }
}
