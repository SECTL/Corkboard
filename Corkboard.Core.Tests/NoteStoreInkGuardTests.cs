using Corkboard.Core.Handwriting;
using Corkboard.Core.Notes;
using Corkboard.Core.RichText;
using Microsoft.Extensions.Logging.Abstractions;

namespace Corkboard.Core.Tests;

/// <summary>
///     存储层兜底：内存里是「空正文 + 空笔迹」时，不能让磁盘上有手写的作业被空内容覆盖。
///     <para>
///         踩过的坑：编辑手写作业时表单落在「打字」页签，保存按空 HTML 写回去，ink 块被抹掉，
///         重启后只剩一张白卡片，而且不可恢复。
///     </para>
/// </summary>
public sealed class NoteStoreInkGuardTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "corkboard-ink-guard-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveAll_KeepsInkOnDisk_WhenMemoryNoteHasNoContent()
    {
        var store = new NoteStore(NullLogger<NoteStore>.Instance, _root, legacyFilePath: null);

        var ink = HandwritingCodec.Encode(new HandwritingDocument
        {
            Strokes =
            [
                new HandwritingStroke
                {
                    Color = Avalonia.Media.Colors.Black,
                    Width = 10,
                    Points = [HandwritingPoint.Create(0.1, 0.2), HandwritingPoint.Create(0.3, 0.4)]
                }
            ]
        });

        var note = new Note { CreatedAt = DateTimeOffset.Now };
        note.SetHandwriting(ink);

        store.SaveAll([note]);

        var file = Path.Combine(_root, NoteStore.GetNoteDirectory(note), NoteFileFormat.GetFileName(note));
        Assert.True(File.Exists(file));

        // 模拟「表单以打字态保存了空内容」：同一个 Id / 创建时间，正文与笔迹都是空。
        var wiped = new Note { Id = note.Id, CreatedAt = note.CreatedAt };
        wiped.SetHtmlContent(string.Empty);

        store.SaveAll([wiped]);

        var text = File.ReadAllText(file);
        Assert.True(NoteFileFormat.TryDecode(text, out var stored));
        Assert.False(string.IsNullOrWhiteSpace(stored.Handwriting));
        Assert.Equal(ink, stored.Handwriting);
        Assert.Equal(RichTextContentKind.Handwriting, stored.ContentKind);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
