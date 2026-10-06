using System.Text.Json;
using Microsoft.Extensions.Logging;
using Corkboard.Core.Abstraction;
using Corkboard.Shared.Abstraction;

namespace Corkboard.Core.Services.Config;

/// <summary>
///     文件配置服务：每份配置一个 JSON 文件，写入走「临时文件 + 原子替换」，
///     避免进程中途被杀留下截断的 JSON。
///     <para>
///         读取失败时返回传入的 fallback 且不重写磁盘——把坏文件留在原地才有机会被人看。
///     </para>
/// </summary>
public sealed class FileConfigService(ILogger<FileConfigService> logger) : ConfigServiceBase
{
    public override bool IsConfigExists<T>(T fallback)
    {
        var filePath = fallback.ConfigFilePath;
        logger.LogInformation("Checking config file existence: {Path}", filePath);
        return File.Exists(filePath);
    }

    public override T LoadConfig<T>(T fallback)
    {
        var filePath = fallback.ConfigFilePath;
        logger.LogInformation("Loading config file: {Path}", filePath);
        if (!File.Exists(filePath))
        {
            SaveConfig(fallback);
            return fallback;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            if (!IsPlainJsonObject(json))
                return fallback;

            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to load config file, using fallback without rewriting: {Path}", filePath);
            return fallback;
        }
    }

    public override void SaveConfig<T>(T config)
    {
        var filePath = config.ConfigFilePath;
        logger.LogInformation("Saving config file: {Path}", filePath);
        EnsureDirectory(filePath);
        var temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(temporaryPath, filePath, overwrite: true);
    }

    public override void DeleteConfig<T>(T config)
    {
        var filePath = config.ConfigFilePath;
        logger.LogInformation("Deleting config file: {Path}", filePath);
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    private static bool IsPlainJsonObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Any();
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }
}
