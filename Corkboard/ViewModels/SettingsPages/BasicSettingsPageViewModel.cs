using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models.SubConfigs.General;
using Corkboard.Core.Services.Config;
using Corkboard.Platforms.Abstractions;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages;

/// <summary>
///     基础设置页的 ViewModel：直接暴露配置子树，页面双向绑定即可。
///     <para>
///         窗口那一摊（不透明度、置底到桌面、圆角、长按拖动、点击穿透）是作业板专属的，
///         已经整块搬到「作业板 → 窗口与收纳」（<c>BoardWindowSettingsPageViewModel</c>），这里只剩
///         语言、启动（开机自启 / 启动窗口 / 记住窗口大小）与隐私三块。
///     </para>
/// </summary>
public partial class BasicSettingsPageViewModel(
    MainConfigHandler configHandler,
    IAutostartService autostart) : ViewModelBase(configHandler)
{
    public BasicSettingsConfig Basic => Config.Basic;

    /// <summary>语言下拉项的来源：只有中文和英文，显示名固定用各自的写法。</summary>
    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new(LanguageMode.ChineseSimplified, CR.Settings_Basic_Language_ChineseSimplified),
        new(LanguageMode.English, CR.Settings_Basic_Language_English)
    ];

    /// <summary>下拉选中项。配置里若是越界值（旧版本落盘的日语 2），就退回第一项中文。</summary>
    public LanguageOption SelectedLanguage
    {
        get => LanguageOptions.FirstOrDefault(option => option.Mode == Basic.Language) ?? LanguageOptions[0];
        set
        {
            if (value is null || value.Mode == Basic.Language)
                return;

            Basic.Language = value.Mode;
            // 语言在 XAML 加载前定好，运行期改只能重启才对全部界面生效。
            LanguageChangeRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>语言改动后由页面请求重启（由 <c>SettingsView.RequestRestartApp</c> 在标题栏亮出提示）。</summary>
    public event EventHandler? LanguageChangeRequested;

    /// <summary>隐私设置子树（在线状态上报）。统计开关放这里，不往基础设置里塞。</summary>
    public PrivacySettingsConfig PrivacySettings => Config.General.PrivacySettings;

    /// <summary>在线状态三档：正常 / 匿名 / 停止（客户端上报的字段相同，位置由服务端统计）。</summary>
    public IReadOnlyList<OnlineStatusOption> OnlineStatusOptions { get; } =
    [
        new(OnlineStatusMode.Full, CR.Settings_Basic_OnlineStatus_Full),
        new(OnlineStatusMode.Anonymous, CR.Settings_Basic_OnlineStatus_Anonymous),
        new(OnlineStatusMode.Off, CR.Settings_Basic_OnlineStatus_Off)
    ];

    /// <summary>在线状态下拉选中项；配置里是未定义值时退回第一项「正常上报」。</summary>
    public OnlineStatusOption SelectedOnlineStatus
    {
        get => OnlineStatusOptions.FirstOrDefault(option => option.Mode == PrivacySettings.OnlineStatusMode)
               ?? OnlineStatusOptions[0];
        set
        {
            if (value is null || value.Mode == PrivacySettings.OnlineStatusMode)
                return;

            PrivacySettings.OnlineStatusMode = value.Mode;
        }
    }

    /// <summary>
    ///     当前平台是否实现了开机自启。没实现的平台整张卡隐藏，
    ///     而不是留一个点不动的开关（见 AGENTS.md 的设置硬规矩）。
    /// </summary>
    public bool IsAutostartSupported => autostart.IsSupported;

    /// <summary>
    ///     写入开机自启系统集成。失败时把配置回滚并返回失败原因，
    ///     由页面弹提示——绝不把失败当成功落盘。
    /// </summary>
    public bool TryApplyAutostart(bool enabled, out string? error)
    {
        if (autostart.TrySetEnabled(enabled, out error))
            return true;

        Basic.Autostart = !enabled;
        return false;
    }
}
