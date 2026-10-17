using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Platform;
using Microsoft.Extensions.Logging;
using Corkboard.Core;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Updates;
using Corkboard.Shared;
using Corkboard.Shared.Updates;
using YamlDotNet.Serialization;

namespace Corkboard.Services.Updates;

/// <summary>
///     自动更新中心：metadata.yaml → 通道 tag → 带签名的更新清单 → 挑本机可用的安装包 →
///     下载 → 校验（长度 + SHA-512）→ 便携 ZIP 事务部署或交给系统安装器。
///     <para>
///         两个来源（GitHub 镜像优先、GitHub 直连兜底）逐级回退；清单与签名都过了 Ed25519 校验才采信。
///         列表属性是设置页的绑定源，所以状态一律只在 UI 线程上通过 <see cref="PropertyChanged" /> 通知。
///     </para>
///     <para>
///         Corkboard 目前只发 Windows 包，但产物匹配、包形态与安装器分支保留跨平台形态，
///         日后加 Linux/macOS 时不用改契约，只加发布产物。
///     </para>
/// </summary>
public sealed class UpdateCenterService(
    MainConfigHandler configHandler,
    ILogger<UpdateCenterService> logger,
    HttpClient httpClient)
    : INotifyPropertyChanged
{
    private const string Product = "Corkboard";
    private const string Repository = "SECTL/Corkboard";
    private const string ManifestFileName = "Corkboard-update-manifest.json";
    private const string SignatureFileName = "Corkboard-update-manifest.sig";
    private const int MaxMetadataBytes = 256 * 1024;
    private const int MaxManifestBytes = 4 * 1024 * 1024;
    private const int MaxSignatureBytes = 64;
    private const int MaxArtifactBytes = 512 * 1024 * 1024;
    private const string PublicKeyResourceUri = "avares://Corkboard/Assets/Updates/release-public-key.txt";
    private static readonly Uri GitHubRawMetadataUri = new("https://raw.githubusercontent.com/SECTL/Corkboard/main/metadata.yaml");
    private static readonly Uri GitHubMirrorPrefix = new("https://ghproxy.sectl.cn/");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly HttpClient _httpClient = httpClient;
    private readonly IDeserializer _yamlDeserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();
    private CancellationTokenSource? _operationCancellation;
    private UpdateOperationPhase _phase;
    private string _statusMessage = Text("M_StatusNotChecked");
    private UpdateManifest? _manifest;
    private UpdateArtifact? _selectedArtifact;
    private UpdateSource _activeSource;
    private string? _downloadedPackagePath;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<UpdateArtifact> AvailableArtifacts { get; } = [];

    public UpdateOperationPhase Phase
    {
        get => _phase;
        private set
        {
            if (SetField(ref _phase, value))
                OnPropertyChanged(nameof(CanCheck));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetField(ref _statusMessage, value))
                OnPropertyChanged(nameof(Status));
        }
    }

    public string Status => StatusMessage;

    public string CurrentVersion => GlobalConstants.Version;
    public string AvailableVersion => _manifest?.Version ?? string.Empty;
    public string NotesUrl => _manifest?.NotesUrl ?? string.Empty;

    public UpdateArtifact? SelectedArtifact
    {
        get => _selectedArtifact;
        set
        {
            if (SetField(ref _selectedArtifact, value))
                OnStateChanged();
        }
    }

    public bool IsBusy => Phase is UpdateOperationPhase.Checking or UpdateOperationPhase.Downloading
        or UpdateOperationPhase.Verifying or UpdateOperationPhase.Installing;
    public bool CanCheck => !IsBusy;
    public bool CanDownloadAndInstall => Phase == UpdateOperationPhase.UpdateAvailable && SelectedArtifact is not null;
    public bool CanApplyUpdate => Phase == UpdateOperationPhase.ReadyToInstall && SelectedArtifact is not null;
    public bool HasAvailableArtifacts => AvailableArtifacts.Count > 0;

    public string PrimaryActionText => CanDownloadAndInstall
        ? Text("C_DownloadAndInstall")
        : Text("C_CheckUpdates");

    /// <summary>
    ///     检查更新。<paramref name="force" /> 为真时不比版本，只要签名清单可用就进「有更新」状态（强制检查）。
    /// </summary>
    public async Task CheckAsync(bool force = false)
    {
        if (IsBusy)
            return;

        CancelCurrentOperation();
        _operationCancellation = new CancellationTokenSource();
        var cancellationToken = _operationCancellation.Token;
        try
        {
            Phase = UpdateOperationPhase.Checking;
            StatusMessage = Text("M_StatusChecking");
            RecordCheckAttempt();
            var channel = GetChannel();
            var (source, manifest) = await GetManifestAsync(channel, cancellationToken);
            _activeSource = source;
            if (!force && !UpdateVersionComparer.IsNewer(manifest.Version, GlobalConstants.Version))
            {
                Phase = UpdateOperationPhase.UpToDate;
                StatusMessage = Text("M_StatusUpToDate");
                return;
            }

            var artifacts = manifest.Artifacts.Where(IsCompatibleArtifact).ToArray();
            if (artifacts.Length == 0)
                throw new InvalidOperationException(Text("M_NoCompatibleArtifact"));

            _manifest = manifest;
            AvailableArtifacts.Clear();
            foreach (var artifact in artifacts)
                AvailableArtifacts.Add(artifact);
            SelectedArtifact = ReadCurrentPackageMarker() is null ? null : SelectDefaultArtifact(artifacts);
            OnPropertyChanged(nameof(AvailableVersion));
            OnPropertyChanged(nameof(NotesUrl));
            Phase = UpdateOperationPhase.UpdateAvailable;
            StatusMessage = SelectedArtifact is null
                ? string.Format(CultureInfo.CurrentUICulture, Text("M_StatusUpdateAvailableSelect"), manifest.Version)
                : string.Format(CultureInfo.CurrentUICulture, Text("M_StatusUpdateAvailable"), manifest.Version);
        }
        catch (OperationCanceledException)
        {
            Phase = UpdateOperationPhase.Cancelled;
            StatusMessage = Text("M_StatusCancelled");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "检查应用更新失败。");
            Phase = UpdateOperationPhase.CheckFailed;
            StatusMessage = exception.Message;
        }
        finally
        {
            OnStateChanged();
        }
    }

    /// <summary>一键更新：先下载校验，再部署/安装并重启。</summary>
    public async Task DownloadAndInstallAsync()
    {
        await DownloadAsync(installAfterDownload: false);
        await ApplyDownloadedUpdateAsync();
    }

    /// <summary>下载并校验安装包；<paramref name="installAfterDownload" /> 为真时立即部署/安装。</summary>
    public async Task DownloadAsync(bool installAfterDownload)
    {
        if (_manifest is null || SelectedArtifact is null || IsBusy)
            return;

        CancelCurrentOperation();
        _operationCancellation = new CancellationTokenSource();
        var cancellationToken = _operationCancellation.Token;
        try
        {
            Phase = UpdateOperationPhase.Downloading;
            StatusMessage = string.Format(CultureInfo.CurrentUICulture, Text("M_StatusDownloading"), SelectedArtifact.AssetName);
            var package = await DownloadAssetWithFallbackAsync(_manifest.Tag, SelectedArtifact.AssetName, MaxArtifactBytes, cancellationToken);
            Phase = UpdateOperationPhase.Verifying;
            StatusMessage = Text("M_StatusVerifying");
            VerifyArtifact(package, SelectedArtifact);

            var updateDirectory = Utils.GetDirectoryPath("updates", "downloads");
            var extension = Path.GetExtension(SelectedArtifact.AssetName);
            var packagePath = Path.Combine(updateDirectory, SelectedArtifact.Sha512.ToLowerInvariant() + extension);
            // 先写 .partial 再原子改名：下载中途退出不会留下一个看起来完整的包。
            await File.WriteAllBytesAsync(packagePath + ".partial", package, cancellationToken);
            File.Move(packagePath + ".partial", packagePath, true);
            _downloadedPackagePath = packagePath;

            if (!installAfterDownload)
            {
                Phase = UpdateOperationPhase.ReadyToInstall;
                StatusMessage = Text("M_StatusReadyToInstall");
                return;
            }
        }
        catch (OperationCanceledException)
        {
            Phase = UpdateOperationPhase.Cancelled;
            StatusMessage = Text("M_StatusCancelled");
        }
        catch (CryptographicException exception)
        {
            logger.LogWarning(exception, "更新包完整性校验失败。");
            Phase = UpdateOperationPhase.VerificationFailed;
            StatusMessage = exception.Message;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "应用更新失败。");
            Phase = UpdateOperationPhase.InstallFailed;
            StatusMessage = exception.Message;
        }
        finally
        {
            OnStateChanged();
        }
    }

    /// <summary>把已经下载校验过的包部署/安装，然后重启到新版本。</summary>
    public async Task ApplyDownloadedUpdateAsync()
    {
        if (_downloadedPackagePath is null || SelectedArtifact is null || IsBusy)
            return;

        try
        {
            Phase = UpdateOperationPhase.Installing;
            StatusMessage = Text("M_StatusPreparing");
            var packageKind = ParsePackageKind(SelectedArtifact.Kind);
            if (packageKind == UpdatePackageKind.PortableZip)
                await DeployPortableAsync(_downloadedPackagePath, SelectedArtifact, CancellationToken.None);
            else
                StartSystemInstaller(_downloadedPackagePath, packageKind);

            Phase = UpdateOperationPhase.Restarting;
            StatusMessage = Text("M_StatusRestarting");
            if (packageKind == UpdatePackageKind.PortableZip)
                await App.Current.RestartThroughLauncherAsync();
            else
                await App.Current.StopAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "应用更新失败。");
            Phase = UpdateOperationPhase.InstallFailed;
            StatusMessage = exception.Message;
        }
        finally
        {
            OnStateChanged();
        }
    }

    public void CancelCurrentOperation() => _operationCancellation?.Cancel();

    private void RecordCheckAttempt()
    {
        configHandler.Data.UpdateSettings.LastCheckTime = DateTime.Now;
        configHandler.Save();
    }

    private async Task<string> GetChannelTagAsync(UpdateSource source, UpdateChannel channel, CancellationToken cancellationToken)
    {
        var metadataUri = source == UpdateSource.GitHub
            ? GitHubRawMetadataUri
            : new Uri($"{GitHubMirrorPrefix}{GitHubRawMetadataUri.AbsoluteUri}");
        var metadataBytes = await BoundedContentReader.GetAsync(_httpClient, metadataUri, MaxMetadataBytes, cancellationToken);
        var yaml = Encoding.UTF8.GetString(metadataBytes);
        var metadata = _yamlDeserializer.Deserialize<MetadataDocument>(yaml)
                       ?? throw new InvalidDataException(Text("M_MetadataEmpty"));
        if (metadata.SchemaVersion != 2
            || !string.Equals(metadata.Product, Product, StringComparison.Ordinal)
            || metadata.Channels is null)
            throw new InvalidDataException(Text("M_MetadataInvalid"));
        var name = channel.ToString().ToLowerInvariant();
        return metadata.Channels.TryGetValue(name, out var entry) && !string.IsNullOrWhiteSpace(entry.Tag)
            ? entry.Tag
            : throw new InvalidDataException(string.Format(CultureInfo.CurrentUICulture, Text("M_MetadataChannelMissing"), name));
    }

    private async Task<byte[]> DownloadAssetAsync(UpdateSource source, string tag, string assetName, int maxBytes, CancellationToken cancellationToken)
    {
        Uri uri = source switch
        {
            UpdateSource.GitHub => new Uri($"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(assetName)}"),
            UpdateSource.GitHubMirror => new Uri($"{GitHubMirrorPrefix}https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(assetName)}"),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
        return await BoundedContentReader.GetAsync(_httpClient, uri, maxBytes, cancellationToken);
    }

    private UpdateManifest VerifyAndDeserializeManifest(byte[] manifestBytes, byte[] signatureBytes, string expectedTag, UpdateChannel expectedChannel)
    {
        var publicKey = ReadEmbeddedPublicKey();
        return UpdateManifestVerifier.Verify(manifestBytes, signatureBytes, publicKey, Product, expectedTag, expectedChannel);
    }

    private static byte[] ReadEmbeddedPublicKey()
    {
        using var stream = AssetLoader.Open(new Uri(PublicKeyResourceUri));
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return UpdateManifestVerifier.DecodePublicKey(reader.ReadToEnd());
    }

    private bool IsCompatibleArtifact(UpdateArtifact artifact)
    {
        var os = GetCurrentOs();
        if (os is null || !string.Equals(artifact.Os, os, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(artifact.Arch, GetCurrentArchitecture(), StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(artifact.RuntimeKind, "framework-dependent", StringComparison.OrdinalIgnoreCase)
            && !HasRequiredDesktopRuntime())
            return false;

        var currentMarker = ReadCurrentPackageMarker();
        return currentMarker is null || string.Equals(artifact.Kind, currentMarker.PackageKind, StringComparison.OrdinalIgnoreCase);
    }

    private UpdateArtifact? SelectDefaultArtifact(IEnumerable<UpdateArtifact> artifacts)
    {
        var marker = ReadCurrentPackageMarker();
        if (marker is not null)
        {
            var exact = artifacts.FirstOrDefault(artifact => string.Equals(artifact.Kind, marker.PackageKind, StringComparison.OrdinalIgnoreCase)
                                                              && string.Equals(artifact.RuntimeKind, marker.RuntimeKind, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                return exact;
        }

        return artifacts.FirstOrDefault(artifact => string.Equals(artifact.RuntimeKind, "self-contained", StringComparison.OrdinalIgnoreCase))
               ?? artifacts.FirstOrDefault();
    }

    private static UpdatePackageMarker? ReadCurrentPackageMarker()
    {
        var path = Path.Combine(AppContext.BaseDirectory, Utils.PackageMarkerFileName);
        if (!File.Exists(path))
            return null;
        try
        {
            var marker = JsonSerializer.Deserialize<UpdatePackageMarker>(File.ReadAllText(path), JsonOptions);
            return marker is { SchemaVersion: 1, Product: Product } ? marker : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task DeployPortableAsync(string packagePath, UpdateArtifact artifact, CancellationToken cancellationToken)
    {
        if (Utils.PackageRoot == Path.GetFullPath(AppContext.BaseDirectory))
            throw new InvalidOperationException(Text("M_PortableLauncherMissing"));

        var transaction = Guid.NewGuid().ToString("N");
        var stagingRoot = Utils.GetDirectoryPath("updates", "staging", transaction);
        ExtractPortableZip(packagePath, stagingRoot);
        var appDirectory = Directory.EnumerateDirectories(stagingRoot, "app-*").SingleOrDefault();
        if (appDirectory is null)
            throw new InvalidDataException(Text("M_ZipAppDirectoryMissing"));

        var marker = JsonSerializer.Deserialize<UpdatePackageMarker>(
                         File.ReadAllText(Path.Combine(appDirectory, Utils.PackageMarkerFileName)), JsonOptions)
                     ?? throw new InvalidDataException(Text("M_ZipMarkerMissing"));
        if (marker.SchemaVersion != 1 || !string.Equals(marker.Product, Product, StringComparison.Ordinal)
            || !string.Equals(marker.Rid, GetCurrentRid(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(marker.PackageKind, "portable-zip", StringComparison.Ordinal)
            || !string.Equals(marker.RuntimeKind, artifact.RuntimeKind, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(Text("M_ZipMismatch"));

        // .partial 先落地再整目录搬走：中途崩掉时下次能看到「没搬完」，不会假装是新版本。
        await File.WriteAllTextAsync(Path.Combine(appDirectory, ".partial"), string.Empty, cancellationToken);
        var targetDirectory = Path.Combine(Utils.PackageRoot, Path.GetFileName(appDirectory));
        if (Directory.Exists(targetDirectory))
            throw new InvalidOperationException(Text("M_TargetVersionExists"));
        Directory.Move(appDirectory, targetDirectory);
        File.Delete(Path.Combine(targetDirectory, ".partial"));

        var launcherName = OperatingSystem.IsWindows() ? "CorkboardLauncher.exe" : "CorkboardLauncher";
        var stagedLauncher = Path.Combine(stagingRoot, launcherName);
        if (!File.Exists(stagedLauncher))
            throw new InvalidDataException(Text("M_ZipLauncherMissing"));
        var targetLauncher = Path.Combine(Utils.PackageRoot, launcherName);
        File.Move(stagedLauncher, targetLauncher + ".new", true);
        File.Move(targetLauncher + ".new", targetLauncher, true);

        // .current 是 Launcher 的「上次跑的是哪一版」偏好：先摘掉旧的，再给新版本打上。
        foreach (var previous in Directory.EnumerateDirectories(Utils.PackageRoot, "app-*"))
            if (!string.Equals(previous, targetDirectory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                File.Delete(Path.Combine(previous, ".current"));
        await File.WriteAllTextAsync(Path.Combine(targetDirectory, ".current"), string.Empty, cancellationToken);
    }

    private static void VerifyArtifact(byte[] bytes, UpdateArtifact artifact)
    {
        if (bytes.LongLength != artifact.ByteLength)
            throw new CryptographicException(Text("M_ArtifactLengthMismatch"));
        var hash = Convert.ToHexString(SHA512.HashData(bytes));
        if (!string.Equals(hash, artifact.Sha512, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException(Text("M_ArtifactHashMismatch"));
    }

    private static void ExtractPortableZip(string packagePath, string stagingRoot)
    {
        const int maxEntries = 20_000;
        const long maxTotalBytes = 4L * 1024 * 1024 * 1024;
        using var archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count > maxEntries)
            throw new InvalidDataException(Text("M_ZipEntryLimit"));

        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;
            if (Path.IsPathRooted(entry.FullName) || entry.FullName.Contains("..", StringComparison.Ordinal))
                throw new InvalidDataException(Text("M_ZipInvalidPath"));
            totalBytes = checked(totalBytes + entry.Length);
            if (totalBytes > maxTotalBytes)
                throw new InvalidDataException(Text("M_ZipSizeLimit"));

            var targetPath = Path.GetFullPath(Path.Combine(stagingRoot, entry.FullName));
            if (!targetPath.StartsWith(Path.GetFullPath(stagingRoot) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
                throw new InvalidDataException(Text("M_ZipOutsidePath"));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, overwrite: false);
        }
    }

    private static void StartSystemInstaller(string packagePath, UpdatePackageKind packageKind)
    {
        ProcessStartInfo startInfo = packageKind switch
        {
            UpdatePackageKind.WindowsExe => new ProcessStartInfo(packagePath)
            {
                UseShellExecute = true,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS"
            },
            UpdatePackageKind.LinuxDeb => new ProcessStartInfo("pkexec")
            {
                UseShellExecute = false,
                ArgumentList = { "apt", "install", "-y", packagePath }
            },
            UpdatePackageKind.MacosPkg => new ProcessStartInfo("open")
            {
                UseShellExecute = false,
                ArgumentList = { packagePath }
            },
            UpdatePackageKind.MacosApp => new ProcessStartInfo("open")
            {
                UseShellExecute = false,
                ArgumentList = { packagePath }
            },
            _ => throw new InvalidOperationException(Text("M_InstallerUnsupported"))
        };
        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException(Text("M_InstallerStartFailed"));
    }

    private UpdateChannel GetChannel() => configHandler.Data.UpdateSettings.UpdateChannel;

    private async Task<(UpdateSource Source, UpdateManifest Manifest)> GetManifestAsync(
        UpdateChannel channel, CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        foreach (var source in GetSources())
        {
            try
            {
                var tag = await GetChannelTagAsync(source, channel, cancellationToken);
                var manifestBytes = await DownloadAssetAsync(source, tag, ManifestFileName, MaxManifestBytes, cancellationToken);
                var signatureBytes = await DownloadAssetAsync(source, tag, SignatureFileName, MaxSignatureBytes, cancellationToken);
                return (source, VerifyAndDeserializeManifest(manifestBytes, signatureBytes, tag, channel));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastException = exception;
                logger.LogInformation(exception, "更新源 {Source} 不可用，尝试下一个来源。", source);
            }
        }

        if (lastException is HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound })
            throw new InvalidOperationException(Text("M_ReleaseManifestMissing"), lastException);

        if (lastException is HttpRequestException)
            throw new InvalidOperationException(Text("M_UpdateSourceUnavailable"), lastException);

        throw lastException ?? new InvalidOperationException(Text("M_NoUpdateSourceAvailable"));
    }

    private static IEnumerable<UpdateSource> GetSources()
    {
        return [UpdateSource.GitHubMirror, UpdateSource.GitHub];
    }

    private async Task<byte[]> DownloadAssetWithFallbackAsync(string tag, string assetName, int maxBytes, CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        // 上次清单是从哪个来源拿到的，下载就先走那个来源，减少一次跨境请求。
        foreach (var source in GetSources().OrderBy(source => source == _activeSource ? 0 : 1))
        {
            try
            {
                return await DownloadAssetAsync(source, tag, assetName, maxBytes, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastException = exception;
                logger.LogInformation(exception, "更新包来源 {Source} 不可用，尝试下一个来源。", source);
            }
        }

        throw lastException ?? new InvalidOperationException(Text("M_NoUpdateSourceAvailable"));
    }

    private static UpdatePackageKind ParsePackageKind(string value) => value.ToLowerInvariant() switch
    {
        "portable-zip" => UpdatePackageKind.PortableZip,
        "windows-exe" => UpdatePackageKind.WindowsExe,
        "linux-deb" => UpdatePackageKind.LinuxDeb,
        "macos-pkg" => UpdatePackageKind.MacosPkg,
        "macos-app" => UpdatePackageKind.MacosApp,
        _ => throw new InvalidDataException(Text("M_UnknownPackageKind"))
    };

    private static string? GetCurrentOs() => OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsLinux() ? "linux"
        : OperatingSystem.IsMacOS() ? "macos"
        : null;

    private static string GetCurrentArchitecture() => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant() switch
    {
        "x64" => "x64",
        "x86" => "x86",
        "arm64" => "arm64",
        var value => value
    };

    private static string? GetCurrentRid() => OperatingSystem.IsWindows() ? $"win-{GetCurrentArchitecture()}"
        : OperatingSystem.IsLinux() ? $"linux-{GetCurrentArchitecture()}"
        : OperatingSystem.IsMacOS() ? $"osx-{GetCurrentArchitecture()}"
        : null;

    private static bool HasRequiredDesktopRuntime()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("dotnet", "--list-runtimes")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
                return false;

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000) || process.ExitCode != 0)
                return false;
            var runtime = OperatingSystem.IsWindows() ? "Microsoft.WindowsDesktop.App 10." : "Microsoft.NETCore.App 10.";
            return output.Split('\n').Any(line => line.StartsWith(runtime, StringComparison.Ordinal));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Text(string key) => UpdateText.Get(key);

    private void OnStateChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(CanCheck));
        OnPropertyChanged(nameof(CanDownloadAndInstall));
        OnPropertyChanged(nameof(CanApplyUpdate));
        OnPropertyChanged(nameof(HasAvailableArtifacts));
    }

    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName!);
        return true;
    }

    private sealed class MetadataDocument
    {
        [YamlMember(Alias = "schema_version")]
        public int SchemaVersion { get; init; }

        [YamlMember(Alias = "product")]
        public string Product { get; init; } = string.Empty;

        [YamlMember(Alias = "channels")]
        public Dictionary<string, MetadataChannel>? Channels { get; init; }
    }

    private sealed class MetadataChannel
    {
        [YamlMember(Alias = "tag")]
        public string Tag { get; init; } = string.Empty;
    }
}
