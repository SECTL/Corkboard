using System.Text.Json.Serialization;

namespace Corkboard.Core.DutyRoster;

/// <summary>
///     值日表名单里的一名成员。
///     <para>
///         JSON 键名与 SecRandom-C 的 <c>Student</c> 对齐（<c>record_id / id / name / gender / tags</c>），
///         这样两边的名单可以互导而不用转换字段。<see cref="RecordId" /> 是唯一身份，
///         <see cref="Id" /> 只是展示用的序号/学号，允许重复与为空。
///     </para>
/// </summary>
public sealed class DutyMember
{
    /// <summary>唯一 ID（对应 SR 的 <c>record_id</c>），跨设备导入时原样保留。</summary>
    public Guid RecordId { get; set; } = Guid.NewGuid();

    /// <summary>序号/学号（对应 SR 的 <c>id</c>），仅展示用，不参与身份判定。</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Gender { get; set; } = string.Empty;

    /// <summary>SR 的标签原样保留（值日表不展示，只为互导不丢信息）。</summary>
    public string Tags { get; set; } = string.Empty;

    /// <summary>是否还在名单里。停用的人不再进排班，但历史记录仍可解析到名字。</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    ///     所属小组（冗余字段，权威关系在 <see cref="DutyGroup.MemberIds" />）。
    ///     由 <see cref="DutyRosterDocument.Normalize" /> 统一重建，不要手改。
    /// </summary>
    public Guid? GroupId { get; set; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Id) ? Name : $"{Id}. {Name}";
}
