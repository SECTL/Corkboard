namespace Corkboard.Core.DutyRoster;

/// <summary>
///     值日职务（扫地、拖地、擦柜子……）。职务是全局目录，各小组再各自分配成员，
///     所以同一套职务不用在每个小组里重配一遍。
/// </summary>
public sealed class DutyPost
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>展示顺序，也是周视图的行顺序。</summary>
    public int Order { get; set; }

    /// <summary>默认需要几个人（自动均摊时用；手工分配不受它限制）。</summary>
    public int DefaultMemberCount { get; set; } = 1;
}
