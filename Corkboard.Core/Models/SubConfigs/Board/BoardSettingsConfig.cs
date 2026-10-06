using CommunityToolkit.Mvvm.ComponentModel;

namespace Corkboard.Core.Models.SubConfigs.Board;

/// <summary>Board 占位业务模块的设置。</summary>
public partial class BoardSettingsConfig : ObservableObject
{
    /// <summary>新建便签的默认颜色（十六进制，形如 #FFFFFF）。</summary>
    [ObservableProperty] private string _defaultNoteColor = "#FFF5CC";

    /// <summary>是否把置顶便签排在前面。</summary>
    [ObservableProperty] private bool _pinNotesOnTop = true;
}
