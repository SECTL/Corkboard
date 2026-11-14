using Corkboard.Core.ControlCenter;
using Corkboard.Core.Models.SubConfigs.ControlCenter;
using Corkboard.Core.Notes;

namespace Corkboard.Core.Tests;

/// <summary>
///     作业自动备份的纯逻辑：上传触发条件（配置 + 凭证 + 连接 + 客户端）与内容指纹
///     （只算板上的作业、按 Id 排序、内容没变就不重复传）。
/// </summary>
public class ControlHomeworkBackupTests
{
    private static readonly ControlNodeCredential Credential = new("dev-1", "sec-1", "SC-6M34RQ");

    private static readonly ControlNodeLinkState Online = new(ControlNodeLinkStatus.Online);

    private static ControlCenterSettingsConfig EnabledConfig() => new()
    {
        Enabled = true,
        HomeworkBackupEnabled = true
    };

    [Fact]
    public void ShouldUpload_AllConditionsMet_ReturnsTrue() =>
        Assert.True(ControlHomeworkBackup.ShouldUpload(EnabledConfig(), Credential, Online, true));

    [Fact]
    public void ShouldUpload_ControlCenterDisabled_ReturnsFalse() =>
        Assert.False(ControlHomeworkBackup.ShouldUpload(
            new ControlCenterSettingsConfig { Enabled = false, HomeworkBackupEnabled = true },
            Credential,
            Online,
            true));

    [Fact]
    public void ShouldUpload_HomeworkBackupDisabled_ReturnsFalse() =>
        Assert.False(ControlHomeworkBackup.ShouldUpload(
            new ControlCenterSettingsConfig { Enabled = true, HomeworkBackupEnabled = false },
            Credential,
            Online,
            true));

    [Fact]
    public void ShouldUpload_NoCredential_ReturnsFalse() =>
        Assert.False(ControlHomeworkBackup.ShouldUpload(EnabledConfig(), null, Online, true));

    [Fact]
    public void ShouldUpload_IncompleteCredential_ReturnsFalse() =>
        Assert.False(ControlHomeworkBackup.ShouldUpload(
            EnabledConfig(),
            new ControlNodeCredential("dev-1", " ", "SC-6M34RQ"),
            Online,
            true));

    [Fact]
    public void ShouldUpload_NotConnected_ReturnsFalse() =>
        Assert.False(ControlHomeworkBackup.ShouldUpload(
            EnabledConfig(),
            Credential,
            new ControlNodeLinkState(ControlNodeLinkStatus.Connecting),
            true));

    [Fact]
    public void ShouldUpload_NoClient_ReturnsFalse() =>
        Assert.False(ControlHomeworkBackup.ShouldUpload(EnabledConfig(), Credential, Online, false));

    [Fact]
    public void Fingerprint_SameContentDifferentOrder_IsStable()
    {
        var first = Note("2f1c2d9a-0000-0000-0000-000000000001", "数学作业");
        var second = Note("2f1c2d9a-0000-0000-0000-000000000002", "语文作业");

        var forward = ControlHomeworkBackup.CreateContentFingerprint([first, second]);
        var reversed = ControlHomeworkBackup.CreateContentFingerprint([second, first]);

        Assert.Equal(forward, reversed);
    }

    [Fact]
    public void Fingerprint_ChangedContent_Changes()
    {
        var note = Note("2f1c2d9a-0000-0000-0000-000000000001", "数学作业");
        var before = ControlHomeworkBackup.CreateContentFingerprint([note]);

        note.Content = "数学作业（改过）";

        Assert.NotEqual(before, ControlHomeworkBackup.CreateContentFingerprint([note]));
    }

    [Fact]
    public void Fingerprint_ChangedDueDate_Changes()
    {
        var note = Note("2f1c2d9a-0000-0000-0000-000000000001", "数学作业");
        note.DueDate = new DateOnly(2026, 11, 20);
        var before = ControlHomeworkBackup.CreateContentFingerprint([note]);

        note.DueDate = new DateOnly(2026, 11, 21);

        Assert.NotEqual(before, ControlHomeworkBackup.CreateContentFingerprint([note]));
    }

    [Fact]
    public void Fingerprint_IgnoresCleanedNotes()
    {
        var onBoard = Note("2f1c2d9a-0000-0000-0000-000000000001", "数学作业");
        var cleaned = Note("2f1c2d9a-0000-0000-0000-000000000002", "已经收起来的");
        cleaned.CleanedAt = DateTimeOffset.Now;

        Assert.Equal(
            ControlHomeworkBackup.CreateContentFingerprint([onBoard]),
            ControlHomeworkBackup.CreateContentFingerprint([onBoard, cleaned]));
    }

    [Fact]
    public void Fingerprint_EmptyBoard_IsStable()
    {
        Assert.Equal(
            ControlHomeworkBackup.CreateContentFingerprint([]),
            ControlHomeworkBackup.CreateContentFingerprint([]));
    }

    private static Note Note(string id, string content) => new()
    {
        Id = Guid.Parse(id),
        Content = content,
        Subject = "数学",
        CreatedAt = DateTimeOffset.Now,
        UpdatedAt = DateTimeOffset.Now
    };
}