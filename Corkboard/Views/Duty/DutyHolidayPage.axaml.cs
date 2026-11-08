using Avalonia.Controls;
using Avalonia.Interactivity;
using Corkboard.ViewModels.Duty;

namespace Corkboard.Views.Duty;

/// <summary>
///     「节假日」页的内容：所有操作都转给 <see cref="DutyHolidayViewModel" />，这里不写业务判断。
///     <para>
///         窗口 <see cref="DutyHolidayWindow" /> 在构造期把 DataContext 设成 VM，页面直接继承；
///         行按钮用 code-behind 转发而不是命令，是因为表格行是普通 POCO、不带命令。
///     </para>
/// </summary>
public partial class DutyHolidayPage : UserControl
{
    public DutyHolidayPage()
    {
        InitializeComponent();
    }

    private DutyHolidayViewModel? ViewModel => DataContext as DutyHolidayViewModel;

    private void OnMarkWorkClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is HolidayRowItem row)
            ViewModel?.MarkWork(row);
    }

    private void OnMarkOffClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is HolidayRowItem row)
            ViewModel?.MarkOff(row);
    }

    private void OnRestoreClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is HolidayRowItem row)
            ViewModel?.Restore(row);
    }

    private void OnRemoveVacationClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is HolidayVacationRowItem item)
            ViewModel?.RemoveVacation(item);
    }

    private void OnAddVacationClick(object? sender, RoutedEventArgs e) => ViewModel?.AddVacation();

    private void OnAddManualClick(object? sender, RoutedEventArgs e) => ViewModel?.AddManual();

    private void OnOpenPaperClick(object? sender, RoutedEventArgs e) => ViewModel?.OpenPaper();

    private void OnOpenSourceClick(object? sender, RoutedEventArgs e) => ViewModel?.OpenSource();

    private void OnOpenLicenseClick(object? sender, RoutedEventArgs e) => ViewModel?.OpenLicense();
}
