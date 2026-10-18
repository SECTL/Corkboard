namespace Corkboard.Platforms;

using Corkboard.Platforms.Abstractions;

/// <summary>
///     没有指针定位实现的兜底：永远报「拿不到」，调用方退回兜底位置（托盘菜单不能因此打不开）。
/// </summary>
public sealed class UnsupportedPointerLocator : IPointerLocator
{
    public static UnsupportedPointerLocator Instance { get; } = new();

    private UnsupportedPointerLocator()
    {
    }

    public bool TryGetCursorPosition(out ScreenPoint position)
    {
        position = default;
        return false;
    }
}
