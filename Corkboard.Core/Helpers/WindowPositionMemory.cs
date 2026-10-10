using Avalonia;

namespace Corkboard.Core.Helpers;

/// <summary>
///     主窗口位置记忆的纯计算部分：把「上次位置」换算到当前屏幕，并把位置夹回可见区。
///     <para>
///         位置按「相对屏幕工作区左上角的偏移（设备像素）」保存，同时记下保存那一刻的工作区大小。
///         屏幕尺寸变了就按工作区比例把偏移等比缩过去（换分辨率、改 DPI 缩放都走这条路），
///         面板于是落在新屏幕上相对原来的位置。
///     </para>
///     <para>
///         只管算，不碰窗口与 <c>Screens</c>：取当前工作区、判断位置落在哪块屏幕都留给调用方，
///         这一层因此可以直接单测（见 <c>WindowPositionMemoryTests</c>）。
///     </para>
/// </summary>
public static class WindowPositionMemory
{
    /// <summary>「还没记过位置」的哨兵值。窗口位置允许是负数（副屏在主屏左侧），不能用 0 表示没记过。</summary>
    public const int Unset = int.MinValue;

    /// <summary>
    ///     位置可信的下限。Windows 把最小化窗口的矩形挪到 (-32000, -32000)，而这个占位值会在
    ///     <c>WindowState</c> 更新之前先经 <c>PositionChanged</c> 冒出来（实测能写进配置），
    ///     记下去就等于把「上次位置」丢了。真机上屏幕左上角不可能到 -30000 以左（副屏在左侧也远没这么远）。
    /// </summary>
    public const int MinPlausibleOffset = -30000;

    /// <summary>这个位置是不是真的落在屏幕上：最小化占位坐标（≤ <see cref="MinPlausibleOffset" />）不算。</summary>
    public static bool IsPlausible(PixelPoint position) =>
        position.X > MinPlausibleOffset && position.Y > MinPlausibleOffset;

    /// <summary>夹位置时至少留多少设备像素在可见区里（标题栏那一角要抓得到）。</summary>
    public const int VisibleEdge = 160;

    /// <summary>
    ///     把保存的偏移换算成当前屏幕工作区上的绝对位置。
    ///     没记过位置（<see cref="Unset" />）或当前工作区无效时返回 <c>null</c>，调用方按「没有位置可恢复」处理。
    ///     <para>
    ///         保存时没记屏幕大小（&lt;= 0）就不缩放，直接按偏移摆——宁可位置偏一点，也不要拿一个瞎猜的比例去缩。
    ///     </para>
    /// </summary>
    public static PixelPoint? Resolve(
        int offsetX,
        int offsetY,
        int savedScreenWidth,
        int savedScreenHeight,
        PixelRect currentWorkingArea)
    {
        // Unset（int.MinValue）与最小化占位坐标都当成「没有位置可恢复」。
        if (offsetX <= MinPlausibleOffset || offsetY <= MinPlausibleOffset)
            return null;

        if (currentWorkingArea.Width <= 0 || currentWorkingArea.Height <= 0)
            return null;

        var scaleX = savedScreenWidth > 0 ? (double)currentWorkingArea.Width / savedScreenWidth : 1d;
        var scaleY = savedScreenHeight > 0 ? (double)currentWorkingArea.Height / savedScreenHeight : 1d;

        return new PixelPoint(
            currentWorkingArea.X + (int)Math.Round(offsetX * scaleX),
            currentWorkingArea.Y + (int)Math.Round(offsetY * scaleY));
    }

    /// <summary>
    ///     把位置夹进工作区：至少留 <see cref="VisibleEdge" /> 设备像素在可见区里，
    ///     窗口既不会整个跑到屏幕外，也不会钻到工作区上方（标题栏被任务栏或屏幕边缘吃掉）。
    /// </summary>
    public static PixelPoint Clamp(PixelPoint position, PixelRect workingArea)
    {
        var maxX = workingArea.X + Math.Max(0, workingArea.Width - VisibleEdge);
        var maxY = workingArea.Y + Math.Max(0, workingArea.Height - VisibleEdge);

        return new PixelPoint(
            Math.Clamp(position.X, workingArea.X, maxX),
            Math.Clamp(position.Y, workingArea.Y, maxY));
    }

    /// <summary>把窗口当前的绝对位置换算成相对主屏工作区的偏移，供落盘。</summary>
    public static (int OffsetX, int OffsetY) ToOffset(PixelPoint position, PixelRect workingArea) =>
        (position.X - workingArea.X, position.Y - workingArea.Y);
}
