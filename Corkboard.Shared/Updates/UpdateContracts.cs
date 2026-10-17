namespace Corkboard.Shared.Updates;

/// <summary>更新通道。清单里的 <c>channel</c> 用小写字符串，客户端按枚举名转成小写比对。</summary>
public enum UpdateChannel
{
    Release,
    Beta,
    Alpha
}

/// <summary>更新来源：GitHub 直连或其镜像前缀。两个来源都失败才算这次检查失败。</summary>
public enum UpdateSource
{
    GitHub,
    GitHubMirror
}

/// <summary>更新包形态。Corkboard 目前只发 Windows 包，其余枚举保留给日后的跨平台发布。</summary>
public enum UpdatePackageKind
{
    PortableZip,
    WindowsExe,
    LinuxDeb,
    MacosPkg,
    MacosApp,
    AndroidApk
}

/// <summary>更新包的运行时形态（依赖框架 / 自包含）。</summary>
public enum UpdateRuntimeKind
{
    FrameworkDependent,
    SelfContained
}

/// <summary>更新操作阶段。设置页与定时调度都按这个状态机决定按钮可用性。</summary>
public enum UpdateOperationPhase
{
    Idle,
    Checking,
    UpToDate,
    UpdateAvailable,
    Downloading,
    Verifying,
    ReadyToInstall,
    Installing,
    Restarting,
    CheckFailed,
    DownloadFailed,
    VerificationFailed,
    InstallFailed,
    Cancelled
}

/// <summary>签名的更新清单（<c>Corkboard-update-manifest.json</c>），随发布资产的同名 <c>.sig</c> 文件一起分发。</summary>
public sealed class UpdateManifest
{
    public int SchemaVersion { get; init; }
    public string Product { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string Tag { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public DateTimeOffset PublishedAt { get; init; }
    public string? NotesUrl { get; init; }
    public List<UpdateArtifact> Artifacts { get; init; } = [];
}

/// <summary>清单里的一条发布产物，对应 GitHub Release 上的一个资产。</summary>
public sealed class UpdateArtifact
{
    public string Id { get; init; } = string.Empty;
    public string Os { get; init; } = string.Empty;
    public string Arch { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string RuntimeKind { get; init; } = string.Empty;
    public string AssetName { get; init; } = string.Empty;
    public long ByteLength { get; init; }
    public string Sha512 { get; init; } = string.Empty;
    public string? RequiredRuntime { get; init; }
    public string? EntryPath { get; init; }
}

/// <summary>
///     随包分发在应用目录里的形态标记（<c>Corkboard.package.json</c>），
///     用来判断当前是便携 ZIP 还是安装版，以及更新时该挑哪一种产物。
/// </summary>
public sealed class UpdatePackageMarker
{
    public int SchemaVersion { get; init; }
    public string Product { get; init; } = string.Empty;
    public string Rid { get; init; } = string.Empty;
    public string PackageKind { get; init; } = string.Empty;
    public string RuntimeKind { get; init; } = string.Empty;
}
