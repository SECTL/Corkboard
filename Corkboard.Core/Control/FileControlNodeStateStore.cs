using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Corkboard.Shared;
using Microsoft.Extensions.Logging;

namespace Corkboard.Core.ControlCenter;

/// <summary>
///     <c>node-state.json</c>：把「已应用的期望状态 revision」与「上次选的班」落盘。
///     <para>
///         为什么不塞进 <c>settings.json</c>：这两项是**设备运行状态**，不是用户偏好——
///         云同步会把它们自带到别的机器上（另一台机器的 revision 完全不同，带了只会互相打架）。
///         所以固定在数据根的 <c>data/config/control/node-state.json</c>。
///     </para>
///     <para>
///         读写都不抛：文件坏了按默认值继续（丢一次「自动重占班」比启动失败好得多），
///         写失败只记 warning。写入走「临时文件 + File.Move(overwrite)」原子替换。
///     </para>
/// </summary>
public sealed class FileControlNodeStateStore : IControlNodeStateStore
{
    /// <summary>文件格式版本（字段有增减时递增；读到别的版本按默认值处理）。</summary>
    public const int FormatVersion = 1;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<FileControlNodeStateStore>? _logger;
    private readonly object _gate = new();

    /// <summary>
    ///     建一个状态存储。
    /// </summary>
    /// <param name="logger">可选日志。</param>
    /// <param name="path">自定义文件路径（测试用；<c>null</c> 用数据根下的默认路径）。</param>
    public FileControlNodeStateStore(ILogger<FileControlNodeStateStore>? logger = null, string? path = null)
    {
        _logger = logger;
        FilePath = string.IsNullOrWhiteSpace(path)
            ? Utils.GetFilePath("config", "control", "node-state.json")
            : path;
    }

    /// <summary>实际落盘路径（排查与测试用）。</summary>
    public string FilePath { get; }

    /// <inheritdoc />
    public ControlNodeState Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(FilePath))
                    return ControlNodeState.Empty;

                var json = File.ReadAllText(FilePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                    return ControlNodeState.Empty;

                var file = JsonSerializer.Deserialize<StateFile>(json, JsonOptions);
                if (file is null || file.FormatVersion != FormatVersion)
                {
                    _logger?.LogWarning(
                        "集控状态文件格式不认识（期望 {Expected}，实际 {Actual}），按默认值继续",
                        FormatVersion,
                        file?.FormatVersion);
                    return ControlNodeState.Empty;
                }

                return new ControlNodeState(
                    Math.Max(0, file.AppliedStateRevision),
                    string.IsNullOrWhiteSpace(file.SelectedClassId) ? null : file.SelectedClassId.Trim(),
                    file.UpdatedAt);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                           or NotSupportedException or ArgumentException)
            {
                // 状态文件坏了不该挡住启动：丢掉它、当作「没应用过任何状态」。
                _logger?.LogWarning(ex, "集控状态文件读取失败，按默认值继续：{Path}", FilePath);
                return ControlNodeState.Empty;
            }
        }
    }

    /// <inheritdoc />
    public void Save(ControlNodeState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (_gate)
        {
            try
            {
                var file = new StateFile
                {
                    FormatVersion = FormatVersion,
                    AppliedStateRevision = Math.Max(0, state.AppliedStateRevision),
                    SelectedClassId = string.IsNullOrWhiteSpace(state.SelectedClassId)
                        ? null
                        : state.SelectedClassId.Trim(),
                    UpdatedAt = state.UpdatedAt == default ? DateTimeOffset.UtcNow : state.UpdatedAt
                };

                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temp, JsonSerializer.Serialize(file, JsonOptions), new UTF8Encoding(false));
                    File.Move(temp, FilePath, true);
                }
                finally
                {
                    TryDelete(temp);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // 状态只是「下次启动更省事」，写不进去不能影响当前会话。
                _logger?.LogWarning(ex, "集控状态文件写入失败：{Path}", FilePath);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时文件删不掉无所谓，下次换名字再写。
        }
    }

    /// <summary>落盘形状（字段名走 snake_case，与其它数据文件一致）。</summary>
    private sealed class StateFile
    {
        public int FormatVersion { get; set; } = FileControlNodeStateStore.FormatVersion;
        public long AppliedStateRevision { get; set; }
        public string? SelectedClassId { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
