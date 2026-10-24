using System.Globalization;
using System.IO;
using Corkboard.Shared;

namespace Corkboard.Core.Board.AutoSave;

/// <summary>
///     自动存档的一张图：文件名、它代表哪一天、以及落盘时间（用来在同一天里挑「最后一份」）。
/// </summary>
/// <param name="FileName">文件名（不含目录），形如 <c>board-2026-10-24.png</c>。</param>
/// <param name="Date">这张图代表的那一天（从文件名里解析出来）。</param>
/// <param name="LastWriteTimeUtc">文件最后写入时间（UTC）。文件名一样新时用它判先后。</param>
public readonly record struct BoardAutoSaveFile(string FileName, DateOnly Date, DateTime LastWriteTimeUtc);

/// <summary>
///     自动存档的默认值、取值范围、文件命名与裁剪规则——**全是纯函数**，没有 IO。
///     <para>
///         设置页控件的上下限、后台服务的夹取、裁剪时「哪些该删」都取自这一份：
///         两处各写一套的话，很容易出现「界面允许填 0 天、判定时又被夹回 1」这种对不上的情况。
///     </para>
///     <para>
///         文件名的日期一律用 <see cref="CultureInfo.InvariantCulture" /> 格式化，
///         与导出文件名（<c>ExportFileNameBuilder</c>）同一个理由：文件名是给人翻的，跨机器看起来该一样。
///     </para>
/// </summary>
public static class BoardAutoSavePolicy
{
    /// <summary>
    ///     默认目录名：<c>data/autosave</c>，就在软件自己的数据目录底下。
    ///     用户可以改到别处（比如桌面），改了就用改的那个。
    /// </summary>
    public const string DefaultFolderName = "autosave";

    /// <summary>文件名前缀：两种时机共用，认文件时只认带这个前缀的。</summary>
    public const string FileNamePrefix = "board-";

    /// <summary>只带日期的文件名格式（「每天一张」用它，同一天反复保存互相覆盖）。</summary>
    public const string DailyFileNameFormat = "yyyy-MM-dd";

    /// <summary>带时分秒的文件名格式（「每次修改都存一张」用它）。</summary>
    public const string TimestampFileNameFormat = "yyyy-MM-dd_HH-mm-ss";

    /// <summary>保存出来的图片格式（固定 PNG，与导出图片一致）。</summary>
    public const string FileExtension = "png";

    /// <summary>保留天数的默认值：一周的板子够回看了，再多只是占地方。</summary>
    public const int DefaultRetentionDays = 7;

    public const int MinRetentionDays = 1;
    public const int MaxRetentionDays = 365;

    /// <summary>把设置页输入的「天」夹进合法区间。</summary>
    public static int ClampRetentionDays(int days) => Math.Clamp(days, MinRetentionDays, MaxRetentionDays);

    /// <summary>
    ///     默认存档目录的绝对路径（<c>data/autosave</c>）。**只算路径、不建目录**：
    ///     默认关闭时不该在磁盘上留下空文件夹，建目录留给真正要写图的那一刻。
    /// </summary>
    public static string ResolveDefaultFolder() => Path.Combine(Utils.DataRoot, DefaultFolderName);

    /// <summary>
    ///     实际要用的目录：用户填了就用用户的（去掉首尾空白与包裹的引号），没填就回退默认目录。
    ///     用户可能手输一个相对路径，这里一律展开成绝对路径，免得「相对于哪个目录」说不清。
    /// </summary>
    public static string ResolveFolder(string? configured)
    {
        var trimmed = NormalizeFolder(configured);
        return trimmed.Length == 0 ? ResolveDefaultFolder() : trimmed;
    }

    /// <summary>
    ///     把用户填的目录收拾成能用的样子：去空白、去包裹的引号、展开成绝对路径。
    ///     空输入返回空串（调用方据此回退默认目录）。**不碰磁盘**，路径合不合法交给写文件那一步。
    /// </summary>
    public static string NormalizeFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return string.Empty;

        var trimmed = folder.Trim().Trim('"', '\'').Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // 手输的非法路径（带非法字符之类）：原样交回去，让写文件那一步报错并记日志，
            // 而不是在设置页里悄悄改成默认目录——那样用户会以为自己的设置生效了。
            return trimmed;
        }
    }

    /// <summary>
    ///     某一天那一张图的文件名（不含目录）。
    ///     <para>
    ///         「每天一张」只带日期，同一天反复保存写的是同一个文件；
    ///         「每次修改都存一张」带时分秒，每次都是新文件。
    ///     </para>
    /// </summary>
    /// <param name="date">这张图代表的那一天。</param>
    /// <param name="mode">保存时机。</param>
    /// <param name="now">
    ///     「每次修改都存一张」时用来取时分秒的当前时刻；「每天一张」用不到，可以不给。
    /// </param>
    public static string BuildFileName(DateOnly date, BoardAutoSaveMode mode, DateTime? now = null)
    {
        if (mode == BoardAutoSaveMode.Daily)
            return $"{FileNamePrefix}{date.ToString(DailyFileNameFormat, CultureInfo.InvariantCulture)}.{FileExtension}";

        var stamp = (now ?? DateTime.Now).ToString(TimestampFileNameFormat, CultureInfo.InvariantCulture);
        return $"{FileNamePrefix}{stamp}.{FileExtension}";
    }

    /// <summary>
    ///     从文件名里认出它代表哪一天。认不出（不是我们的命名、或日期是假的）返回 <c>null</c>——
    ///     用户自己往这个目录里丢的图片不该被当成存档删掉。
    /// </summary>
    public static DateOnly? TryParseDate(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var name = Path.GetFileName(fileName.Trim());
        if (!name.StartsWith(FileNamePrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!name.EndsWith("." + FileExtension, StringComparison.OrdinalIgnoreCase))
            return null;

        var stem = name[FileNamePrefix.Length..];
        stem = stem[..^("." + FileExtension).Length];

        // 「每天一张」只有日期；「每次修改都存一张」后面还跟着 _HH-mm-ss。
        var datePart = stem.Length >= DailyFileNameFormat.Length ? stem[..DailyFileNameFormat.Length] : stem;

        return DateOnly.TryParseExact(datePart, DailyFileNameFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// <summary>
    ///     算出这一轮该删掉哪些文件（只返回文件名，不删）。
    ///     <para>
    ///         两条规则：<list type="number">
    ///             <item>代表的那一天早于「今天往前数 N−1 天」的一律删（N = 保留天数，1 就是只留今天）；</item>
    ///             <item>「每天一张」模式下同一天只留最后一份——从「每次修改都存一张」切过来时，
    ///                 那天可能已经攒了一堆带时分秒的图，切成每天一张后该收敛回一份，
    ///                 否则「每天只保留最后一份」这句承诺就是假的。</item>
    ///         </list>
    ///     </para>
    ///     <para>不是我们命名的文件（<see cref="TryParseDate" /> 认不出）不会出现在结果里，一个都不会被删。</para>
    /// </summary>
    /// <param name="files">目录里现有的文件（<see cref="BoardAutoSaveFile.Date" /> 已解析好）。</param>
    /// <param name="today">今天（调用方给，测试才不依赖本机时钟）。</param>
    /// <param name="retentionDays">保留天数，内部会夹一次。</param>
    /// <param name="mode">保存时机；「每天一张」时同一日期只留一份。</param>
    public static IReadOnlyList<string> SelectExpired(
        IEnumerable<BoardAutoSaveFile> files,
        DateOnly today,
        int retentionDays,
        BoardAutoSaveMode mode)
    {
        ArgumentNullException.ThrowIfNull(files);

        var days = ClampRetentionDays(retentionDays);
        var cutoff = today.AddDays(-(days - 1));

        var expired = new List<string>();
        var kept = new List<BoardAutoSaveFile>();

        foreach (var file in files)
        {
            if (file.Date < cutoff)
                expired.Add(file.FileName);
            else
                kept.Add(file);
        }

        // 每天只留最后一份：同一天里按「写入时间 → 是不是带时分秒 → 文件名」挑出最新的那张，其余都删。
        if (mode == BoardAutoSaveMode.Daily)
        {
            foreach (var group in kept.GroupBy(file => file.Date))
            {
                var newest = group
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    // 时间戳打平时（某些文件系统只精确到两秒）优先留「只带日期」的那个：
                    // 它才是这个模式下刚写出来的正主，留着带时分秒的旧文件反而会把新的删掉。
                    .ThenBy(file => HasTimestamp(file.FileName))
                    .ThenByDescending(file => file.FileName, StringComparer.Ordinal)
                    .First();

                foreach (var file in group)
                    if (!string.Equals(file.FileName, newest.FileName, StringComparison.Ordinal))
                        expired.Add(file.FileName);
            }
        }

        return expired;
    }

    /// <summary>文件名里除了日期还带了时分秒（「每次修改都存一张」的命名）。</summary>
    private static bool HasTimestamp(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (!name.StartsWith(FileNamePrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var stem = name[FileNamePrefix.Length..];
        if (stem.EndsWith("." + FileExtension, StringComparison.OrdinalIgnoreCase))
            stem = stem[..^("." + FileExtension).Length];

        return stem.Length > DailyFileNameFormat.Length;
    }

    /// <summary>
    ///     目录里那些图占了多大（字节）。只统计认得出的存档文件，用户自己丢进去的图片不算在内。
    ///     给设置页显示「现在占了多少」用。
    /// </summary>
    public static long TotalBytes(IEnumerable<(string FileName, long Length)> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        long total = 0;
        foreach (var (fileName, length) in files)
            if (TryParseDate(fileName) is not null && length > 0)
                total += length;

        return total;
    }
}
