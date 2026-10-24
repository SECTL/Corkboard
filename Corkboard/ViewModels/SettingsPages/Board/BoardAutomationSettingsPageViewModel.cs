using Corkboard.Core.Services.Config;

namespace Corkboard.ViewModels.SettingsPages.Board;

/// <summary>
///     「作业板 → 自动化与数据」设置页的 ViewModel：自动清理、历史与回放、数据维护三块。
///     <para>
///         三块的逻辑一点都没重写——它们各自还是原来的 ViewModel（清理与历史），
///         这一页只负责把它们摆到一起，所以这里只暴露两个子 VM。
///     </para>
/// </summary>
public partial class BoardAutomationSettingsPageViewModel(
    MainConfigHandler configHandler,
    CleanupSettingsPageViewModel cleanup,
    HistorySettingsPageViewModel history) : ViewModelBase(configHandler)
{
    /// <summary>自动清理与数据维护（清空作业板、立即清理）的子 VM。页面绑定加 <c>Cleanup.</c> 前缀。</summary>
    public CleanupSettingsPageViewModel Cleanup { get; } = cleanup;

    /// <summary>历史与回放（保存粒度、保留天数、容量上限、占用与立即清理）的子 VM。绑定加 <c>History.</c> 前缀。</summary>
    public HistorySettingsPageViewModel History { get; } = history;
}
