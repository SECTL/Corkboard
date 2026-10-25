using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core;
using Corkboard.Core.Services.Config;
using Corkboard.Services.Desktop;
using Corkboard.Services.Stats;
using Microsoft.Extensions.Logging;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages;

/// <summary>
///     关于页的 ViewModel：版本信息来自 <see cref="GlobalConstants" />，外部链接交给
///     <see cref="IExternalLauncher" /> 用系统浏览器打开，在线人数来自后台的
///     <see cref="OnlineStatusService" />，贡献人员名单从 GitHub 拉。
/// </summary>
/// <remarks>
///     结构照上游 SecRandom-C 的关于页：页头海报（现在先空着）、应用信息 / 作者 / 鸣谢三张卡，
///     鸣谢里的「贡献人员」点开是右侧抽屉（抽屉宿主在设置壳上，见 <c>SettingsView</c>）。
/// </remarks>
public partial class AboutSettingsPageViewModel : ViewModelBase
{
    /// <summary>贡献人员名单的接口地址。</summary>
    private const string ContributorsEndpoint =
        "https://api.github.com/repos/SECTL/Corkboard/contributors?per_page=30";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IExternalLauncher _launcher;
    private readonly ILogger<AboutSettingsPageViewModel> _logger;
    private readonly OnlineStatusService _onlineStatus;

    /// <summary>是不是正在拉贡献人员名单（决定刷新按钮能不能点）。</summary>
    [ObservableProperty] private bool _isRefreshingContributors;

    /// <summary>拉名单失败时的提示；成功时清空。</summary>
    [ObservableProperty] private string? _contributorsError;

    public AboutSettingsPageViewModel(
        MainConfigHandler configHandler,
        IHttpClientFactory httpClientFactory,
        IExternalLauncher launcher,
        ILogger<AboutSettingsPageViewModel> logger,
        OnlineStatusService onlineStatus) : base(configHandler)
    {
        _httpClientFactory = httpClientFactory;
        _launcher = launcher;
        _logger = logger;
        _onlineStatus = onlineStatus;
        _onlineStatus.OnlineCountChanged += OnOnlineCountChanged;

        BannerSource = LoadBanner();
    }

    public string AppName => GlobalConstants.AppName;

    public string Version => GlobalConstants.DisplayVersion;

    /// <summary>版权年份算到今年，免得每年手动改一次。</summary>
    public string Copyright => $"Copyright (c) 2025-{DateTime.Now.Year} 思拓创联";

    /// <summary>提交号：用户报问题时报的这一串，对着仓库能定位到具体一版。</summary>
    public string Commit => GlobalConstants.CommitHash;

    /// <summary>系统版本 + 架构：出问题时最先要知道的就是「在什么系统上」。</summary>
    public string Platform =>
        $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";

    /// <summary>运行库版本：自包含包实际跑在哪个 .NET 上。</summary>
    public string Runtime => RuntimeInformation.FrameworkDescription;

    /// <summary>
    ///     页头海报。**目前项目里还没有图**（`Assets/Banners` 只留了目录与文件名约定，
    ///     见那个目录下的 README），所以这里通常是 null，页头退化成只有标题的一截。
    ///     把 `corkboard-banner-cn.png` 之类丢进去就自动显示，不用改代码。
    /// </summary>
    public Bitmap? BannerSource { get; }

    /// <summary>有海报才画图：没图时不占高度，页头就是标题那一截。</summary>
    public bool HasBanner => BannerSource is not null;

    /// <summary>
    ///     在线人数。还没拿到（或隐私设置是「停止上报」）时显示「—」，
    ///     不把 0 当真实数字写出来。
    /// </summary>
    public string OnlineUsersText => string.Format(
        CultureInfo.CurrentCulture, CR.Settings_About_OnlineUsersFormat,
        _onlineStatus.HasOnlineCount
            ? _onlineStatus.OnlineCount.ToString("N0", CultureInfo.CurrentCulture)
            : "—");

    /// <summary>贡献人员名单（抽屉里那个列表，头像异步补）。</summary>
    public ObservableCollection<GitHubContributor> Contributors { get; } = [];

    /// <summary>刷新中不让重复点。</summary>
    public bool CanRefreshContributors => !IsRefreshingContributors;

    /// <summary>页面打开时主动查一次在线人数；失败保持占位符，不弹任何提示。</summary>
    public async Task RefreshOnlineStatusAsync()
    {
        await _onlineStatus.GetOnlineStatsAsync().ConfigureAwait(true);
        OnPropertyChanged(nameof(OnlineUsersText));
    }

    /// <summary>离开可视树时断开对单例服务的订阅（页面是 transient 的，见 AGENTS 的设置页规矩）。</summary>
    public void Detach()
    {
        _onlineStatus.OnlineCountChanged -= OnOnlineCountChanged;
    }

    /// <summary>
    ///     打开链接。地址来自 XAML 里各按钮的 <c>CommandParameter</c>（都是常量字面量），
    ///     所以这里不再拼接，交给 <see cref="IExternalLauncher" /> 判断能不能开。
    /// </summary>
    [RelayCommand]
    private void OpenUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return;

        _launcher.TryOpenUri(uri);
    }

    /// <summary>
    ///     拉贡献人员名单并补头像。列表先按接口顺序填好（头像后到，界面不用等），
    ///     失败只把原因写进 <see cref="ContributorsError" />，让抽屉里那一行红字说明情况。
    /// </summary>
    [RelayCommand]
    private async Task RefreshContributorsAsync()
    {
        if (IsRefreshingContributors)
            return;

        IsRefreshingContributors = true;
        ContributorsError = null;
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"Corkboard/{GlobalConstants.Version}");

            var response = await FetchContributorsAsync(client).ConfigureAwait(true);
            if (response is null)
                return;

            Contributors.Clear();
            foreach (var item in response)
            {
                if (string.IsNullOrWhiteSpace(item.Login)
                    || string.IsNullOrWhiteSpace(item.HtmlUrl)
                    || string.IsNullOrWhiteSpace(item.AvatarUrl))
                {
                    continue;
                }

                // 机器人账号（dependabot 之类）不进名单，那是工具不是人。
                if (!string.Equals(item.Type, "User", StringComparison.Ordinal))
                    continue;

                Contributors.Add(new GitHubContributor(item.Login, item.HtmlUrl, item.AvatarUrl,
                    item.Contributions));
            }

            // 头像一张一张补：几十个请求并发打出去容易被限流，串行也更快看到结果。
            foreach (var contributor in Contributors)
                await contributor.LoadAvatarAsync(client).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "加载贡献者名单失败。");
            ContributorsError = string.Format(CultureInfo.CurrentCulture,
                CR.Settings_About_ContributorsLoadFailed, exception.Message);
        }
        finally
        {
            IsRefreshingContributors = false;
        }
    }

    /// <summary>取一次名单；网络抖一下重试一次，彻底失败就交给调用方报错。</summary>
    private async Task<GitHubContributorResponse[]?> FetchContributorsAsync(HttpClient client)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await client.GetFromJsonAsync<GitHubContributorResponse[]>(ContributorsEndpoint)
                    .ConfigureAwait(true);
            }
            catch (Exception exception) when (attempt == 0
                                              && exception is HttpRequestException or TaskCanceledException)
            {
                _logger.LogDebug(exception, "加载贡献者名单失败，稍后重试一次。");
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
            }
        }
    }

    /// <summary>
    ///     按界面语言挑一张海报。缺哪个语言就退回英文那张；一张都没有就返回 null
    ///     （页头退化成纯标题，不报错）。
    /// </summary>
    private Bitmap? LoadBanner()
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        string[] candidates = language switch
        {
            "zh" => ["cn", "en"],
            "ja" => ["ja", "en"],
            _ => ["en"]
        };

        foreach (var suffix in candidates)
        {
            var uri = new Uri($"avares://Corkboard/Assets/Banners/corkboard-banner-{suffix}.png");
            try
            {
                if (!AssetLoader.Exists(uri))
                    continue;

                return new Bitmap(AssetLoader.Open(uri));
            }
            catch (Exception exception)
            {
                // 图坏了不该让整页打不开。
                _logger.LogDebug(exception, "海报 {Uri} 加载失败。", uri);
            }
        }

        return null;
    }

    private void OnOnlineCountChanged(object? sender, EventArgs e) =>
        OnPropertyChanged(nameof(OnlineUsersText));

    partial void OnIsRefreshingContributorsChanged(bool value) =>
        OnPropertyChanged(nameof(CanRefreshContributors));
}
