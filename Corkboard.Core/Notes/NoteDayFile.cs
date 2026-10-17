using CommunityToolkit.Mvvm.ComponentModel;

namespace Corkboard.Core.Notes;

/// <summary>单日作业文件 <c>notes.json</c> 的形状：这一天创建的全部作业。</summary>
public partial class NoteDayFile : ObservableObject
{
    [ObservableProperty] private List<Note> _notes = [];
}
