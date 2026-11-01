using Corkboard.Core.CrashRecovery;
using Corkboard.Core.Enums.Configs;

namespace Corkboard.Core.Tests;

/// <summary>
///     崩溃恢复的启动参数：<c>--crash-report &lt;路径&gt;</c> 与 <c>--crash-auto-restart</c>。
///     <para>
///         这条路上**不许再抛异常**（已经在崩溃之后了），所以解析非法输入一律返回「不是崩溃恢复」，
///         外面就按正常启动走。用例把几条脏输入钉死。
///     </para>
/// </summary>
public class CrashRecoveryPromptOptionsTests
{
    [Fact]
    public void Parse_NullArguments_ReturnsNull() => Assert.Null(CrashRecoveryPromptOptions.Parse(null));

    [Fact]
    public void Parse_EmptyArguments_ReturnsNull() => Assert.Null(CrashRecoveryPromptOptions.Parse([]));

    [Fact]
    public void Parse_UnrelatedArguments_ReturnsNull() =>
        Assert.Null(CrashRecoveryPromptOptions.Parse(["--open-settings"]));

    [Fact]
    public void Parse_SwitchWithoutReport_ReturnsNull() =>
        Assert.Null(CrashRecoveryPromptOptions.Parse([CrashRecoveryPromptOptions.CrashAutoRestartArgument]));

    [Fact]
    public void Parse_ReportArgumentWithoutValue_ReturnsNull() =>
        Assert.Null(CrashRecoveryPromptOptions.Parse([CrashRecoveryPromptOptions.CrashReportArgument]));

    [Fact]
    public void Parse_ReportOnly_DoesNotAutoRestart()
    {
        var options = CrashRecoveryPromptOptions.Parse([CrashRecoveryPromptOptions.CrashReportArgument, "C:/crashes/1.txt"]);

        Assert.NotNull(options);
        Assert.Equal("C:/crashes/1.txt", options.CrashReportPath);
        Assert.False(options.AutoRestart);
    }

    [Fact]
    public void Parse_ReportAndSwitch_AutoRestarts()
    {
        var options = CrashRecoveryPromptOptions.Parse(
        [
            CrashRecoveryPromptOptions.CrashReportArgument, "C:/crashes/1.txt",
            CrashRecoveryPromptOptions.CrashAutoRestartArgument
        ]);

        Assert.NotNull(options);
        Assert.True(options.AutoRestart);
    }

    /// <summary>开关写在报告参数前面也要认（参数顺序不固定）。</summary>
    [Fact]
    public void Parse_SwitchBeforeReport_StillAutoRestarts()
    {
        var options = CrashRecoveryPromptOptions.Parse(
        [
            CrashRecoveryPromptOptions.CrashAutoRestartArgument,
            CrashRecoveryPromptOptions.CrashReportArgument, "C:/crashes/1.txt"
        ]);

        Assert.NotNull(options);
        Assert.True(options.AutoRestart);
    }

    [Fact]
    public void RemoveCrashRecoveryArguments_KeepsEverythingElse()
    {
        var remaining = CrashRecoveryPromptOptions.RemoveCrashRecoveryArguments(
        [
            "--open-settings",
            CrashRecoveryPromptOptions.CrashReportArgument, "C:/crashes/1.txt",
            CrashRecoveryPromptOptions.CrashAutoRestartArgument
        ]);

        Assert.Equal(["--open-settings"], remaining);
    }

    [Fact]
    public void RemoveCrashRecoveryArguments_NullBecomesEmpty() =>
        Assert.Empty(CrashRecoveryPromptOptions.RemoveCrashRecoveryArguments(null));
}

/// <summary>
///     「这一档崩了之后该怎么重启」纯逻辑。四档模式的差别全在这里，
///     最容易出错的是「直接重启」那一档在退避窗口里必须变成不重启（否则崩溃—重启死循环）。
/// </summary>
public class CrashRecoveryLaunchPlanTests
{
    private const string ReportPath = "C:/crashes/crash-20261101-091500-000.txt";

    [Fact]
    public void None_DoesNotRestart() =>
        Assert.Null(CrashRecoveryLaunchPlan.Create(CrashRecoveryMode.None, ReportPath));

    [Fact]
    public void RestartOnly_RestartsWithoutArguments() =>
        Assert.Equal([], CrashRecoveryLaunchPlan.Create(CrashRecoveryMode.RestartOnly, string.Empty));

    [Fact]
    public void RestartOnly_WithoutBackoffSlot_DoesNotRestart() =>
        Assert.Null(CrashRecoveryLaunchPlan.Create(CrashRecoveryMode.RestartOnly, string.Empty, allowAutomaticRestart: false));

    [Fact]
    public void PromptThenRestart_PassesReportOnly() =>
        Assert.Equal(
            [CrashRecoveryPromptOptions.CrashReportArgument, ReportPath],
            CrashRecoveryLaunchPlan.Create(CrashRecoveryMode.PromptThenRestart, ReportPath));

    [Fact]
    public void PromptAndRestart_PassesSwitch() =>
        Assert.Equal(
            [
                CrashRecoveryPromptOptions.CrashReportArgument, ReportPath,
                CrashRecoveryPromptOptions.CrashAutoRestartArgument
            ],
            CrashRecoveryLaunchPlan.Create(CrashRecoveryMode.PromptAndRestart, ReportPath));

    /// <summary>两分钟内已经自动重启过一次：退成「先提示」，让用户自己决定，别再自己重启。</summary>
    [Fact]
    public void PromptAndRestart_WithoutBackoffSlot_FallsBackToPromptOnly() =>
        Assert.Equal(
            [CrashRecoveryPromptOptions.CrashReportArgument, ReportPath],
            CrashRecoveryLaunchPlan.Create(CrashRecoveryMode.PromptAndRestart, ReportPath, allowAutomaticRestart: false));

    [Fact]
    public void UndefinedMode_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CrashRecoveryLaunchPlan.Create((CrashRecoveryMode)99, ReportPath));

    /// <summary>计划参数 → 新进程解析，两边必须对上（这是跨进程的唯一契约）。</summary>
    [Theory]
    [InlineData(CrashRecoveryMode.PromptThenRestart, false)]
    [InlineData(CrashRecoveryMode.PromptAndRestart, true)]
    public void LaunchPlan_RoundTripsThroughPromptOptions(CrashRecoveryMode mode, bool expectedAutoRestart)
    {
        var plan = CrashRecoveryLaunchPlan.Create(mode, ReportPath);
        var options = CrashRecoveryPromptOptions.Parse(plan);

        Assert.NotNull(options);
        Assert.Equal(ReportPath, options.CrashReportPath);
        Assert.Equal(expectedAutoRestart, options.AutoRestart);
    }
}

/// <summary>
///     自动重启退避：两分钟内只准一次。状态文件坏掉 / 写不进去时一律往「能重启」那边靠，
///     宁可多给一次机会，也不要因为一个坏文件就永远不恢复。
/// </summary>
public class CrashRecoveryRestartGuardTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 30, 0, TimeSpan.FromHours(8));

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "corkboard-crash-guard-tests", Guid.NewGuid().ToString("N"));

    private string StatePath => Path.Combine(_root, "crash-restart-guard.json");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (Exception)
        {
            // 临时目录清不掉不影响用例结论。
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingState_AlwaysAllows(string? statePath) =>
        Assert.True(CrashRecoveryRestartGuard.CanBeginAutomaticRestart(statePath, Now));

    [Fact]
    public void NoStateFile_Allows() =>
        Assert.True(CrashRecoveryRestartGuard.CanBeginAutomaticRestart(StatePath, Now));

    [Fact]
    public void UnreadableState_AllowsInsteadOfBlocking()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(StatePath, "这不是 json");

        Assert.True(CrashRecoveryRestartGuard.CanBeginAutomaticRestart(StatePath, Now));
    }

    [Fact]
    public void TryBegin_WritesStateAndCreatesDirectory()
    {
        Assert.True(CrashRecoveryRestartGuard.TryBeginAutomaticRestart(StatePath, Now));
        Assert.True(File.Exists(StatePath));
    }

    [Fact]
    public void SecondRestartWithinBackoff_IsBlocked()
    {
        Assert.True(CrashRecoveryRestartGuard.TryBeginAutomaticRestart(StatePath, Now));
        Assert.False(CrashRecoveryRestartGuard.CanBeginAutomaticRestart(StatePath, Now.AddMinutes(1)));
        Assert.False(CrashRecoveryRestartGuard.TryBeginAutomaticRestart(StatePath, Now.AddMinutes(1)));
    }

    [Fact]
    public void RestartAfterBackoff_IsAllowedAgain()
    {
        Assert.True(CrashRecoveryRestartGuard.TryBeginAutomaticRestart(StatePath, Now));

        var later = Now + CrashRecoveryRestartGuard.RestartBackoff;
        Assert.True(CrashRecoveryRestartGuard.CanBeginAutomaticRestart(StatePath, later));
    }

    [Fact]
    public void BackoffWindowIsTwoMinutes() =>
        Assert.Equal(TimeSpan.FromMinutes(2), CrashRecoveryRestartGuard.RestartBackoff);
}

/// <summary>
///     崩溃恢复配置项：默认「提示并倒计时自动重启」，脏值由 <c>Normalize()</c> 收回默认值
///     （枚举按数字落盘，旧配置里可能是别的数）。
/// </summary>
public class CrashRecoverySettingsConfigTests
{
    [Fact]
    public void DefaultMode_IsPromptAndRestart()
    {
        var config = new Corkboard.Core.Models.SubConfigs.General.CrashRecoverySettingsConfig();

        Assert.Equal(CrashRecoveryMode.PromptAndRestart, config.Mode);
    }

    [Theory]
    [InlineData((CrashRecoveryMode)(-1))]
    [InlineData((CrashRecoveryMode)4)]
    [InlineData((CrashRecoveryMode)99)]
    public void Normalize_ResetsUndefinedValues(CrashRecoveryMode mode)
    {
        var config = new Corkboard.Core.Models.SubConfigs.General.CrashRecoverySettingsConfig { Mode = mode };

        config.Normalize();

        Assert.Equal(CrashRecoveryMode.PromptAndRestart, config.Mode);
    }

    [Theory]
    [InlineData(CrashRecoveryMode.None)]
    [InlineData(CrashRecoveryMode.RestartOnly)]
    [InlineData(CrashRecoveryMode.PromptThenRestart)]
    [InlineData(CrashRecoveryMode.PromptAndRestart)]
    public void Normalize_KeepsValidValues(CrashRecoveryMode mode)
    {
        var config = new Corkboard.Core.Models.SubConfigs.General.CrashRecoverySettingsConfig { Mode = mode };

        config.Normalize();

        Assert.Equal(mode, config.Mode);
    }
}
