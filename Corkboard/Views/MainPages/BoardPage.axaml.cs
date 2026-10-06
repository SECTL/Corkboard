using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.Core.Models.Board;
using Corkboard.ViewModels.MainPages;

namespace Corkboard.Views.MainPages;

/// <summary>
///     Board 占位页。<see cref="PageInfo" /> 的值同时是导航项 Id 与键控 DI 的 key，
///     改 Id 必须同步改 <c>AppConsts.DefaultMainPageId</c>。
/// </summary>
[PageInfo("main.board", FluentIcons.BoardFilled)]
public partial class BoardPage : UserControl
{
    public BoardPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<BoardPageViewModel>();
    }

    private BoardPageViewModel? ViewModel => DataContext as BoardPageViewModel;

    private void OnAddNoteClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.AddNoteCommand.Execute(null);
    }

    private void OnDeleteNoteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: BoardNote note })
            ViewModel?.DeleteNoteCommand.Execute(note);
    }

    private void OnTogglePinClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: BoardNote note })
            ViewModel?.TogglePinCommand.Execute(note);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
