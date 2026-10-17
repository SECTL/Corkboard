using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Services.Config;
using Corkboard.ViewModels;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Board;

/// <summary>
///     「快捷标签」设置页：维护布置作业时点一下就能插进内容里的那组标签。
///     <para>
///         标签存在 <c>settings.json</c> 的 <see cref="BoardSettingsConfig.QuickTags" /> 里。
///         配置管道只订阅集合的增删、不订阅集合元素，所以这里每次增删之后都显式
///         <see cref="MainConfigHandler.Save" /> 一次，不指望自动落盘。
///     </para>
///     <para>
///         输入框走草稿属性（<see cref="TagDraft" />），**不**双向绑配置：配置每变一次就写一遍盘。
///     </para>
/// </summary>
public partial class QuickTagsSettingsPageViewModel : ViewModelBase
{
    private readonly MainConfigHandler _configHandler;

    public QuickTagsSettingsPageViewModel(MainConfigHandler configHandler) : base(configHandler)
    {
        _configHandler = configHandler;

        // 老配置里可能留着空白、重复或超量的标签：进页面先按 Core 的规则原地收拾一遍，
        // 界面上看到的就是磁盘上的那一份。
        Settings.NormalizeQuickTags();
        ReloadTags();
    }

    public BoardSettingsConfig Settings => Config.BoardSettings;

    /// <summary>
    ///     输入框里的草稿。**不直接双向绑配置**：配置每次属性变化都会落一次盘，
    ///     而这里要的是「填好、点新增」才写一次。
    /// </summary>
    [ObservableProperty] private string _tagDraft = string.Empty;

    /// <summary>
    ///     界面上的标签行。配置里存的是纯字符串，这里包成行对象只是为了给每行挂一颗删除按钮
    ///     （同时也给以后做重命名留个位置）。
    /// </summary>
    public ObservableCollection<QuickTagItem> Tags { get; } = [];

    /// <summary>一个标签都没有：显示空态文案。</summary>
    public bool IsTagListEmpty => Tags.Count == 0;

    /// <summary>标签数到上限了：新增按钮禁用。</summary>
    public bool IsTagLimitReached => Tags.Count >= BoardSettingsConfig.MaxQuickTags;

    /// <summary>「已加 N 个，最多 M 个」。同时承担「到上限了」的提示。</summary>
    public string CountText => string.Format(
        CR.Settings_Board_QuickTags_CountFormat, Tags.Count, BoardSettingsConfig.MaxQuickTags);

    /// <summary>当前草稿能不能加：既没到上限、也不是空白、也不跟已有的重复（忽略大小写）。</summary>
    public bool CanAddDraft
    {
        get
        {
            if (IsTagLimitReached)
                return false;

            var candidate = TagDraft.Trim();
            return candidate.Length > 0 && !ContainsTag(candidate);
        }
    }

    partial void OnTagDraftChanged(string value)
    {
        OnPropertyChanged(nameof(CanAddDraft));
    }

    /// <summary>把草稿加进标签集合并落盘。重复、空白、到达上限时什么都不做。</summary>
    [RelayCommand]
    private void AddTag()
    {
        var candidate = TagDraft.Trim();
        if (candidate.Length == 0 || IsTagLimitReached || ContainsTag(candidate))
            return;

        Tags.Add(new QuickTagItem(candidate));
        Persist();

        // 加完清空输入框，方便连着录下一个。
        TagDraft = string.Empty;
    }

    /// <summary>删掉某一行。界面用 code-behind 的点击事件把行对象传进来（同色板那套做法）。</summary>
    public void RemoveTag(QuickTagItem? item)
    {
        if (item is null || !Tags.Remove(item))
            return;

        Persist();
    }

    /// <summary>配置里是不是已经有同名标签（忽略大小写，和 Core 的清洗规则一致）。</summary>
    private bool ContainsTag(string name)
        => Tags.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    ///     把界面上的标签写回配置并显式落盘。写回前再按 Core 的规则清洗一次，
    ///     万一有重复 / 超量不会被写进配置。
    /// </summary>
    private void Persist()
    {
        var normalized = BoardSettingsConfig.NormalizeQuickTagList(Tags.Select(item => item.Name));

        Settings.QuickTags.Clear();
        foreach (var tag in normalized)
            Settings.QuickTags.Add(tag);

        // 集合增删其实会触发自动落盘，但元素级的改动不会；这里显式保存一次，
        // 保证「清洗后截断 / 去重」这类不属于增删的变化也一定写到磁盘。
        _configHandler.Save();

        // 清洗可能丢掉了界面上的某个标签（重复、超量），界面跟着配置重建一次。
        ReloadTags();
    }

    /// <summary>从配置重建界面行，并刷新空态、上限与计数这几条绑定。</summary>
    private void ReloadTags()
    {
        Tags.Clear();
        foreach (var tag in Settings.QuickTags)
            Tags.Add(new QuickTagItem(tag));

        OnPropertyChanged(nameof(IsTagListEmpty));
        OnPropertyChanged(nameof(IsTagLimitReached));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(CanAddDraft));
    }
}

/// <summary>快捷标签列表里的一行：目前只有名字，删除按钮由页面按行对象回传。</summary>
public sealed class QuickTagItem(string name)
{
    public string Name { get; } = name;
}
