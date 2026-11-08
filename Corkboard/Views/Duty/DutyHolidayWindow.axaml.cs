using Avalonia.Controls;
using FluentAvalonia.UI.Windowing;
using Corkboard.Core.Abstraction;
using Corkboard.Services.Desktop;
using Corkboard.ViewModels.Duty;

namespace Corkboard.Views.Duty;

/// <summary>
///     「节假日」窗口：数据源标注、年份表格、假期区间、手动新增与影响预览。
///     <para>
///         照 <c>ExamBoardEditorWindow</c> 的写法：VM 从 <see cref="IAppHost" /> 取（transient），
///         图标记 <see cref="AppIcon.LoadWindowIcon" />（要出现在任务栏就必须设），
///         <c>Opened</c> 挂订阅、<c>Closed</c> 解订阅——节假日服务是单例，漏解会越挂越多。
///     </para>
/// </summary>
public partial class DutyHolidayWindow : FAAppWindow
{
    public DutyHolidayWindow()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<DutyHolidayViewModel>();

        ((Window)this).Icon = AppIcon.LoadWindowIcon();

        Opened += (_, _) => ViewModel?.Attach();
        Closed += (_, _) => ViewModel?.Detach();
    }

    private DutyHolidayViewModel? ViewModel => DataContext as DutyHolidayViewModel;
}
