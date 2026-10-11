using CommunityToolkit.Mvvm.ComponentModel;

namespace Corkboard.Core.Models.SubConfigs.Hitokoto;

/// <summary>一言的刷新、内容筛选与显示设置。</summary>
public partial class HitokotoSettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private int _refreshMinutes = 5;

    /// <summary>类别代码 a–l，留空表示全部类别。</summary>
    [ObservableProperty] private string _categories = string.Empty;

    /// <summary>屏蔽词，一行一个，同时筛选正文、来源与作者。</summary>
    [ObservableProperty] private string _blockedWords = string.Empty;

    [ObservableProperty] private bool _showSource = true;
    [ObservableProperty] private bool _showAuthor = true;
}
