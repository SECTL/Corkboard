using System.Text.Json;
using Corkboard.Core.Abstraction;

namespace Corkboard.Core.CrashRecovery;

/// <summary>退避守卫的落盘内容：上一次「自动重启」发生在什么时候。</summary>
public sealed record CrashRecoveryGuardState(DateTimeOffset LastAutomaticRestartAt);

/// <summary>
///     自动重启退避：崩溃—重启—再崩—再启 会变成一个死循环（用户连点都点不了），
///     所以两分钟内只允许自动重启一次。状态写在临时目录，跨进程有效。
/// </summary>
public static class CrashRecoveryRestartGuard
{
    /// <summary>两次自动重启之间的最小间隔。</summary>
    public static readonly TimeSpan RestartBackoff = TimeSpan.FromMinutes(2);

    /// <summary>
    ///     现在能不能自动重启：状态文件不存在、读不出来、或者距上次已经超过 <see cref="RestartBackoff" /> 都算能。
    ///     读失败一律当作「能」——宁可多给一次重启，也不要因为一个坏文件永远不恢复。
    /// </summary>
    public static bool CanBeginAutomaticRestart(string? statePath, DateTimeOffset now)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(statePath) || !File.Exists(statePath))
                return true;

            var json = File.ReadAllText(statePath);
            var state = JsonSerializer.Deserialize<CrashRecoveryGuardState>(json, ConfigServiceBase.JsonOptions);
            return state is null || now - state.LastAutomaticRestartAt >= RestartBackoff;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>
    ///     占用这一次自动重启（写状态文件）。已经过了退避窗口、并且状态写得进去才返回 <c>true</c>；
    ///     写不进去就返回 <c>false</c>，调用方据此放弃这一次自动重启（不然可能循环起来）。
    /// </summary>
    public static bool TryBeginAutomaticRestart(string? statePath, DateTimeOffset now)
    {
        if (!CanBeginAutomaticRestart(statePath, now))
            return false;

        try
        {
            if (string.IsNullOrWhiteSpace(statePath))
                return false;

            var directory = Path.GetDirectoryName(Path.GetFullPath(statePath));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(new CrashRecoveryGuardState(now), ConfigServiceBase.JsonOptions);
            File.WriteAllText(statePath, json);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
