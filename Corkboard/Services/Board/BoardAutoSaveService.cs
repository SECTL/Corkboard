using System.IO;
using Avalonia.Threading;
using Corkboard.Core.Board;
using Corkboard.Core.Board.AutoSave;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Services.Config;
using Microsoft.Extensions.Logging;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Services.Board;

/// <summary>
///     作业板自动存档：板子有改动之后等几秒，把整块板面渲成一张 PNG 存进指定目录，
///     再按「保留天数」把过期的删掉。
///     <para>
///         为什么是「改完等几秒」而不是「到点就存」：板子是随时在改的（拖宽拖高、改内容、改科目），
///         按固定间隔存会存下一堆毫无意义的中间态；等改动停下来再存，一次改动只落一张图。
///         另外还有一个十分钟一次的小巡检，只做一件事：<b>「每天一张」模式下今天还没有图就补一张</b>
///         ——板子一整天没动也该有当天的那一张，否则「每天保留最后一份」在没改动的日子就断档了。
///     </para>
///     <para>
///         渲染由页面提供（<see cref="RegisterRenderer" />）：只有页面拿得到那块内容容器，
///         也才需要在渲染期间把卡片上的编辑 / 删除按钮收起来（与手动导出同一套做法）。
///         页面不在可视树里时（还没显示出来）没有渲染器，这一轮就跳过、不算失败。
///     </para>
///     <para>
///         本服务是单例，订阅的是单例（<see cref="IBoardService" /> 与配置句柄），生命周期与进程一样长，
///         不需要断开订阅；唯一的例外是页面注册的渲染器，页面卸载时会自己注销。
///     </para>
/// </summary>
public sealed class BoardAutoSaveService
{
    /// <summary>
    ///     改动之后等多久才存（防抖）。和云同步那边作业板备份的窗口一致：
    ///     短到用户感觉不到，长到一次连续编辑只落一张图。
    /// </summary>
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(5);

    /// <summary>补「今天还没有图」的巡检间隔。</summary>
    private static readonly TimeSpan CatchUpInterval = TimeSpan.FromMinutes(10);

    /// <summary>启动之后先等一会儿再巡检：别跟开机时的窗口初始化抢渲染。</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    private readonly MainConfigHandler _configHandler;
    private readonly ILogger<BoardAutoSaveService> _logger;

    private CancellationTokenSource? _debounce;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    /// <summary>
    ///     正在存这一张。防抖、巡检、设置页的「立即保存一张」三条路都可能同时叫到
    ///     <see cref="SaveNowAsync" />：渲染要在 UI 线程上把卡片按钮收起来再放回去，
    ///     两次叠在一起会互相把状态提前还原，所以一次只让一张图走完。
    /// </summary>
    private bool _isSaving;

    /// <summary>页面注册进来的渲染器；页面不在可视树里时为 <c>null</c>。</summary>
    private Func<CancellationToken, Task<byte[]?>>? _renderer;

    private BoardAutoSaveSettingsConfig? _settings;

    public BoardAutoSaveService(
        MainConfigHandler configHandler,
        IBoardService boardService,
        ILogger<BoardAutoSaveService> logger)
    {
        _configHandler = configHandler;
        _logger = logger;

        boardService.Changed += OnBoardChanged;
        configHandler.Reloaded += OnConfigReloaded;
        SubscribeSettings();

        StartLoop();
    }

    /// <summary>上一次成功存档的时间；没存过为 <c>null</c>（设置页据此显示「还没存过」）。</summary>
    public DateTimeOffset? LastSavedAt { get; private set; }

    /// <summary>上一次成功存档的完整路径；没存过为 <c>null</c>。</summary>
    public string? LastSavedPath { get; private set; }

    /// <summary>上一次失败的原因（给人看的短句）；成功一次就清空。设置页用它显示状态。</summary>
    public string? LastError { get; private set; }

    /// <summary>存档状态变了（成功 / 失败 / 目录改了），设置页订阅它刷新那几行文字。</summary>
    public event EventHandler? StatusChanged;

    /// <summary>现在生效的存档目录（绝对路径）。</summary>
    public string Folder => _configHandler.Data.BoardAutoSave.ResolveFolder();

    /// <summary>存档目录里现在有几张图、占了多少字节（只算我们自己的命名，用户放在这儿的别的图不算）。</summary>
    public (int Count, long Bytes) GetStatistics()
    {
        try
        {
            var folder = Folder;
            if (!Directory.Exists(folder))
                return (0, 0);

            var files = new List<(string, long)>();
            foreach (var path in Directory.EnumerateFiles(folder, "*.png"))
            {
                var name = Path.GetFileName(path);
                if (BoardAutoSavePolicy.TryParseDate(name) is null)
                    continue;

                files.Add((name, new FileInfo(path).Length));
            }

            return (files.Count, BoardAutoSavePolicy.TotalBytes(files));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 统计只是给界面看的，读不了就当零，不要因此报错。
            _logger.LogDebug(exception, "统计自动存档目录失败");
            return (0, 0);
        }
    }

    /// <summary>
    ///     页面注册渲染器（页面加载时调）。传 <c>null</c> 表示注销（页面卸载时调）。
    ///     <para>
    ///         渲染器必须自己保证在 UI 线程上跑、并在渲染期间把卡片上的按钮收起来；
    ///         返回 <c>null</c> 表示「这一轮没什么可存的」（比如板子是空的、正在回放）。
    ///     </para>
    /// </summary>
    public void RegisterRenderer(Func<CancellationToken, Task<byte[]?>>? renderer)
    {
        _renderer = renderer;
    }

    /// <summary>
    ///     板子改了就排一次存档。改得再密也只有最后一笔算数（防抖）。
    /// </summary>
    public void Schedule()
    {
        if (!_configHandler.Data.BoardAutoSave.Enabled)
            return;

        _debounce?.Cancel();
        _debounce?.Dispose();
        var cts = new CancellationTokenSource();
        _debounce = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceWindow, cts.Token).ConfigureAwait(false);

                // 这一句会等里面的异步活干完（InvokeAsync 对 Func<Task<T>> 是解包语义），
                // 所以存档里的异常会被下面的 catch 接住，不会变成没人观察的任务异常。
                await Dispatcher.UIThread.InvokeAsync(() => SaveNowAsync(manual: false),
                    DispatcherPriority.Background).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "排队的作业板自动存档失败");
            }
        }, CancellationToken.None);
    }

    /// <summary>
    ///     立刻存一张（设置页的「立即保存」也走它）。返回是否真的写了文件。
    ///     <para>必须在 UI 线程上调用：渲染要用到界面控件。</para>
    /// </summary>
    public async Task<bool> SaveNowAsync(bool manual)
    {
        var settings = _configHandler.Data.BoardAutoSave;
        if (!manual && !settings.Enabled)
            return false;

        if (_renderer is not { } renderer)
        {
            _logger.LogDebug("作业板还没显示出来，这一轮自动存档跳过");
            return false;
        }

        // 上一张还在渲：这一轮直接跳过。改动还在，下一次改动会重新排队；
        // 硬挤进来只会两张图抢同一份「临时收起按钮」的状态。
        if (_isSaving)
        {
            _logger.LogDebug("上一张自动存档还没写完，这一轮跳过");
            return false;
        }

        _isSaving = true;
        try
        {
            return await SaveCoreAsync(settings, renderer).ConfigureAwait(true);
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task<bool> SaveCoreAsync(
        BoardAutoSaveSettingsConfig settings,
        Func<CancellationToken, Task<byte[]?>> renderer)
    {
        var folder = settings.ResolveFolder();
        var now = DateTime.Now;
        var mode = settings.Mode;

        byte[]? png;
        try
        {
            png = await renderer(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            Report(CR.Settings_Board_AutoSave_Failed, exception, "渲染作业板图片失败");
            return false;
        }

        if (png is null || png.Length == 0)
        {
            // 板子上没作业 / 正在回放：不算失败，也不该覆盖已有的图。
            _logger.LogDebug("作业板这一轮没有可存的内容，自动存档跳过");
            return false;
        }

        var path = Path.Combine(folder, BoardAutoSavePolicy.BuildFileName(DateOnly.FromDateTime(now), mode, now));

        try
        {
            Directory.CreateDirectory(folder);
            await WriteAtomicallyAsync(path, png).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Report(CR.Settings_Board_AutoSave_Failed, exception, $"写入自动存档失败：{path}");
            return false;
        }

        LastSavedAt = DateTimeOffset.Now;
        LastSavedPath = path;
        LastError = null;
        StatusChanged?.Invoke(this, EventArgs.Empty);
        _logger.LogInformation("作业板已自动存档：{Path}", path);

        Prune(folder, DateOnly.FromDateTime(now), settings.RetentionDays, mode);
        return true;
    }

    #region 目录与裁剪

    /// <summary>按保留天数删掉过期的图；删不掉只记日志，不影响这一次保存已经成功的事实。</summary>
    private void Prune(string folder, DateOnly today, int retentionDays, BoardAutoSaveMode mode)
    {
        try
        {
            var files = new List<BoardAutoSaveFile>();
            foreach (var path in Directory.EnumerateFiles(folder, "*.png"))
            {
                var name = Path.GetFileName(path);
                if (BoardAutoSavePolicy.TryParseDate(name) is not { } date)
                    continue;

                files.Add(new BoardAutoSaveFile(name, date, File.GetLastWriteTimeUtc(path)));
            }

            var expired = BoardAutoSavePolicy.SelectExpired(files, today, retentionDays, mode);
            foreach (var name in expired)
            {
                try
                {
                    File.Delete(Path.Combine(folder, name));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(exception, "删掉过期的自动存档失败：{Name}", name);
                }
            }

            if (expired.Count > 0)
                _logger.LogInformation("清掉了 {Count} 份过期的作业板存档", expired.Count);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "清理自动存档目录失败");
        }
    }

    /// <summary>先写临时文件再换名：进程中途被杀也不会留下半张打不开的图。</summary>
    private static async Task WriteAtomicallyAsync(string path, byte[] bytes)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes).ConfigureAwait(true);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // 临时文件删不掉不影响这次保存。
            }
        }
    }

    #endregion

    #region 触发源

    private void OnBoardChanged(object? sender, EventArgs e) => Schedule();

    private void OnConfigReloaded(object? sender, EventArgs e) => SubscribeSettings();

    /// <summary>
    ///     订阅配置：从云端恢复设置会把 <see cref="MainConfigHandler.Data" /> 整个换掉，
    ///     旧的子配置对象已经不归这份配置管了，得重新挂。
    /// </summary>
    private void SubscribeSettings()
    {
        var settings = _configHandler.Data.BoardAutoSave;
        if (ReferenceEquals(_settings, settings))
            return;

        if (_settings is not null)
            _settings.PropertyChanged -= OnSettingsChanged;

        _settings = settings;
        settings.PropertyChanged += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            // 刚打开开关：立刻补一张，别等用户下次改动。
            case nameof(BoardAutoSaveSettingsConfig.Enabled):
                if (_configHandler.Data.BoardAutoSave.Enabled)
                    Schedule();
                break;

            // 换了目录 / 换了时机：这一天该有的图可能还没有（新目录是空的），排一次。
            case nameof(BoardAutoSaveSettingsConfig.Folder):
            case nameof(BoardAutoSaveSettingsConfig.Mode):
                Schedule();
                break;
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region 巡检（每天一张的补档）

    private void StartLoop()
    {
        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunLoopAsync(_loopCts.Token), CancellationToken.None);
    }

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                await CatchUpAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(CatchUpInterval, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "作业板自动存档巡检失败");
        }
    }

    /// <summary>
    ///     巡检只补「今天还没有图」这一种情况（仅「每天一张」模式）。
    ///     「每次修改都存一张」模式下没改动就不该凭空多一张，所以那种模式什么都不做。
    /// </summary>
    private async Task CatchUpAsync(CancellationToken stoppingToken)
    {
        var settings = _configHandler.Data.BoardAutoSave;
        if (!settings.Enabled || settings.Mode != BoardAutoSaveMode.Daily)
            return;

        var today = DateOnly.FromDateTime(DateTime.Now);
        var path = Path.Combine(settings.ResolveFolder(),
            BoardAutoSavePolicy.BuildFileName(today, BoardAutoSaveMode.Daily));
        if (File.Exists(path))
            return;

        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () => await SaveNowAsync(manual: false),
                DispatcherPriority.Background, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    #endregion

    /// <summary>存档失败时统一走这里：记日志、留一句给人看的原因、通知界面。</summary>
    private void Report(string message, Exception exception, string logMessage)
    {
        LastError = message;
        StatusChanged?.Invoke(this, EventArgs.Empty);
        _logger.LogError(exception, logMessage);
    }
}
