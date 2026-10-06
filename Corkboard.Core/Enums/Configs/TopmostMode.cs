namespace Corkboard.Core.Enums.Configs;

/// <summary>
///     主窗口置顶模式。上游还有一个 Windows 专用的 UIAccess 模式（需要签名与提权），
///     骨架阶段先保留普通置顶。
/// </summary>
public enum TopmostMode
{
    None,
    Always
}
