using System.Reflection;
using Avalonia.Media;

namespace Corkboard.Core;

/// <summary>
///     全局常量与版本信息。版本元数据来自头程序集的
///     <see cref="AssemblyInformationalVersionAttribute" />（格式 <c>tag+commit</c>）。
/// </summary>
public static class GlobalConstants
{
    private static readonly object VersionSourceGate = new();
    private static Assembly? _versionAssembly;
    private static VersionMetadata? _versionMetadata;

    private static Assembly VersionAssembly =>
        _versionAssembly ?? Assembly.GetEntryAssembly() ?? typeof(GlobalConstants).Assembly;

    private static VersionMetadata Metadata
    {
        get
        {
            var cached = _versionMetadata;
            if (cached is not null)
                return cached;

            lock (VersionSourceGate)
                return _versionMetadata ??= ReadMetadata(VersionAssembly);
        }
    }

    /// <summary>
    ///     发布承载本次构建版本信息的头程序集。平台入口在读取任何版本值之前调用；
    ///     重新发布会清掉缓存。
    /// </summary>
    public static void SetVersionAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        lock (VersionSourceGate)
        {
            _versionAssembly = assembly;
            _versionMetadata = null;
        }
    }

    public static string Tag => Metadata.Tag;
    public static string Branch => Metadata.Branch;
    public static string CommitHash => Metadata.CommitHash[..Math.Min(7, Metadata.CommitHash.Length)];
    public static string FullCommitHash => Metadata.CommitHash;

    public static string CodeName => @"Placeholder";
    public static string Version => Tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? Tag : $@"v{Tag}";
    public static string DisplayVersion => $@"{Version} (Codename {CodeName})";
    public static string VersionLong => $@"{Version}-{CodeName}-{CommitHash}({Branch})";

    public static string PlatformExecutableExtension => OperatingSystem.IsWindows() ? @".exe" : "";

    /// <summary>应用显示名，窗口标题与协议注册共用。</summary>
    public const string AppName = "Corkboard";

    /// <summary>
    ///     SECTL 平台标识（<c>platform_id</c>）：服务端按它归并本应用的统计与版本上报。
    /// </summary>
    /// <remarks>
    ///     与 OAuth 客户端 ID（<c>Corkboard.Services.Auth.SectlAuthEndpoints.ClientId</c>）是两个不同的值，
    ///     上游 SecRandom 恰好同值，本项目不是。改这里只影响统计维度，不影响登录。
    /// </remarks>
    public const string PlatformId = "platform_29648fc4ac3ba07d";

    /// <summary>自定义 URL 协议 scheme。</summary>
    public const string UrlProtocolScheme = "corkboard";

    public const string DefaultThemeColor = "#0078D4";

    /// <summary>
    ///     应用默认字体（MiSans，小米 HyperOS 系统字体）在资源中的位置。
    ///     入口层的 <c>FontManagerOptions.DefaultFamilyName</c> 必须用这个值，否则首帧会先用系统字体渲染。
    /// </summary>
    public const string DefaultFontFamily = "avares://Corkboard/Assets/Fonts/MiSans/#MiSans";

    /// <summary>第三方图标字体（Fluent System Icons）在资源中的位置。</summary>
    public const string FluentIconsFontResource = "avares://Corkboard/Assets/Fonts/#FluentSystemIcons-Resizable";

#if DEBUG
    public static bool IsDevelopment => true;
#else
    public static bool IsDevelopment => false;
#endif

    public static FontFamily FluentIconsFontFamily { get; } = new(FluentIconsFontResource);

    /// <summary>随包分发的默认字体，供设置页把「默认」这个选项还原成真实字体。</summary>
    public static FontFamily DefaultAvaFontFamily { get; } = new(DefaultFontFamily);

    internal static VersionMetadata ReadMetadata(Assembly assembly)
    {
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(informationalVersion))
            return new VersionMetadata("0.0.0.0", "Unknown", "Unknown");

        var separator = informationalVersion.IndexOf('+');
        var generatedGitInfo = assembly.GetType("Corkboard.GitInfo");
        var branch = generatedGitInfo?.GetField("Branch", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string
                     ?? generatedGitInfo?.GetProperty("Branch", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string
                     ?? "Unknown";
        return separator < 0
            ? new VersionMetadata(informationalVersion, branch, "Unknown")
            : new VersionMetadata(informationalVersion[..separator], branch, informationalVersion[(separator + 1)..]);
    }
}

internal sealed record VersionMetadata(string Tag, string Branch, string CommitHash);
