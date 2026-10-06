using CommunityToolkit.Mvvm.ComponentModel;

namespace Corkboard.Core.Models.Board;

/// <summary>一块便签。Board 是骨架里的占位业务模块，字段刻意保持中性，方便后续替换成真实需求。</summary>
public partial class BoardNote : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private string _colorHex = "#FFF5CC";
    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private int _order;
    [ObservableProperty] private DateTimeOffset _createdAt = DateTimeOffset.Now;
    [ObservableProperty] private DateTimeOffset _updatedAt = DateTimeOffset.Now;

    public BoardNote Clone()
    {
        return new BoardNote
        {
            Id = Id,
            Title = Title,
            Content = Content,
            ColorHex = ColorHex,
            IsPinned = IsPinned,
            Order = Order,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }
}
