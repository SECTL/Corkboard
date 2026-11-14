using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Corkboard.Core.Models.SubConfigs.Exam;

/// <summary>
///     考试看板主题的用户覆盖项：13 个 token 各一个可空颜色，<c>null</c> = 沿用底（跟随外观或预设）。
///     <para>
///         它是要落盘的配置对象，所以放在 <c>Models/SubConfigs/Exam/</c>，不要塞进
///         <c>Core/Exam/Theme/</c> 的纯逻辑目录。JSON 键由 <c>SnakeCaseLower</c> 推导，
///         颜色字节序与仓库其它配置一致（磁盘 <c>#RRGGBBAA</c>）。
///     </para>
/// </summary>
public partial class ExamBoardThemeOverrides : ObservableObject
{
    [ObservableProperty] private Color? _surface;
    [ObservableProperty] private Color? _textPrimary;
    [ObservableProperty] private Color? _textSecondary;
    [ObservableProperty] private Color? _textTertiary;
    [ObservableProperty] private Color? _subtitle;
    [ObservableProperty] private Color? _divider;
    [ObservableProperty] private Color? _upcoming;
    [ObservableProperty] private Color? _inProgress;
    [ObservableProperty] private Color? _urgent;
    [ObservableProperty] private Color? _highlight;
    [ObservableProperty] private Color? _exitTrack;
    [ObservableProperty] private Color? _exitRingStart;
    [ObservableProperty] private Color? _exitRingEnd;

    /// <summary>清空全部覆盖，回到「只用底」的状态。</summary>
    public void Clear()
    {
        Surface = null;
        TextPrimary = null;
        TextSecondary = null;
        TextTertiary = null;
        Subtitle = null;
        Divider = null;
        Upcoming = null;
        InProgress = null;
        Urgent = null;
        Highlight = null;
        ExitTrack = null;
        ExitRingStart = null;
        ExitRingEnd = null;
    }

    public bool IsEmpty =>
        Surface is null && TextPrimary is null && TextSecondary is null && TextTertiary is null &&
        Subtitle is null && Divider is null && Upcoming is null && InProgress is null && Urgent is null &&
        Highlight is null && ExitTrack is null && ExitRingStart is null && ExitRingEnd is null;
}