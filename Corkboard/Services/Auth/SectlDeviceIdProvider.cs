using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Corkboard.Shared;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.Auth;

/// <summary>
///     设备标识提供方。SECTL 的令牌交换与刷新都会带上 <c>device_uuid</c>，
///     服务端据此把会话与设备关联起来。
/// </summary>
public interface IDeviceIdProvider
{
    /// <summary>返回本机稳定的设备标识；第一次调用时生成并持久化。</summary>
    Guid GetOrCreate();
}

/// <summary>
///     把设备标识保存为数据根下 <c>data/config/sectl-device.json</c> 的单字段文件。
/// </summary>
/// <remarks>
///     自包含实现，不依赖任何配置模型：文件损坏或不可写时退回「本次运行内稳定」的新标识，
///     宁可让服务端看到一个新设备，也不能让登录流程因为读不到配置而失败。
/// </remarks>
public sealed class SectlDeviceIdProvider : IDeviceIdProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _gate = new();
    private readonly ILogger<SectlDeviceIdProvider> _logger;
    private Guid? _deviceId;
    private bool _loadAttempted;

    public SectlDeviceIdProvider(ILogger<SectlDeviceIdProvider> logger)
        : this(Utils.GetFilePath(SectlAuthEndpoints.DeviceIdFileSegments), logger)
    {
    }

    public SectlDeviceIdProvider(string deviceFilePath, ILogger<SectlDeviceIdProvider> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceFilePath);
        DeviceFilePath = Path.GetFullPath(deviceFilePath);
        _logger = logger;
    }

    /// <summary>设备标识文件的绝对路径。</summary>
    public string DeviceFilePath { get; }

    public Guid GetOrCreate()
    {
        lock (_gate)
        {
            if (_deviceId is { } cached)
                return cached;

            if (!_loadAttempted)
            {
                _loadAttempted = true;
                if (TryLoad() is { } loaded && loaded != Guid.Empty)
                {
                    _deviceId = loaded;
                    return loaded;
                }
            }

            var created = Guid.NewGuid();
            _deviceId = created;
            Persist(created);
            return created;
        }
    }

    private Guid? TryLoad()
    {
        try
        {
            if (!File.Exists(DeviceFilePath))
                return null;
            var json = File.ReadAllText(DeviceFilePath);
            var record = JsonSerializer.Deserialize<SectlDeviceIdRecord>(json, JsonOptions);
            return record?.DeviceUuid;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(exception, "读取 SECTL 设备标识失败，本次运行将使用新的设备标识。");
            return null;
        }
    }

    private void Persist(Guid deviceId)
    {
        try
        {
            var directory = Path.GetDirectoryName(DeviceFilePath)!;
            Directory.CreateDirectory(directory);

            // 与令牌一样先写临时文件再改名：读到半个文件比读不到更糟。
            var temporary = $"{DeviceFilePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                var content = Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(new SectlDeviceIdRecord(deviceId), JsonOptions));
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(content, 0, content.Length);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporary, DeviceFilePath, overwrite: true);
            }
            catch
            {
                TryDelete(temporary);
                throw;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "保存 SECTL 设备标识失败，本次运行只在内存中使用该标识。");
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

    private sealed record SectlDeviceIdRecord([property: JsonPropertyName("device_uuid")] Guid DeviceUuid);
}
