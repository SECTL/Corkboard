using Corkboard.Core.Homework;
using Corkboard.Core.Notes;

namespace Corkboard.Core.Board;

/// <summary>
///     作业板文件监视的「哪些文件的变动值得重读板子」判断。
///     <para>
///         只认三类：作业文件（<c>.cbnote</c>，见 <see cref="NoteFileFormat" />）、旧版「一天一个」的
///         <c>notes.json</c>、以及科目与手动排序的定义 <c>board.json</c>。
///         旧版回收目录 <see cref="NoteStore.LegacyArchiveDirectoryName" /> 整棵子树、写盘临时文件
///         （<c>*.tmp</c>）都不算——那些要么不进板子，要么只是原子替换的中间态。
///     </para>
///     <para>
///         判断放在 Core 是为了能单测；应用层的
///         <c>Corkboard/Services/Board/BoardFileWatcherHostedService</c> 只负责
///         <see cref="System.IO.FileSystemWatcher" />、防抖与切到 UI 线程。
///     </para>
/// </summary>
public static class BoardFileWatchPolicy
{
    /// <summary>
    ///     这个文件是不是作业板数据：路径必须在数据根之下，且落在上面那三类里。
    ///     <para>
    ///         入参可以是 <see cref="System.IO.FileSystemWatcher" /> 给的全路径；根目录带不带结尾分隔符都行。
    ///         解析不了（空串、根路径、越出数据根）一律返回 <see langword="false" />，绝不抛异常。
    ///     </para>
    /// </summary>
    public static bool IsWatchedFile(string? rootDirectory, string? path)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || string.IsNullOrWhiteSpace(path))
            return false;

        string relative;
        try
        {
            relative = Path.GetRelativePath(rootDirectory, path);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (string.IsNullOrEmpty(relative)
            || relative == "."
            || relative.StartsWith("..", StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            return false;
        }

        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var segment in segments)
        {
            if (string.Equals(segment, NoteStore.LegacyArchiveDirectoryName, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        var fileName = segments[^1];

        // 原子写盘的中间态（`{文件名}.{guid}.tmp`）出现又消失不代表内容变了。
        if (fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            return false;

        if (fileName.EndsWith(NoteFileFormat.FileExtension, StringComparison.OrdinalIgnoreCase))
            return true;

        // 旧版「一天一个」的 notes.json 在日期目录里；board.json 是科目与手动排序，只认数据根那一份。
        if (string.Equals(fileName, NoteStore.NotesFileName, StringComparison.OrdinalIgnoreCase))
            return true;

        return segments.Length == 1
               && string.Equals(fileName, HomeworkConfig.StorageFileName, StringComparison.OrdinalIgnoreCase);
    }
}