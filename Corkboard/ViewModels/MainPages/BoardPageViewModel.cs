using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Abstraction.Services;
using Corkboard.Core.Models.Board;
using Corkboard.Core.Services.Config;

namespace Corkboard.ViewModels.MainPages;

/// <summary>
///     Board 占位页的 ViewModel。它只依赖 <see cref="IBoardService" /> 与主配置，
///     真实业务替换时这一层可以整页替换，不需要动框架。
/// </summary>
public partial class BoardPageViewModel : ViewModelBase
{
    private readonly IBoardService _boardService;

    public BoardPageViewModel(MainConfigHandler configHandler, IBoardService boardService)
        : base(configHandler)
    {
        _boardService = boardService;
        Notes = _boardService.Notes;
        ((INotifyCollectionChanged)Notes).CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNotes));
    }

    public ReadOnlyObservableCollection<BoardNote> Notes { get; }

    [ObservableProperty] private string _newTitle = string.Empty;
    [ObservableProperty] private string _newContent = string.Empty;

    public bool HasNotes => Notes.Count > 0;

    [RelayCommand]
    private void AddNote()
    {
        if (string.IsNullOrWhiteSpace(NewTitle) && string.IsNullOrWhiteSpace(NewContent))
            return;

        _boardService.Add(NewTitle.Trim(), NewContent.Trim(), Config.BoardSettings.DefaultNoteColor);
        NewTitle = string.Empty;
        NewContent = string.Empty;
    }

    [RelayCommand]
    private void DeleteNote(BoardNote? note)
    {
        if (note is null)
            return;

        _boardService.Remove(note.Id);
    }

    [RelayCommand]
    private void TogglePin(BoardNote? note)
    {
        if (note is null)
            return;

        _boardService.TogglePin(note.Id);
    }
}
