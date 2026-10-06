using System.ComponentModel;
using System.Text.Json;

namespace Corkboard.Shared;

/// <summary>
///     全应用唯一的路径解析入口：数据目录由「包根目录 + data」决定，安装版在包目录不可写时退回用户目录，
///     便携版则必须留在可执行文件旁边，这样整个包才能被整体搬走。
///     <para>
///         <c>data/config</c> 是运行时数据目录，**刻意保持隐藏**，不摆在用户眼前。
///     </para>
/// </summary>
public static class Utils
{
    public const string PackageRootEnvironmentVariable = "CORKBOARD_PACKAGE_ROOT";
    public const string PackageMarkerFileName = "Corkboard.package.json";
    private const string ConfigDirectoryName = "config";
    private const string UnixHiddenEntriesFileName = ".hidden";
    private static readonly object DataRootGate = new();
    private static string? _configuredDataRoot;
    private static bool _dataRootWasRead;
    private static DesktopDataRootPreparationResult? _desktopPreparation;

    public readonly record struct DesktopDataRootPreparationResult(
        bool IsPortablePackage,
        bool IsWritable,
        string DataRoot,
        string? ErrorMessage);

    public static string PackageRoot => ResolvePackageRoot();

    public static string DataRoot
    {
        get
        {
            lock (DataRootGate)
            {
                _dataRootWasRead = true;
                return _configuredDataRoot ?? Path.Combine(PackageRoot, "data");
            }
        }
    }

    /// <summary>
    ///     在第一条持久化路径被使用之前选定数据根。安装版在包目录不可写时退回 LocalApplicationData；
    ///     便携版不可写时必须留在原地并把失败原因交给启动流程处理。
    /// </summary>
    public static DesktopDataRootPreparationResult PrepareDesktopDataRoot()
    {
        var packageRoot = ResolvePackageRoot();
        return PrepareDesktopDataRoot(packageRoot, IsPortablePackage(packageRoot));
    }

    private static DesktopDataRootPreparationResult PrepareDesktopDataRoot(string packageRoot, bool isPortablePackage)
    {
        lock (DataRootGate)
        {
            if (_desktopPreparation is { } prepared)
                return prepared;
            if (_dataRootWasRead)
                throw new InvalidOperationException("The data root must be prepared before it is first used.");
            if (_configuredDataRoot is not null)
                throw new InvalidOperationException("The data root has already been configured.");

            var packageDataRoot = Path.Combine(packageRoot, "data");
            if (TryVerifyWritable(packageDataRoot, out var packageError))
            {
                _configuredDataRoot = Path.GetFullPath(packageDataRoot);
                _dataRootWasRead = true;
                var result = new DesktopDataRootPreparationResult(isPortablePackage, true, _configuredDataRoot, null);
                _desktopPreparation = result;
                return result;
            }

            if (isPortablePackage)
            {
                _configuredDataRoot = Path.GetFullPath(packageDataRoot);
                _dataRootWasRead = true;
                var result = new DesktopDataRootPreparationResult(true, false, _configuredDataRoot, packageError);
                _desktopPreparation = result;
                return result;
            }

            var fallbackRoot = GetDesktopFallbackDataRoot();
            if (!TryVerifyWritable(fallbackRoot, out var fallbackError))
                throw new InvalidOperationException(
                    "Neither the installed package data directory nor the per-user data directory is writable. " +
                    $"Package: {packageDataRoot}; fallback: {fallbackRoot}; {fallbackError}");

            _configuredDataRoot = Path.GetFullPath(fallbackRoot);
            _dataRootWasRead = true;
            var fallbackResult = new DesktopDataRootPreparationResult(false, true, _configuredDataRoot, packageError);
            _desktopPreparation = fallbackResult;
            return fallbackResult;
        }
    }

    private static string GetPath([Localizable(false)] params string[] strings)
    {
        return Path.Combine([DataRoot, .. strings]);
    }

    /// <summary>取得数据目录下某个文件的绝对路径，并顺带创建父目录。</summary>
    public static string GetFilePath([Localizable(false)] params string[] strings)
    {
        var path = GetPath(strings);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            EnsureConfigDirectoryHidden(strings);
        }

        return path;
    }

    /// <summary>取得数据目录下某个子目录的绝对路径，并创建它。</summary>
    public static string GetDirectoryPath([Localizable(false)] params string[] strings)
    {
        var path = GetPath(strings);

        if (!string.IsNullOrEmpty(path))
        {
            Directory.CreateDirectory(path);
            EnsureConfigDirectoryHidden(strings);
        }

        return path;
    }

    /// <summary>
    ///     把 <c>data/config</c> 标成「隐藏 + 系统」：它是运行时数据目录，不摆在用户眼前。
    ///     Windows 用文件属性，类 Unix 走同目录的 <c>.hidden</c> 清单（freedesktop 约定）。
    /// </summary>
    private static void EnsureConfigDirectoryHidden(IReadOnlyList<string> pathSegments)
    {
        if (pathSegments.Count == 0
            || !string.Equals(pathSegments[0], ConfigDirectoryName, StringComparison.Ordinal))
            return;

        var configDirectory = Path.Combine(DataRoot, ConfigDirectoryName);
        try
        {
            var attributes = File.GetAttributes(configDirectory);
            File.SetAttributes(configDirectory, attributes | FileAttributes.Hidden | FileAttributes.System);
        }
        catch (PlatformNotSupportedException)
        {
            EnsureUnixHiddenEntry(configDirectory);
        }
        catch (IOException)
        {
            // 文件系统写不了属性时，配置读写仍然必须可用。
        }
        catch (UnauthorizedAccessException)
        {
            // 隐藏只是尽力而为，不能影响配置加载与保存。
        }

        if (!OperatingSystem.IsWindows())
            EnsureUnixHiddenEntry(configDirectory);
    }

    private static void EnsureUnixHiddenEntry(string configDirectory)
    {
        var dataDirectory = Directory.GetParent(configDirectory)?.FullName;
        if (string.IsNullOrWhiteSpace(dataDirectory))
            return;

        var hiddenEntriesPath = Path.Combine(dataDirectory, UnixHiddenEntriesFileName);
        try
        {
            var entries = File.Exists(hiddenEntriesPath)
                ? File.ReadAllLines(hiddenEntriesPath)
                : [];
            if (entries.Any(entry => string.Equals(entry.Trim(), ConfigDirectoryName, StringComparison.Ordinal)))
                return;

            using var stream = new FileStream(hiddenEntriesPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            if (stream.Length > 0)
                writer.WriteLine();
            writer.WriteLine(ConfigDirectoryName);
        }
        catch (IOException)
        {
            // 没有可写父目录时，隐藏元数据是可选项。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上，拒绝写入不应改变配置行为。
        }
    }

    private static string ResolvePackageRoot()
    {
        var appDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var configuredRoot = Environment.GetEnvironmentVariable(PackageRootEnvironmentVariable);
        if (IsPortablePackageRoot(configuredRoot, appDirectory))
            return Path.GetFullPath(configuredRoot!);

        var parent = Directory.GetParent(appDirectory)?.FullName;
        return IsPortablePackageRoot(parent, appDirectory) ? Path.GetFullPath(parent!) : appDirectory;
    }

    private static string GetDesktopFallbackDataRoot()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
            throw new InvalidOperationException("The platform does not provide a per-user data directory.");

        return Path.Combine(localApplicationData, "Corkboard", "data");
    }

    private static bool IsPortablePackage(string packageRoot)
    {
        var appDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        if (!IsPortablePackageRoot(packageRoot, appDirectory))
            return false;

        var markerPath = Path.Combine(appDirectory, PackageMarkerFileName);
        try
        {
            using var marker = JsonDocument.Parse(File.ReadAllText(markerPath));
            return marker.RootElement.TryGetProperty("packageKind", out var packageKind)
                   && string.Equals(packageKind.GetString(), "portable-zip", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryVerifyWritable(string directory, out string? errorMessage)
    {
        var testFile = Path.Combine(directory, $".corkboard-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(testFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.WriteByte(0);
                stream.Flush(true);
            }

            File.Delete(testFile);
            errorMessage = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           NotSupportedException or ArgumentException)
        {
            try
            {
                if (File.Exists(testFile))
                    File.Delete(testFile);
            }
            catch (IOException)
            {
                // 原始写入错误对调用方更有价值。
            }
            catch (UnauthorizedAccessException)
            {
                // 同上。
            }

            errorMessage = exception.Message;
            return false;
        }
    }

    private static bool IsPortablePackageRoot(string? candidate, string appDirectory)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
            return false;

        var root = Path.GetFullPath(candidate);
        var normalizedAppDirectory = Path.GetFullPath(appDirectory);
        var parent = Directory.GetParent(normalizedAppDirectory)?.FullName;
        if (!string.Equals(root, parent, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal))
            return false;

        var appDirectoryName = Path.GetFileName(normalizedAppDirectory.TrimEnd(Path.DirectorySeparatorChar));
        return appDirectoryName.StartsWith("app-", StringComparison.Ordinal)
               && File.Exists(Path.Combine(normalizedAppDirectory, PackageMarkerFileName));
    }

    internal static void ResetDataRootForTests()
    {
        lock (DataRootGate)
        {
            _configuredDataRoot = null;
            _dataRootWasRead = false;
            _desktopPreparation = null;
        }
    }
}
