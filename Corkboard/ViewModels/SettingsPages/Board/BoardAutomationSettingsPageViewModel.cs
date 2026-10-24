using Corkboard.Core.Services.Config;

namespace Corkboard.ViewModels.SettingsPages.Board;

/// <summary>
///     「作业板 → 自动化与数据」设置页的 ViewModel：自动清理、历史与回放、自动保存、数据维护四块。
///     <para>
///         每一块的逻辑都不在这一页里——它们各自还是自己的 ViewModel（清理、历史、自动保存），
///         这一页只负责把它们摆到一起，所以这里只暴露几个子 VM。
///     </para>
/// </summary>
public partial class BoardAutomationSettingsPageViewModel(
    MainConfigHandler configHandler,
    CleanupSettingsPageViewModel cleanup,
    HistorySettingsPageViewModel history,
    AutoSaveSettingsPageViewModel autoSave) : ViewModelBase(configHandler)
{
    /// <summary>自动清理与数据维护（清空作业板、立即清理）的子 VM。页面绑定加 <c>Cleanup.</c> 前缀。</summary>
    public CleanupSettingsPageViewModel Cleanup { get; } = cleanup;

    /// <summary>历史与回放（保存粒度、保留天数、容量上限、占用与立即清理）的子 VM。绑定加 <c>History.</c> 前缀。</summary>
    public HistorySettingsPageViewModel History { get; } = history;

    /// <summary>自动保存（开关、保存时机、保留天数、保存位置、立即保存一张）的子 VM。绑定加 <c>AutoSave.</c> 前缀。</summary>
    public AutoSaveSettingsPageViewModel AutoSave { get; } = autoSave;
}
