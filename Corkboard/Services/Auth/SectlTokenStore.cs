using System.Text;
using System.Text.Json;
using Corkboard.Shared;

namespace Corkboard.Services.Auth;

/// <summary>
///     磁盘上唯一一份 SECTL OAuth 访问/刷新令牌的持有者。
/// </summary>
/// <remarks>
///     refresh token 是一次性的，每次刷新都会轮换，因此更新必须先落盘再被调用方使用，而且绝不能
///     原地写入：如果在「旧 refresh token 已失效」与「新 refresh token 已持久化」之间崩溃，会话就
///     永久失效。所以每次保存都先写临时文件、刷到存储设备，再用一次 rename 替换目标文件。
/// </remarks>
public sealed class SectlTokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>使用数据根 <c>data/config/sectl-auth.json</c> 的默认位置。</summary>
    public SectlTokenStore()
        : this(Utils.GetFilePath(SectlAuthEndpoints.TokenFileSegments))
    {
    }

    public SectlTokenStore(string tokenPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenPath);
        Path = System.IO.Path.GetFullPath(tokenPath);
        LockPath = Path + ".lock";
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
    }

    /// <summary>令牌文件的绝对路径。</summary>
    public string Path { get; }

    /// <summary>
    ///     与令牌文件同目录的锁文件，用来串行化多个进程对同一令牌文件的轮换。
    ///     该文件从不删除（空文件不会被误认成令牌），也不属于任何归档根，因此不会随备份一起被带走。
    /// </summary>
    public string LockPath { get; }

    /// <summary>
    ///     读取令牌。文件不存在、不可读或损坏都等价于「没有会话」：下一次成功授权会重写它，
    ///     而刷新流程绝不能基于半读出来的令牌对运行。
    /// </summary>
    public async Task<SectlToken?> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(Path))
                return null;
            var json = await File.ReadAllTextAsync(Path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<SectlToken>(json, JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    ///     原子地替换令牌文件。临时文件在 rename 之前先刷到磁盘，
    ///     这样断电也不会暴露一个被截断、或已改名但尚未写入的令牌对。
    /// </summary>
    public async Task SaveAsync(SectlToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        var directory = System.IO.Path.GetDirectoryName(Path)!;
        Directory.CreateDirectory(directory);
        var temporary = $"{Path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var content = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(token, JsonOptions));
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, Path, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    /// <summary>删除令牌文件；失败只记录在调用方的语义里——清空内存会话才是「已登出」。</summary>
    public void Delete()
    {
        try
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 清空内存会话才是登出；文件被占用只是留下一份陈旧令牌，下一次成功授权会覆盖它。
        }
    }

    /// <summary>
    ///     获取跨进程刷新锁，最多等待 <paramref name="timeout" />。另一个进程持锁或文件系统拒绝时
    ///     返回 <see langword="null" />，调用方此时只依赖进程内单飞保证一致性。
    /// </summary>
    public async Task<IDisposable?> TryAcquireLockAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            try
            {
                return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                // 其他进程持锁：等它把轮换结果落到磁盘。
                if (DateTimeOffset.UtcNow >= deadline)
                    return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
