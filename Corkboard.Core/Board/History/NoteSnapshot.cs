using Corkboard.Core.Notes;
using Corkboard.Core.RichText;

namespace Corkboard.Core.Board.History;

/// <summary>
///     写进历史快照的一条作业：纯数据，和 <see cref="Note" /> 的持久化字段一一对应。
///     <para>
///         为什么不直接序列化 <see cref="Note" />：它是 <c>ObservableObject</c>（带属性变更事件），
///         把一份只读快照也做成可观察对象没有意义，而且它的 <c>IsOnBoard</c>、<c>Formats</c>
///         这类计算/遗留字段不该进历史。历史里只留「要还原哪些字段」。
///     </para>
/// </summary>
public sealed class NoteSnapshot
{
    public Guid Id { get; set; }

    public string Content { get; set; } = string.Empty;

    public RichTextContentKind ContentKind { get; set; }

    public string? Handwriting { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string? Section { get; set; }

    public List<string>? Tags { get; set; }

    public DateOnly? DueDate { get; set; }

    public DateTimeOffset? CleanedAt { get; set; }

    public double? ContentFontSize { get; set; }

    public int Order { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>把内存里的作业拍成一份快照。</summary>
    public static NoteSnapshot FromNote(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);

        return new NoteSnapshot
        {
            Id = note.Id,
            Content = note.Content,
            ContentKind = note.ContentKind,
            Handwriting = note.Handwriting,
            Subject = note.Subject,
            Section = note.Section,
            Tags = Note.NormalizeTags(note.Tags),
            DueDate = note.DueDate,
            CleanedAt = note.CleanedAt,
            ContentFontSize = note.ContentFontSize,
            Order = note.Order,
            CreatedAt = note.CreatedAt,
            UpdatedAt = note.UpdatedAt
        };
    }

    /// <summary>把快照还原成一份新的作业对象（回放与「还原到此刻」用，不与内存里那条共享引用）。</summary>
    public Note ToNote()
    {
        return new Note
        {
            Id = Id,
            Content = Content,
            ContentKind = ContentKind,
            Handwriting = Handwriting,
            Subject = Subject,
            Section = Section,
            Tags = Tags,
            DueDate = DueDate,
            CleanedAt = CleanedAt,
            ContentFontSize = ContentFontSize,
            Order = Order,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }
}
