using System.IO.Compression;

namespace Corkboard.Core.Helpers;

/// <summary>把旧日志压成 .gz 后删除原文件；失败由调用方决定是否上报。</summary>
public static class GZipHelper
{
    public static void CompressFileAndDelete(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
            return;

        var targetPath = $"{filePath}.gz";
        using (var source = File.OpenRead(filePath))
        using (var target = File.Create(targetPath))
        using (var gzip = new GZipStream(target, CompressionLevel.SmallestSize))
        {
            source.CopyTo(gzip);
        }

        File.Delete(filePath);
    }
}
