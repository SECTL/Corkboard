using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Corkboard.Core.Models.SubConfigs.ControlCenter;
using Corkboard.Core.Notes;

namespace Corkboard.Core.ControlCenter;

/// <summary>
///     作业自动备份的纯逻辑：什么条件下才该传、这次内容和上次成功传的那份是不是同一份。
///     <para>
///         这里不碰网络、不碰 UI 线程，也不记状态——「什么时候调度、传完怎么报」都留在应用层的
///         <c>ControlCenterHostedService</c>；抽出来只为了让「触发条件」与「指纹」能单测。
///     </para>
/// </summary>
public static class ControlHomeworkBackup
{
    /// <summary>
    ///     是否具备上传条件（全部满足才传）：集控开关 + 自动备份开关 + 完整凭证 +
    ///     连接在线 + 客户端已经建出来。
    /// </summary>
    /// <param name="config">当前集控配置。</param>
    /// <param name="credential">当前设备凭证（可能还没读盘，为 <see langword="null" />）。</param>
    /// <param name="snapshot">当前连接状态。</param>
    /// <param name="clientPresent">托管服务手里是否已经有客户端对象。</param>
    public static bool ShouldUpload(
        ControlCenterSettingsConfig? config,
        ControlNodeCredential? credential,
        ControlNodeLinkState snapshot,
        bool clientPresent) =>
        clientPresent
        && config is { Enabled: true, HomeworkBackupEnabled: true }
        && credential is { IsComplete: true }
        && snapshot.IsConnected;

    /// <summary>
    ///     计算「板子内容」的指纹：只算还留在板上的作业（已清理的不进快照，也不该让指纹变化），
    ///     每条取 <c>Id + Content + DueDate + CleanedAt</c> 拼起来做 SHA-256。
    ///     <para>
    ///         按 <see cref="Note.Id" /> 排序再拼：集合顺序变了但内容没变时指纹不变，
    ///         于是不会因为列表重排白传一份一模一样的快照。
    ///     </para>
    /// </summary>
    public static string CreateContentFingerprint(IEnumerable<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);

        var payload = new StringBuilder();
        foreach (var note in notes.Where(note => note.IsOnBoard).OrderBy(note => note.Id))
        {
            payload
                .Append(note.Id.ToString("D"))
                .Append('\u001f')
                .Append(note.Content ?? string.Empty)
                .Append('\u001f')
                .Append(note.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty)
                .Append('\u001f')
                .Append(note.CleanedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty)
                .Append('\u001e');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString()));
        return Convert.ToHexString(hash);
    }
}

/// <summary>
///     作业自动备份对外可见的状态（不可变快照，跟 <see cref="ControlNodeLinkState" /> 同一个风格）。
/// </summary>
/// <param name="LastSuccessAt">上一次成功备份的时间；从没成功过为 <see langword="null" />。</param>
/// <param name="UploadedCount">本次运行期间成功备份的次数。</param>
/// <param name="LastErrorKey">上一次失败的本地化键（成功一次后清空）。</param>
/// <param name="LastErrorText">上一次失败的服务端人话（只进日志 / 兜底展示）。</param>
public sealed record ControlHomeworkBackupState(
    DateTimeOffset? LastSuccessAt = null,
    int UploadedCount = 0,
    string? LastErrorKey = null,
    string? LastErrorText = null)
{
    /// <summary>初始状态：还没备份过。</summary>
    public static ControlHomeworkBackupState Initial { get; } = new();

    /// <summary>是否成功备份过至少一次。</summary>
    public bool HasUploaded => LastSuccessAt is not null;
}