using System.IO;

namespace Corkboard.Core.DutyRoster.Transfer;

/// <summary>云传输的体积上限（与上游 SecRandom 的 <c>SyncTransferLimits</c> 一致）。</summary>
public static class DutyTransferLimits
{
    /// <summary>信封 JSON（Base64 后）与解密后明文的共同上限：1 MiB。</summary>
    public const int MaxPayloadBytes = 1024 * 1024;

    public static void EnsurePayloadSize(long length, string payloadName)
    {
        if (length < 0 || length > MaxPayloadBytes)
            throw new InvalidDataException($"{payloadName} 超过 1 MiB 传输上限");
    }
}