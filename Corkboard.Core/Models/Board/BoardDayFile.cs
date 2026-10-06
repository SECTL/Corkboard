using CommunityToolkit.Mvvm.ComponentModel;

namespace Corkboard.Core.Models.Board;

/// <summary>单日作业文件 <c>notes.json</c> 的形状：这一天创建的全部作业。</summary>
public partial class BoardDayFile : ObservableObject
{
    [ObservableProperty] private List<BoardNote> _notes = [];
}
