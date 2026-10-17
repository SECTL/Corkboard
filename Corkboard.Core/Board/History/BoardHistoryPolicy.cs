namespace Corkboard.Core.Board.History;

/// <summary>
///     作业历史（时空回放的数据源）的边界与默认值。
///     <para>
///         这里只有**纯常量与夹取**：设置页与存储层都从这一份取上下限，别在两边各写一套。
///         三个值都能在「设置 → 作业板 → 历史与回放」里改：
///         <list type="bullet">
///             <item>保存间隔：同一条作业连续保存，间隔小于它只记成一版（省空间）；</item>
///             <item>保留天数：超过这么多天的历史文件会被删掉；</item>
///             <item>容量上限：历史占满这么多就先删最老的那几天。</item>
///         </list>
///     </para>
/// </summary>
public static class BoardHistoryPolicy
{
    /// <summary>同一条作业连续保存的合并窗口默认值（分钟）。</summary>
    public const int DefaultCoalesceMinutes = 3;

    public const int MinCoalesceMinutes = 1;
    public const int MaxCoalesceMinutes = 10;

    /// <summary>历史保留天数的默认值。</summary>
    public const int DefaultRetentionDays = 90;

    public const int MinRetentionDays = 20;
    public const int MaxRetentionDays = 999;

    /// <summary>历史占用上限的默认值（MB）。</summary>
    public const int DefaultMaxSizeMb = 200;

    public const int MinMaxSizeMb = 20;
    public const int MaxMaxSizeMb = 2000;

    /// <summary>把设置页输入的「分钟」夹成合法的合并窗口。</summary>
    public static TimeSpan ClampCoalesceWindow(int minutes)
        => TimeSpan.FromMinutes(ClampCoalesceMinutes(minutes));

    /// <summary>把设置页输入的「分钟」夹进 [<see cref="MinCoalesceMinutes" />, <see cref="MaxCoalesceMinutes" />]。</summary>
    public static int ClampCoalesceMinutes(int minutes) => Math.Clamp(minutes, MinCoalesceMinutes, MaxCoalesceMinutes);

    /// <summary>把设置页输入的「天」夹成合法的保留天数。</summary>
    public static int ClampRetentionDays(int days) => Math.Clamp(days, MinRetentionDays, MaxRetentionDays);

    /// <summary>把设置页输入的「MB」夹成合法的容量上限。</summary>
    public static int ClampMaxSizeMb(int megabytes) => Math.Clamp(megabytes, MinMaxSizeMb, MaxMaxSizeMb);

    /// <summary>把 MB 换算成字节（存储层按字节比对）。</summary>
    public static long ToBytes(int megabytes) => (long)ClampMaxSizeMb(megabytes) * 1024 * 1024;
}
