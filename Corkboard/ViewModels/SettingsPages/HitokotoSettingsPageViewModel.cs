using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Models.SubConfigs.Hitokoto;
using Corkboard.Core.Services.Config;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages;

public partial class HitokotoSettingsPageViewModel : ViewModelBase
{
    public HitokotoSettingsPageViewModel(MainConfigHandler configHandler) : base(configHandler)
    {
        _refreshMinutesDraft = Math.Clamp(Hitokoto.RefreshMinutes, 1, 1440);
        _blockedWordsDraft = Hitokoto.BlockedWords;
        CategoryOptions =
        [
            CreateCategory('a', CR.Settings_Hitokoto_Category_Animation),
            CreateCategory('b', CR.Settings_Hitokoto_Category_Comics),
            CreateCategory('c', CR.Settings_Hitokoto_Category_Games),
            CreateCategory('d', CR.Settings_Hitokoto_Category_Literature),
            CreateCategory('e', CR.Settings_Hitokoto_Category_Original),
            CreateCategory('f', CR.Settings_Hitokoto_Category_Internet),
            CreateCategory('g', CR.Settings_Hitokoto_Category_Other),
            CreateCategory('h', CR.Settings_Hitokoto_Category_FilmAndTV),
            CreateCategory('i', CR.Settings_Hitokoto_Category_Poetry),
            CreateCategory('j', CR.Settings_Hitokoto_Category_NeteaseMusic),
            CreateCategory('k', CR.Settings_Hitokoto_Category_Philosophy),
            CreateCategory('l', CR.Settings_Hitokoto_Category_Humor)
        ];
    }

    public HitokotoSettingsConfig Hitokoto => Config.Hitokoto;

    public IReadOnlyList<HitokotoCategoryOption> CategoryOptions { get; }

    // 输入控件只改草稿，提交时才写配置，避免每敲一个字符就落盘。
    [ObservableProperty] private decimal? _refreshMinutesDraft;
    [ObservableProperty] private string? _blockedWordsDraft;

    public void CommitRefreshMinutes()
    {
        var minutes = (int)Math.Clamp(RefreshMinutesDraft ?? Hitokoto.RefreshMinutes, 1m, 1440m);
        Hitokoto.RefreshMinutes = minutes;
        RefreshMinutesDraft = minutes;
    }

    [RelayCommand]
    public void SaveBlockedWords()
    {
        Hitokoto.BlockedWords = BlockedWordsDraft ?? string.Empty;
    }

    public void SaveDrafts()
    {
        CommitRefreshMinutes();
        SaveBlockedWords();
    }

    private HitokotoCategoryOption CreateCategory(char code, string displayName) =>
        new(code, displayName, Hitokoto.Categories.Contains(code), CommitCategories);

    private void CommitCategories()
    {
        // 不勾选任何类别就存空串，表示全部；勾选时按 a–l 顺序拼接。
        Hitokoto.Categories = string.Concat(CategoryOptions.Where(option => option.IsSelected)
            .Select(option => option.Code));
    }
}

public sealed class HitokotoCategoryOption(
    char code,
    string displayName,
    bool isSelected,
    Action selectionChanged) : ObservableObject
{
    private bool _isSelected = isSelected;

    public char Code { get; } = code;
    public string DisplayName { get; } = displayName;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
                selectionChanged();
        }
    }
}
