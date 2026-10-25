using System.Globalization;
using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages;

/// <summary>
///     贡献人员名单里的一条：GitHub 登录名、主页地址、提交数与头像。
/// </summary>
/// <remarks>
///     形状照上游 SecRandom-C 关于页的同名类：名单来自 GitHub 的 contributors 接口，
///     接口只给头像 URL，图片要自己下（<see cref="LoadAvatarAsync" />）。
///     头像没下来只是少一张图，界面那边有占位色块兜着，所以下载失败一律吞掉。
/// </remarks>
public sealed partial class GitHubContributor : ObservableObject
{
    [ObservableProperty] private Bitmap? _avatar;

    public GitHubContributor(string login, string profileUrl, string avatarUrl, int contributions)
    {
        Login = login;
        ProfileUrl = profileUrl;
        AvatarUrl = avatarUrl;
        Contributions = contributions;
    }

    /// <summary>GitHub 登录名（列表主标题）。</summary>
    public string Login { get; }

    /// <summary>作者主页，点整行就用系统浏览器打开。</summary>
    public string ProfileUrl { get; }

    /// <summary>头像图片地址（接口给的那个）。</summary>
    public string AvatarUrl { get; }

    /// <summary>提交次数。</summary>
    public int Contributions { get; }

    /// <summary>「N 次提交」：条数文案跟界面语言走，所以在这里格式化而不是写死在 XAML 里。</summary>
    public string ContributionText =>
        string.Format(CultureInfo.CurrentCulture, CR.Settings_About_ContributorCommits, Contributions);

    /// <summary>
    ///     下载并解码头像。失败（网络、非图片、已取消）就保持 <see cref="Avatar" /> 为空，
    ///     界面显示占位色块——名单里有几十个人，不值得为一张头像打断整页。
    /// </summary>
    public async Task LoadAvatarAsync(HttpClient client, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = await client.GetStreamAsync(AvatarUrl, cancellationToken)
                .ConfigureAwait(true);

            // 先整个读进内存再解码：GitHub 的头像 URL 是重定向到 CDN 的流，
            // 直接喂给 Bitmap 时偶发「流不支持定位」。
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(true);
            buffer.Position = 0;
            Avatar = new Bitmap(buffer);
        }
        catch (Exception)
        {
            // 单张头像失败不影响名单本身。
            Avatar = null;
        }
    }
}

/// <summary>GitHub <c>/contributors</c> 接口返回的一项（只取用得到的字段）。</summary>
public sealed class GitHubContributorResponse
{
    [JsonPropertyName("login")] public string? Login { get; set; }

    [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }

    [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; set; }

    [JsonPropertyName("contributions")] public int Contributions { get; set; }

    [JsonPropertyName("type")] public string? Type { get; set; }
}
