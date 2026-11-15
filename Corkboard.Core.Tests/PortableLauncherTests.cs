using Corkboard.Shared;

namespace Corkboard.Core.Tests;

/// <summary>
///     便携包启动入口的解析：开机自启项、<c>corkboard://</c> 协议注册与「重启」都靠
///     <see cref="Utils.FindLauncherPath" /> 找包根目录那个启动器，而不是 <c>app-*</c> 里的应用本体。
///     <para>
///         用例全是纯路径逻辑（临时目录 + 假文件），不读环境变量，也不启动任何进程。
///     </para>
/// </summary>
public class PortableLauncherTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "corkboard-launch-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录残留不影响其它用例。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }
    }

    [Fact]
    public void FindLauncherPath_PortableLayout_ResolvesPackageRootLauncher()
    {
        var appDirectory = CreatePortableLayout();

        var launcherPath = Utils.FindLauncherPath(appDirectory);

        Assert.Equal(Path.Combine(_root, Utils.LauncherFileName), launcherPath);
        // 启动器必须落在包根，而不是 app-* 目录里——那正是每个版本各一份、会被更新换掉的那份。
        Assert.Equal(_root, Path.GetDirectoryName(launcherPath));
    }

    [Fact]
    public void FindLauncherPath_ConfiguredPackageRoot_IsAccepted()
    {
        var appDirectory = CreatePortableLayout();

        Assert.Equal(Path.Combine(_root, Utils.LauncherFileName),
            Utils.FindLauncherPath(appDirectory, _root));
    }

    [Fact]
    public void FindLauncherPath_TrailingSeparator_StillResolvesParentAsPackageRoot()
    {
        // AppContext.BaseDirectory 带结尾分隔符，而 Directory.GetParent 对带结尾分隔符的路径会返回
        // 目录自己——真机上直接双击 app-*/Corkboard.Desktop.exe 走的正是这条。
        var appDirectory = CreatePortableLayout() + Path.DirectorySeparatorChar;

        Assert.Equal(Path.Combine(_root, Utils.LauncherFileName), Utils.FindLauncherPath(appDirectory));
        Assert.Equal(Path.Combine(_root, Utils.LauncherFileName),
            Utils.FindLauncherPath(appDirectory, _root + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void FindLauncherPath_MissingLauncher_ReturnsNull()
    {
        var appDirectory = CreatePortableLayout(withLauncher: false);

        Assert.Null(Utils.FindLauncherPath(appDirectory));
    }

    [Fact]
    public void FindLauncherPath_MissingPackageMarker_ReturnsNull()
    {
        var appDirectory = CreatePortableLayout(withMarker: false);

        Assert.Null(Utils.FindLauncherPath(appDirectory));
    }

    [Fact]
    public void FindLauncherPath_AppDirectoryWithoutPrefix_ReturnsNull()
    {
        var appDirectory = CreatePortableLayout("bin");

        Assert.Null(Utils.FindLauncherPath(appDirectory));
    }

    [Fact]
    public void FindLauncherPath_DevelopmentDirectory_ReturnsNull()
    {
        // 开发构建：bin/Debug/net10.0 底下没有包标记，也不该去找启动器。
        var appDirectory = Path.Combine(_root, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(appDirectory);
        File.WriteAllText(Path.Combine(appDirectory, Utils.PackageMarkerFileName), "{}");

        Assert.Null(Utils.FindLauncherPath(appDirectory));
    }

    private string CreatePortableLayout(string appDirectoryName = "app-v1.2.3-4",
        bool withLauncher = true, bool withMarker = true)
    {
        var appDirectory = Path.Combine(_root, appDirectoryName);
        Directory.CreateDirectory(appDirectory);
        if (withMarker)
            File.WriteAllText(Path.Combine(appDirectory, Utils.PackageMarkerFileName), "{}");
        if (withLauncher)
            File.WriteAllText(Path.Combine(_root, Utils.LauncherFileName), string.Empty);
        return appDirectory;
    }
}