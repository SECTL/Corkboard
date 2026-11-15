using System.IO;
using Avalonia.Threading;
using Corkboard.Core.Board;
using Corkboard.Core.Notes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.Board;

/// <summary>
///     作业板文件监视：作业文件（<c>.cbnote</c>、旧版 <c>notes.json</c>）与科目定义 <c>board.json</c>
///     被外部改动后，自动让作业板从磁盘重读一遍。
///     <para>
///         为什么需要它：作业本身是磁盘上的数据文件（见 <see cref="NoteStore" />），云同步、集控下发、
///         或者用户自己拷贝替换文件都不会经过本进程的写盘通道，内存里的板子不知道发生了什么。
///         重读一次会发 <see cref="IBoardService.Changed" />，主页面与看板模式里嵌的那块
///         <c>BoardPage</c> 都订阅着它，所以两边会一起换成磁盘上的新内容。
///     </para>
///     <para>
///         ⚠️ 本服务自己也落盘（<see cref="IBoardService" /> 每次保存都会写文件），那些事件同样会打进来。
///         这里不区分「谁写的」，一律交给 <see cref="IBoardService.Reload" /> 按值比较：内容没变就早退、
///         不发通知，界面不会白刷一遍。判断哪些文件值得看的是 Core 的
///         <see cref="BoardFileWatchPolicy" />（有单测），这里只管监视、防抖与切到 UI 线程。
///     </para>
/// </summary>
public sealed class BoardFileWatcherHostedService : BackgroundService
{
    /// <summary>
    ///     连续事件攒多久再重读：一次「保存」在文件系统上往往是创建临时文件、改名、修改时间
    ///     连着好几条事件，立刻重读会读到写了一半的中间态，也会让界面连刷好几遍。
    /// </summary>
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(700);

    private readonly IBoardService _boardService;
    private readonly ILogger<BoardFileWatcherHostedService> _logger;
    private readonly object _gate = new();

    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private string? _rootDirectory;
    private bool _stopped;

    public BoardFileWatcherHostedService(
        IBoardService boardService,
        ILogger<BoardFileWatcherHostedService> logger)
    {
        _boardService = boardService;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        StartWatching();
        return WaitForStopAsync(stoppingToken);
    }

    /// <summary>
    ///     把服务挂住直到进程退出；<see cref="FileSystemWatcher" /> 自己有线程，不需要在这里轮询。
    /// </summary>
    private async Task WaitForStopAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            StopWatching();
        }
    }

    private void StartWatching()
    {
        try
        {
            // RootDirectory 会顺带把数据目录建出来（首次启动时目录可能还不存在）。
            var root = NoteStore.RootDirectory;

            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                               | NotifyFilters.LastWrite
                               | NotifyFilters.Size
                               | NotifyFilters.CreationTime,
                InternalBufferSize = 64 * 1024
            };
            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            watcher.Deleted += OnFileChanged;
            watcher.Renamed += OnFileRenamed;
            watcher.Error += OnWatcherError;

            lock (_gate)
            {
                if (_stopped)
                {
                    watcher.Dispose();
                    return;
                }

                _rootDirectory = root;
                _watcher = watcher;
                _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
            }

            watcher.EnableRaisingEvents = true;
            _logger.LogDebug("Watching board files under {Directory}", root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 监视不起来不该拖垮启动：外部改动就退回「下次启动才看得到」，其余功能照常。
            _logger.LogWarning(exception, "Could not watch board files; external changes will not refresh the board");
        }
    }

    private void StopWatching()
    {
        lock (_gate)
        {
            _stopped = true;

            if (_debounceTimer is { } timer)
            {
                _debounceTimer = null;
                timer.Dispose();
            }

            if (_watcher is { } watcher)
            {
                _watcher = null;
                watcher.EnableRaisingEvents = false;
                watcher.Changed -= OnFileChanged;
                watcher.Created -= OnFileChanged;
                watcher.Deleted -= OnFileChanged;
                watcher.Renamed -= OnFileRenamed;
                watcher.Error -= OnWatcherError;
                watcher.Dispose();
            }
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (IsWatched(e.FullPath))
            ScheduleReload();
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        // 改名两边都要看：旧路径消失（作业被移走）与新路径出现（作业被放进来）都算内容变了。
        if (IsWatched(e.OldFullPath) || IsWatched(e.FullPath))
            ScheduleReload();
    }

    private bool IsWatched(string path)
    {
        var root = _rootDirectory;
        return root is not null && BoardFileWatchPolicy.IsWatchedFile(root, path);
    }

    /// <summary>把防抖窗口往后推：窗口内又来了事件就继续等，事件停下来才重读一次。</summary>
    private void ScheduleReload()
    {
        lock (_gate)
        {
            // 只可能因为 FileSystemWatcher 的 Error 走到这里；其余情况都在触发后 700ms 内。
            _debounceTimer?.Change(DebounceWindow, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnDebounceElapsed(object? state)
    {
        lock (_gate)
        {
            if (_stopped)
                return;
        }

        try
        {
            // 重读要动 ObservableCollection（作业板列表绑着它），必须在 UI 线程上跑。
            Dispatcher.UIThread.Post(ReloadOnUiThread, DispatcherPriority.Background);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            // Dispatcher 已经在关闭（应用正在退出）：这一轮不刷了。
            _logger.LogDebug(exception, "Board file watcher could not post the reload to the UI thread");
        }
    }

    private void ReloadOnUiThread()
    {
        lock (_gate)
        {
            if (_stopped)
                return;
        }

        try
        {
            if (_boardService.Reload())
                _logger.LogInformation("Board reloaded because its files changed on disk");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Board files changed on disk but reloading them failed");
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // 事件缓冲区溢出时，中间那几条通知已经丢了——干脆整份重读一次，保证界面与磁盘一致。
        _logger.LogWarning(e.GetException(), "Board file watcher reported an error; reloading the board");
        ScheduleReload();
    }
}