using System.Collections.ObjectModel;
using Corkboard.Core.Models.Board;

namespace Corkboard.Core.Abstraction.Services;

/// <summary>Board 占位业务的服务契约。真实业务落地时替换实现即可，UI 只依赖这个接口。</summary>
public interface IBoardService
{
    ReadOnlyObservableCollection<BoardNote> Notes { get; }

    event EventHandler? Changed;

    BoardNote Add(string title, string content, string? colorHex = null);

    bool Update(BoardNote note);

    bool Remove(Guid id);

    bool TogglePin(Guid id);

    void Reload();

    void Save();
}
