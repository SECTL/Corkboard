using Microsoft.Extensions.Logging.Abstractions;
using Corkboard.Core.Notes;

namespace Corkboard.Core.Tests;

/// <summary>
///     作业专有文件（<c>.cbnote</c>）的读写契约：一条作业一个文件，元数据、富文本内容、手写笔迹
///     全在同一个文件里；旧 <c>notes.json</c> 只用于读取兼容与迁移。
/// </summary>
public class NoteFileFormatTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "corkboard-note-file-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private NoteStore CreateStore() => new(NullLogger<NoteStore>.Instance, _root);

    [Fact]
    public void EncodeDecode_RoundTripsEverythingIncludingContentAndInk()
    {
        var note = new Note
        {
            Content = "<p style=\"text-align:left;\">第一行</p>\n<p>第二行 = 带等号</p>",
            ContentKind = RichText.RichTextContentKind.Html,
            Subject = "英语",
            Section = "A组",
            DueDate = new DateOnly(2026, 10, 18),
            CreatedAt = new DateTimeOffset(2026, 10, 17, 22, 3, 11, TimeSpan.FromHours(8)),
            UpdatedAt = new DateTimeOffset(2026, 10, 17, 22, 4, 0, TimeSpan.FromHours(8)),
            Order = 3,
            Tags = ["上课前写完", "带=等号"]
        };
        note.SetHandwriting("CORKBOARD-INK/2\naspect=1.6\nref=540.00\n#FF1A1A1A|4.00|0|0.1,0.2,1.0");

        var text = NoteFileFormat.Encode(note);

        // 专有格式：首行魔数，内容与笔迹都以长度块的形式放进来（不是 JSON 转义的字符串）。
        Assert.StartsWith(NoteFileFormat.FormatMagic + "/" + NoteFileFormat.FormatVersion, text);
        Assert.Contains("text=", text);
        Assert.Contains("ink=", text);
        Assert.DoesNotContain("\"content\"", text);

        Assert.True(NoteFileFormat.TryDecode(text, out var decoded));
        Assert.Equal(note.Id, decoded.Id);
        Assert.Equal(note.Content, decoded.Content);
        Assert.Equal(RichText.RichTextContentKind.Handwriting, decoded.ContentKind);
        Assert.Equal(note.Handwriting, decoded.Handwriting);
        Assert.Equal("英语", decoded.Subject);
        Assert.Equal("A组", decoded.Section);
        Assert.Equal(new DateOnly(2026, 10, 18), decoded.DueDate);
        Assert.Equal(3, decoded.Order);
        Assert.Equal(["上课前写完", "带=等号"], decoded.Tags);
        Assert.Equal(note.CreatedAt, decoded.CreatedAt);
        Assert.Equal(note.UpdatedAt, decoded.UpdatedAt);
    }

    [Fact]
    public void TryDecode_RejectsBadDataWithoutThrowing()
    {
        Assert.False(NoteFileFormat.TryDecode(null, out _));
        Assert.False(NoteFileFormat.TryDecode("", out _));
        Assert.False(NoteFileFormat.TryDecode("{ not our format }", out _));
        Assert.False(NoteFileFormat.TryDecode("CORKBOARD-NOTE/9\nid=abc", out _));
    }

    [Fact]
    public void SaveAll_WritesOneCbnoteFilePerNoteAndLoadsBack()
    {
        var store = CreateStore();
        var note = new Note
        {
            Content = "数学作业",
            Subject = "数学",
            CreatedAt = new DateTimeOffset(2026, 3, 4, 10, 0, 0, TimeSpan.Zero)
        };

        store.SaveAll([note]);

        var directory = Path.Combine(_root, NoteStore.GetNoteDirectory(note));
        var file = Path.Combine(directory, NoteFileFormat.GetFileName(note));
        Assert.True(File.Exists(file));
        Assert.Equal(NoteFileFormat.FileExtension, Path.GetExtension(file));

        var loaded = CreateStore().LoadAll();
        var single = Assert.Single(loaded);
        Assert.Equal("数学作业", single.Content);
        Assert.Equal("数学", single.Subject);
    }

    [Fact]
    public void SaveAll_MigratesLegacyNotesJsonAndDeletesIt()
    {
        var store = CreateStore();
        var legacyNote = new Note
        {
            Content = "旧数据",
            Subject = "语文",
            CreatedAt = new DateTimeOffset(2026, 3, 4, 10, 0, 0, TimeSpan.Zero)
        };
        var directory = Path.Combine(_root, NoteStore.GetNoteDirectory(legacyNote));
        Directory.CreateDirectory(directory);
        var legacyFile = Path.Combine(directory, NoteStore.NotesFileName);
        File.WriteAllText(legacyFile,
            System.Text.Json.JsonSerializer.Serialize(
                new NoteDayFile { Notes = [legacyNote] },
                Corkboard.Core.Abstraction.ConfigServiceBase.JsonOptions));

        // 读：旧文件里的作业能读回来（按 Id 去重，新文件优先）。
        var loaded = CreateStore().LoadAll();
        Assert.Equal("旧数据", Assert.Single(loaded).Content);

        // 写：改写成 .cbnote，旧 notes.json 删掉，重启不会再读一遍旧文件。
        store.SaveAll(loaded);
        Assert.False(File.Exists(legacyFile));
        Assert.True(File.Exists(Path.Combine(directory, NoteFileFormat.GetFileName(legacyNote))));
        Assert.Equal("旧数据", Assert.Single(CreateStore().LoadAll()).Content);
    }

    [Fact]
    public void SaveAll_KeepsUnreadableLegacyFileForInspection()
    {
        var directory = Path.Combine(_root, "2026", "03", "04");
        Directory.CreateDirectory(directory);
        var broken = Path.Combine(directory, NoteStore.NotesFileName);
        File.WriteAllText(broken, "{ 不是 JSON");

        var store = CreateStore();
        var note = new Note { Content = "好", CreatedAt = new DateTimeOffset(2026, 3, 4, 10, 0, 0, TimeSpan.Zero) };

        // 坏文件读不出来：这一次没有作业，但它已经被记成「别删」。
        Assert.Empty(store.LoadAll());
        store.SaveAll([note]);

        // 解析失败的旧文件要原地留着（让人有机会看），新的作业照常落盘。
        Assert.True(File.Exists(broken));
        Assert.True(File.Exists(Path.Combine(directory, NoteFileFormat.GetFileName(note))));
    }
}
