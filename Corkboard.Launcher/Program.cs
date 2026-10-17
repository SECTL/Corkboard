using System.Diagnostics;
using System.Text.Json;

const string packageRootEnvironmentVariable = "CORKBOARD_PACKAGE_ROOT";
var root = Path.GetFullPath(AppContext.BaseDirectory);
var executableName = OperatingSystem.IsWindows() ? "Corkboard.Desktop.exe" : "Corkboard.Desktop";

// 便携包根目录只有一个启动器和一个或多个 app-* 有效载荷目录。挑选顺序与上游
// SecRandom.Launcher 一致：先看有没有 .current 标记（应用自己写的「当前版本」），
// 再比版本号，最后比目录名末尾的构建号；.partial 与缺少标记的目录一律跳过。
var installation = Directory.EnumerateDirectories(root, "app-*")
    .Where(path => IsValidInstallation(path, executableName))
    .OrderByDescending(path => File.Exists(Path.Combine(path, ".current")))
    .ThenByDescending(ParseVersion)
    .ThenByDescending(ParseBuildNumber)
    .FirstOrDefault();

if (installation is null)
{
    Console.Error.WriteLine("找不到可启动的 Corkboard 版本。请重新下载完整安装包。");
    return 1;
}

var startInfo = new ProcessStartInfo
{
    FileName = Path.Combine(installation, executableName),
    WorkingDirectory = installation,
    UseShellExecute = false
};
// 应用靠这个环境变量知道自己的数据根与包根在哪儿（Utils.PackageRoot / DataRoot），
// 这样换一个 app-* 生效时用户数据不会跟着搬。
startInfo.Environment[packageRootEnvironmentVariable] = root;
foreach (var argument in args)
    startInfo.ArgumentList.Add(argument);

try
{
    Process.Start(startInfo);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"无法启动 Corkboard：{exception.Message}");
    return 1;
}

static bool IsValidInstallation(string path, string executableName)
{
    if (File.Exists(Path.Combine(path, ".partial")) || !File.Exists(Path.Combine(path, executableName)))
        return false;

    var markerPath = Path.Combine(path, "Corkboard.package.json");
    if (!File.Exists(markerPath))
        return false;

    try
    {
        using var marker = JsonDocument.Parse(File.ReadAllText(markerPath));
        var root = marker.RootElement;
        return root.TryGetProperty("product", out var product)
               && string.Equals(product.GetString(), "Corkboard", StringComparison.Ordinal)
               && root.TryGetProperty("schemaVersion", out var schemaVersion)
               && schemaVersion.GetInt32() == 1
               && root.TryGetProperty("packageKind", out var packageKind)
               && string.Equals(packageKind.GetString(), "portable-zip", StringComparison.Ordinal);
    }
    catch (JsonException)
    {
        return false;
    }
}

static Version ParseVersion(string path)
{
    var directoryName = Path.GetFileName(path);
    if (!directoryName.StartsWith("app-v", StringComparison.Ordinal))
        return new Version();

    var versionText = directoryName[5..];
    var separatorIndex = versionText.LastIndexOf('-');
    if (separatorIndex >= 0 && int.TryParse(versionText[(separatorIndex + 1)..], out _))
        versionText = versionText[..separatorIndex];

    var prereleaseIndex = versionText.IndexOf('-');
    if (prereleaseIndex >= 0)
        versionText = versionText[..prereleaseIndex];
    return Version.TryParse(versionText, out var version) ? version : new Version();
}

static int ParseBuildNumber(string path)
{
    var directoryName = Path.GetFileName(path);
    var separatorIndex = directoryName.LastIndexOf('-');
    return separatorIndex >= 0 && int.TryParse(directoryName[(separatorIndex + 1)..], out var build)
        ? build
        : 0;
}
