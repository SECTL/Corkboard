using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Abstraction.Services;
using Corkboard.Core.Models.Board;

namespace Corkboard.Core.Services.Board;

/// <summary>
///     Board 占位领域的实现：内存里维护 <see cref="ObservableCollection{T}" />，
///     每次变更后通过 <see cref="ConfigServiceBase" /> 整份落盘。
///     <para>
///         这样写是为了把「配置 + 日志 + DI」这条管道跑通：服务本身没有业务难度，
///         但它证明了新模块不需要自己发明持久化方式。
///     </para>
/// </summary>
public class BoardService : IBoardService
{
    private readonly ConfigServiceBase _configService;
    private readonly ILogger<BoardService> _logger;
    private readonly ObservableCollection<BoardNote> _notes = [];
    private BoardConfig _config;

    public BoardService(ConfigServiceBase configService, ILogger<BoardService> logger)
    {
        _configService = configService;
        _logger = logger;
        _config = _configService.LoadConfig(new BoardConfig());
        _notes = new ObservableCollection<BoardNote>(_config.Notes);
        Notes = new ReadOnlyObservableCollection<BoardNote>(_notes);
    }

    public ReadOnlyObservableCollection<BoardNote> Notes { get; }

    public event EventHandler? Changed;

    public BoardNote Add(string title, string content, string? colorHex = null)
    {
        var note = new BoardNote
        {
            Title = title,
            Content = content,
            ColorHex = string.IsNullOrWhiteSpace(colorHex) ? "#FFF5CC" : colorHex,
            Order = _notes.Count == 0 ? 0 : _notes.Max(x => x.Order) + 1
        };

        _notes.Add(note);
        Persist();
        return note;
    }

    public bool Update(BoardNote note)
    {
        ArgumentNullException.ThrowIfNull(note);

        var index = IndexOf(note.Id);
        if (index < 0)
            return false;

        note.UpdatedAt = DateTimeOffset.Now;
        _notes[index] = note;
        Persist();
        return true;
    }

    public bool Remove(Guid id)
    {
        var index = IndexOf(id);
        if (index < 0)
            return false;

        _notes.RemoveAt(index);
        Persist();
        return true;
    }

    public bool TogglePin(Guid id)
    {
        var index = IndexOf(id);
        if (index < 0)
            return false;

        _notes[index].IsPinned = !_notes[index].IsPinned;
        _notes[index].UpdatedAt = DateTimeOffset.Now;
        Persist();
        return true;
    }

    public void Reload()
    {
        _config = _configService.LoadConfig(new BoardConfig());
        _notes.Clear();
        foreach (var note in _config.Notes)
            _notes.Add(note);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Save()
    {
        Persist();
    }

    private void Persist()
    {
        _config.Notes = [.. _notes];
        _configService.SaveConfig(_config);
        _logger.LogInformation("Board notes persisted: {Count}", _notes.Count);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private int IndexOf(Guid id)
    {
        for (var i = 0; i < _notes.Count; i++)
            if (_notes[i].Id == id)
                return i;

        return -1;
    }
}
