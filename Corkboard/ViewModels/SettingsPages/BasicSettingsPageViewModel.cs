using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Display;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models.SubConfigs.General;
using Corkboard.Core.Services.Config;
using Corkboard.Platforms.Abstractions;
using Microsoft.Extensions.Logging;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages;

/// <summary>
///     基础设置页的 ViewModel：直接暴露配置子树，页面双向绑定即可。
///     <para>
///         窗口那一摊（不透明度、置底到桌面、圆角）是作业板专属的，
///         已经整块搬到「作业板 → 窗口与收纳」（<c>BoardWindowSettingsPageViewModel</c>），这里只剩
///         语言、启动（开机自启 / 启动窗口 / 记住窗口大小 / 链接调用）与隐私三块。
///     </para>
/// </summary>
public partial class BasicSettingsPageViewModel(
    MainConfigHandler configHandler,
    IAutostartService autostart,
    IUrlSchemeRegistrar urlSchemes) : ViewModelBase(configHandler)
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
    ///     崩溃恢复子树。真正处理崩溃的那条路会直接读盘（进程可能已经半死），
    ///     这里只是把配置项摊给设置页。
    /// </summary>
    public CrashRecoverySettingsConfig CrashRecoverySettings => Config.General.CrashRecovery;

    /// <summary>崩溃恢复四档，顺序与 <see cref="CrashRecoveryMode" /> 一致。</summary>
    public IReadOnlyList<CrashRecoveryModeOption> CrashRecoveryModeOptions { get; } =
    [
        new(CrashRecoveryMode.None, CR.Settings_Basic_CrashRecovery_None),
        new(CrashRecoveryMode.RestartOnly, CR.Settings_Basic_CrashRecovery_RestartOnly),
        new(CrashRecoveryMode.PromptThenRestart, CR.Settings_Basic_CrashRecovery_PromptThenRestart),
        new(CrashRecoveryMode.PromptAndRestart, CR.Settings_Basic_CrashRecovery_PromptAndRestart)
    ];

    /// <summary>崩溃恢复下拉选中项；配置里是未定义值时退回第一项「不处理」。</summary>
    public CrashRecoveryModeOption SelectedCrashRecoveryMode
    {
        get => CrashRecoveryModeOptions.FirstOrDefault(option => option.Mode == CrashRecoverySettings.Mode)
               ?? CrashRecoveryModeOptions[0];
        set
        {
            if (value is null || value.Mode == CrashRecoverySettings.Mode)
                return;

            CrashRecoverySettings.Mode = value.Mode;
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

    // ── 链接调用 ──────────────────────────────────────────────

    /// <summary>当前系统能不能注册自定义协议（macOS 改不了 Info.plist，整张卡隐藏）。</summary>
    public bool IsUrlProtocolSupported => urlSchemes.IsSupported;

    /// <summary>
    ///     链接调用那张卡里逐行摆出来的入口地址：一行一个地址、一句「它做什么」，紧跟一颗复制按钮。
    ///     地址跟着 <see cref="DisplayBoardUri" /> 认的写法一起改（协议前缀与主机名不从别处再抄一遍）。
    /// </summary>
    public IReadOnlyList<UrlProtocolEntry> UrlProtocolEntries { get; } =
    [
        new(DisplayBoardUri.Prefix + DisplayBoardUri.Host, CR.Settings_Display_Open),
        new($"{DisplayBoardUri.Prefix}{DisplayBoardUri.Host}?exit=1", CR.Settings_Display_Close)
    ];

    /// <summary>协议注册开关：写配置的同时去系统里登记 / 注销 <c>corkboard://</c>。</summary>
    public bool UrlProtocolEnabled
    {
        get => Basic.UrlProtocol;
        set
        {
            if (Basic.UrlProtocol == value)
                return;

            Basic.UrlProtocol = value;
            OnPropertyChanged();
            ApplyUrlProtocol(value);
        }
    }

    private void ApplyUrlProtocol(bool enabled)
    {
        if (!urlSchemes.IsSupported)
            return;

        if (urlSchemes.TrySetEnabled(GlobalConstants.UrlProtocolScheme, enabled, out var error))
            return;

        // 注册失败就把开关拨回去，别让界面显示一个其实没生效的「已允许」。
        Basic.UrlProtocol = !enabled;
        OnPropertyChanged(nameof(UrlProtocolEnabled));
        IAppHost.TryGetService<ILogger<BasicSettingsPageViewModel>>()
            ?.LogWarning("注册 corkboard:// 链接失败，已回滚：{Error}", error);
    }
}

/// <summary>
///     「链接调用」那张卡里的一行：一个 <c>corkboard://</c> 地址、一句它干什么，紧跟一颗复制按钮。
///     <para>
///         剪贴板只能从页面那一层拿（<c>TopLevel.Clipboard</c>），所以真正写剪贴板的动作在页面
///         code-behind 里（<c>BasicSettingsPage.OnCopyUrlClick</c>），这里只负责
///         「点过之后把『已复制』冒出来一会儿」这一下反馈。
///     </para>
/// </summary>
public partial class UrlProtocolEntry(string url, string action) : ObservableObject
{
    /// <summary>连续点同一颗按钮时只让最后一发的计时生效，免得前一次把新冒出来的提示提前收掉。</summary>
    private int _copyToken;

    /// <summary>这一行的完整地址（复制出去的就是这一串）。</summary>
    public string Url { get; } = url;

    /// <summary>这个地址做什么用（「打开全屏」/「关闭全屏」）。</summary>
    public string Action { get; } = action;

    /// <summary>刚复制过：按钮旁边冒一句「已复制」，过一会儿自己收回去。</summary>
    [ObservableProperty] private bool _isCopied;

    /// <summary>复制按钮的提示：平时是「复制链接」，刚复制完是「已复制」。</summary>
    public string CopyTip => IsCopied ? CR.Settings_Basic_UrlCopied : CR.Settings_Basic_UrlCopy;

    partial void OnIsCopiedChanged(bool value) => OnPropertyChanged(nameof(CopyTip));

    /// <summary>冒出「已复制」，<paramref name="hold" /> 之后再收回去。</summary>
    public async Task MarkCopiedAsync(TimeSpan hold)
    {
        var token = ++_copyToken;
        IsCopied = true;
        await Task.Delay(hold);
        if (token == _copyToken)
            IsCopied = false;
    }
}
