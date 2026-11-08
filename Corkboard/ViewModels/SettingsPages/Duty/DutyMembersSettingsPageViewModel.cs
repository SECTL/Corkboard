using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.DutyRoster;
using Corkboard.Core.Models.SubConfigs.Duty;
using Corkboard.Core.Services.Config;
using Corkboard.Helpers;

namespace Corkboard.ViewModels.SettingsPages.Duty;

/// <summary>
///     新增成员时「序号与现有成员冲突」的判定档位，与 SR 的 <c>SequenceNumberConflictKind</c> 一致。
/// </summary>
public enum DutySequenceConflictKind
{
    /// <summary>没有冲突（或新序号为空），可以直接新增。</summary>
    None,

    /// <summary>只有一名成员使用该序号且它是纯数字，可以把该序号及之后的数字序号整体 +1。</summary>
    Shiftable,

    /// <summary>序号被占用，但新序号不是纯数字，无法自动顺延。</summary>
    NotNumeric,

    /// <summary>名单里已经有多名成员使用同一序号，自动顺延消除不了重复。</summary>
    AlreadyDuplicated
}

/// <summary>序号冲突的处理方式，四档与 SR 的 <c>SequenceNumberConflictChoice</c> 一致。</summary>
public enum DutySequenceConflictChoice
{
    ShiftAndInsert,
    AppendAtEnd,
    KeepDuplicate,
    EditAgain
}

/// <summary>一名成员的序号顺延计划项；<see cref="Index" /> 指向文档成员集合的下标。</summary>
public sealed record DutySequenceShiftEntry(int Index, string OldId, long OldNumber, long NewNumber);

/// <summary>新增成员时的序号冲突分析结果，供页面决定展示哪种确认弹窗。</summary>
public sealed class DutySequenceConflict
{
    public DutySequenceConflictKind Kind { get; init; } = DutySequenceConflictKind.None;

    /// <summary>去掉首尾空白后的新序号。</summary>
    public string NewId { get; init; } = string.Empty;

    /// <summary>名单中已使用该序号的成员下标。</summary>
    public IReadOnlyList<int> DuplicateIndices { get; init; } = [];

    /// <summary>纯数字且数值不小于新序号的成员及顺延结果。</summary>
    public IReadOnlyList<DutySequenceShiftEntry> ShiftEntries { get; init; } = [];

    /// <summary>新成员插入位置：被撞成员在集合中的下标。</summary>
    public int InsertIndex { get; init; }

    /// <summary>追加到末尾时可用的序号（现有最大数字 + 1）；没有数字序号或已到上限时为 <c>null</c>。</summary>
    public long? AppendNumber { get; init; }
}

/// <summary>
///     序号冲突判定（照 SR 的 <c>ProfileSequenceNumbering</c> 搬到应用层；值日表序号只用于展示，
///     允许为空与重复，这里只按 SR 的行为给出「能不能自动顺延」的建议）。
/// </summary>
internal static class DutySequenceConflictAnalyzer
{
    /// <summary>把序号解析成数字；只接受去掉首尾空白后的纯 ASCII 十进制数字。</summary>
    public static long? TryParseNumber(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var text = id.Trim();
        foreach (var character in text)
        {
            if (character is < '0' or > '9')
                return null;
        }

        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    public static DutySequenceConflict Analyze(IReadOnlyList<string>? existingIds, string? newId)
    {
        var trimmedNewId = newId?.Trim() ?? string.Empty;
        if (existingIds is null || existingIds.Count == 0 || trimmedNewId.Length == 0)
            return new DutySequenceConflict { Kind = DutySequenceConflictKind.None, NewId = trimmedNewId };

        var duplicates = new List<int>();
        var numbers = new long?[existingIds.Count];
        long? maximumNumber = null;

        for (var index = 0; index < existingIds.Count; index++)
        {
            var existingId = existingIds[index]?.Trim() ?? string.Empty;
            if (existingId.Length > 0 && string.Equals(existingId, trimmedNewId, StringComparison.Ordinal))
                duplicates.Add(index);

            var number = TryParseNumber(existingId);
            numbers[index] = number;
            if (number is { } value && (maximumNumber is null || value > maximumNumber))
                maximumNumber = value;
        }

        var appendNumber = maximumNumber is { } maximum && maximum < long.MaxValue ? maximum + 1 : (long?)null;

        if (duplicates.Count == 0)
            return new DutySequenceConflict
            {
                Kind = DutySequenceConflictKind.None,
                NewId = trimmedNewId,
                AppendNumber = appendNumber
            };

        if (duplicates.Count > 1)
            return new DutySequenceConflict
            {
                Kind = DutySequenceConflictKind.AlreadyDuplicated,
                NewId = trimmedNewId,
                DuplicateIndices = duplicates,
                AppendNumber = appendNumber
            };

        if (TryParseNumber(trimmedNewId) is not { } newNumber)
            return new DutySequenceConflict
            {
                Kind = DutySequenceConflictKind.NotNumeric,
                NewId = trimmedNewId,
                DuplicateIndices = duplicates,
                AppendNumber = appendNumber
            };

        var shiftEntries = new List<DutySequenceShiftEntry>();
        for (var index = 0; index < existingIds.Count; index++)
        {
            if (numbers[index] is not { } existingNumber || existingNumber < newNumber)
                continue;

            // 序号已经到 long 上限时无法再 +1，按「不可自动顺延」处理，避免写出重复序号。
            if (existingNumber == long.MaxValue)
                return new DutySequenceConflict
                {
                    Kind = DutySequenceConflictKind.NotNumeric,
                    NewId = trimmedNewId,
                    DuplicateIndices = duplicates,
                    AppendNumber = appendNumber
                };

            shiftEntries.Add(new DutySequenceShiftEntry(index, existingIds[index]?.Trim() ?? string.Empty,
                existingNumber, existingNumber + 1));
        }

        return new DutySequenceConflict
        {
            Kind = DutySequenceConflictKind.Shiftable,
            NewId = trimmedNewId,
            DuplicateIndices = duplicates,
            ShiftEntries = shiftEntries,
            InsertIndex = duplicates[0],
            AppendNumber = appendNumber
        };
    }
}

/// <summary>小组下拉项（排班页也用它，Id 非空）。</summary>
public sealed class DutyGroupOption
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public override string ToString() => Name;
}

/// <summary>成员行 / 成员表单的小组选项；<see cref="Id" /> 为空表示「未分组」。</summary>
public sealed class DutyMemberGroupOption
{
    public Guid? Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public override string ToString() => Name;
}

/// <summary>
///     名单表格里的一行。桌面端直接在表格里内联编辑：文本列改完由 <c>CellEditEnded</c> 提交，
///     勾选框与小组下拉一变更就提交——两者都回调页面 ViewModel，只有它知道怎么写服务。
/// </summary>
public sealed partial class DutyMemberItem : ObservableObject
{
    private readonly DutyMembersSettingsPageViewModel? _owner;
    private bool _suppressCommit;

    public DutyMemberItem(Guid recordId, DutyMembersSettingsPageViewModel? owner)
    {
        RecordId = recordId;
        _owner = owner;
    }

    public Guid RecordId { get; }

    /// <summary>所属小组；权威关系在 <see cref="DutyGroup.MemberIds" />，这里只是界面状态。</summary>
    public Guid? GroupId { get; private set; }

    [ObservableProperty] private string _id = string.Empty;

    [ObservableProperty] private string _name = string.Empty;

    [ObservableProperty] private string _gender = string.Empty;

    [ObservableProperty] private string _tags = string.Empty;

    [ObservableProperty] private bool _isActive = true;

    [ObservableProperty] private string _groupName = string.Empty;

    [ObservableProperty] private DutyMemberGroupOption? _selectedGroup;

    /// <summary>小组下拉的候选项（共用页面的集合，行不自己维护副本）。</summary>
    public IReadOnlyList<DutyMemberGroupOption> GroupOptions =>
        _owner is null ? Array.Empty<DutyMemberGroupOption>() : _owner.GroupOptions;

    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name)
            ? string.IsNullOrWhiteSpace(Id) ? "（未命名）" : Id
            : Name;

    /// <summary>表格里的状态文案；正常参加值日时不显示字。</summary>
    public string StatusText => IsActive ? string.Empty : "停用";

    /// <summary>文本列是否被改过（决定 <c>CellEditEnded</c> 要不要写盘）。</summary>
    public bool IsDirty { get; private set; }

    partial void OnIdChanged(string value) => MarkDirty();

    partial void OnNameChanged(string value) => MarkDirty();

    partial void OnGenderChanged(string value) => MarkDirty();

    partial void OnTagsChanged(string value) => MarkDirty();

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        if (!_suppressCommit)
            _owner?.OnRowActiveChanged(this, value);
    }

    partial void OnSelectedGroupChanged(DutyMemberGroupOption? value)
    {
        if (_suppressCommit)
            return;

        GroupId = value?.Id;
        GroupName = value?.Name ?? string.Empty;
        if (!_suppressCommit)
            _owner?.OnRowGroupChanged(this);
    }

    /// <summary>用文档里的成员数据装填这一行（Reload 与提交后回填都走这里）。</summary>
    internal void Apply(DutyMember member, string groupName, IReadOnlyList<DutyMemberGroupOption> groupOptions)
    {
        _suppressCommit = true;
        try
        {
            // 装填期间 _suppressCommit 压住勾选框 / 小组下拉的回调，只发通知不写盘。
            Id = member.Id;
            Name = member.Name;
            Gender = member.Gender;
            Tags = member.Tags;
            IsActive = member.IsActive;
            GroupId = member.GroupId;
            GroupName = groupName;
            SelectedGroup = groupOptions.FirstOrDefault(option => option.Id == member.GroupId);
        }
        finally
        {
            _suppressCommit = false;
            IsDirty = false;
        }
    }

    internal void ClearDirty() => IsDirty = false;

    private void MarkDirty()
    {
        if (!_suppressCommit)
            IsDirty = true;

        OnPropertyChanged(nameof(DisplayName));
    }
}

/// <summary>
///     「名单与导入」设置页：值日候选人名单的增删改、搜索与来源标注。
///     <para>
///         名册属于业务数据（<c>data/duty/duty.json</c>），由 <see cref="DutyRosterService" /> 读写；
///         这里只做界面状态与草稿，改动都经服务，改完显式 <c>Save()</c>。
///     </para>
/// </summary>
public sealed partial class DutyMembersSettingsPageViewModel : ViewModelBase
{
    private readonly DutyRosterService _service;
    private readonly DebounceTimer _searchDebounce = new(TimeSpan.FromMilliseconds(300));
    private readonly DutyMemberGroupOption _ungrouped = new() { Id = null, Name = "未分组" };

    /// <summary>本地提交期间压住 <see cref="DutyRosterService.Changed" />，避免整表重建丢焦点。</summary>
    private int _suppressChanged;

    private bool _isReloading;
    private PendingMemberAdd? _pendingAdd;
    private DutySequenceConflict? _pendingConflict;

    public DutyMembersSettingsPageViewModel(MainConfigHandler configHandler, DutyRosterService service)
        : base(configHandler)
    {
        _service = service;
        DraftGroup = _ungrouped;
        Reload();
    }

    /// <summary>值日表的界面偏好（启用、周起始日、默认视图……）；名册与排班规则不在这里。</summary>
    public DutySettingsConfig Settings => Config.DutySettings;

    public ObservableCollection<DutyMemberItem> Members { get; } = [];

    /// <summary>小组下拉项（第一项固定是「未分组」）。</summary>
    public ObservableCollection<DutyMemberGroupOption> GroupOptions { get; } = [];

    /// <summary>搜索框（姓名 / 序号 / 小组，包含即命中），停手 300ms 后再过滤。</summary>
    [ObservableProperty] private string _searchText = string.Empty;

    /// <summary>统计行：共 N 人 · 参加值日 M 人 · K 个小组。</summary>
    [ObservableProperty] private string _statsText = string.Empty;

    /// <summary>结果提示（「已添加」「已保存」「没有匹配的成员」这类）；空串时那一行不显示字。</summary>
    [ObservableProperty] private string _statusText = string.Empty;

    [ObservableProperty] private bool _showEmptyState;

    [ObservableProperty] private string _emptyStateText = string.Empty;

    /// <summary>当前名单抽屉里的名单名草稿（不直接写配置/文档，点「重命名」才提交）。</summary>
    [ObservableProperty] private string _rosterNameDraft = string.Empty;

    [ObservableProperty] private string _sourceProviderText = "手工添加";

    [ObservableProperty] private string _rosterSourceNameText = string.Empty;

    [ObservableProperty] private string _importMethodText = string.Empty;

    [ObservableProperty] private string _importedAtText = string.Empty;

    [ObservableProperty] private bool _hasImportSource;

    [ObservableProperty] private bool _hasRosterSourceName;

    [ObservableProperty] private bool _hasImportMethod;

    [ObservableProperty] private bool _hasImportedAt;

    /// <summary>正在编辑的成员（为空 = 新增）。</summary>
    [ObservableProperty] private Guid? _editingRecordId;

    [ObservableProperty] private string _formTitle = "添加成员";

    [ObservableProperty] private string _formStatusText = string.Empty;

    [ObservableProperty] private string _submitButtonText = "添加";

    [ObservableProperty] private string _draftId = string.Empty;

    [ObservableProperty] private string _draftName = string.Empty;

    [ObservableProperty] private string _draftGender = string.Empty;

    [ObservableProperty] private string _draftTags = string.Empty;

    [ObservableProperty] private bool _draftIsActive = true;

    [ObservableProperty] private DutyMemberGroupOption? _draftGroup;

    /// <summary>是否处于「编辑已有成员」状态（抽屉标题与按钮文字据此切换）。</summary>
    public bool IsEditing => EditingRecordId is not null;

    /// <summary>工具栏「当前名单」按钮的文字。</summary>
    public string CurrentRosterLabel
    {
        get
        {
            var name = _service.Document.Source.RosterName?.Trim() ?? string.Empty;
            return name.Length == 0 ? "当前名单" : name;
        }
    }

    internal bool IsReloading => _isReloading;

    partial void OnSearchTextChanged(string value) => _searchDebounce.Schedule(Reload);

    partial void OnEditingRecordIdChanged(Guid? value) => OnPropertyChanged(nameof(IsEditing));

    /// <summary>从服务重新读一遍名单（按搜索词过滤后塞进 <see cref="Members" />）。</summary>
    public void Reload()
    {
        _isReloading = true;
        try
        {
            var document = _service.Document;

            GroupOptions.Clear();
            GroupOptions.Add(_ungrouped);
            foreach (var group in document.OrderedGroups)
                GroupOptions.Add(new DutyMemberGroupOption { Id = group.Id, Name = group.Name });

            var keyword = SearchText?.Trim() ?? string.Empty;
            Members.Clear();
            foreach (var member in document.Members.Where(member => MatchesSearch(member, keyword)))
            {
                var item = new DutyMemberItem(member.RecordId, this);
                item.Apply(member, GroupNameOf(member.GroupId), GroupOptions);
                Members.Add(item);
            }
        }
        finally
        {
            _isReloading = false;
        }

        RefreshStats();
        RefreshSourceInfo();
        OnPropertyChanged(nameof(CurrentRosterLabel));
    }

    /// <summary>提交一行文本列（页面 <c>CellEditEnded</c> 里调）。</summary>
    public void CommitRow(DutyMemberItem item)
    {
        if (!item.IsDirty)
            return;

        var id = item.Id.Trim();
        var name = item.Name.Trim();
        if (id.Length == 0 && name.Length == 0)
        {
            if (_service.FindMember(item.RecordId) is { } existing)
                item.Apply(existing, GroupNameOf(existing.GroupId), GroupOptions);

            item.ClearDirty();
            StatusText = "姓名和序号至少要填一个";
            return;
        }

        ApplyLocal(() => _service.UpdateMemberProfile(
            item.RecordId, name, id, item.Gender.Trim(), item.Tags.Trim(), item.IsActive, item.GroupId));

        if (_service.FindMember(item.RecordId) is { } updated)
            item.Apply(updated, GroupNameOf(updated.GroupId), GroupOptions);

        item.ClearDirty();
        RefreshStats();
        StatusText = "已保存";
    }

    /// <summary>勾选框切换（行里直接回调，避免整表 Reload）。</summary>
    internal void OnRowActiveChanged(DutyMemberItem item, bool isActive)
    {
        if (_isReloading)
            return;

        ApplyLocal(() => _service.SetMemberActive(item.RecordId, isActive));
        RefreshStats();
        StatusText = isActive ? "已设为参加值日" : "已设为不参加值日";
    }

    /// <summary>小组下拉变更（走 <c>UpdateMemberProfile</c>，直接改 GroupId 会被 Normalize 清掉）。</summary>
    internal void OnRowGroupChanged(DutyMemberItem item)
    {
        if (_isReloading)
            return;

        ApplyLocal(() => _service.UpdateMemberProfile(
            item.RecordId, item.Name.Trim(), item.Id.Trim(), item.Gender.Trim(), item.Tags.Trim(),
            item.IsActive, item.GroupId));

        RefreshStats();
        StatusText = "已保存";
    }

    /// <summary>删除成员（确认框在页面 code-behind 里弹）。</summary>
    public void Remove(Guid recordId)
    {
        ApplyLocal(() => _service.RemoveMember(recordId));
        Reload();
        StatusText = "已删除";
    }

    /// <summary>清空名单（确认框在抽屉 code-behind 里弹）。</summary>
    public void ClearRoster()
    {
        ApplyLocal(() => _service.ClearMembers());
        Reload();
        StatusText = "已清空名单";
    }

    /// <summary>当前名单抽屉打开时把草稿与来源信息刷成文档里的值。</summary>
    public void PrepareRosterDrawer()
    {
        RosterNameDraft = _service.Document.Source.RosterName ?? string.Empty;
        RefreshSourceInfo();
    }

    /// <summary>重命名当前名单（成功之后页面/抽屉负责关闭抽屉）。</summary>
    [RelayCommand]
    private void RenameRoster()
    {
        var name = RosterNameDraft.Trim();
        ApplyLocal(() => _service.RenameRoster(name));
        OnPropertyChanged(nameof(CurrentRosterLabel));
        RefreshSourceInfo();
        StatusText = "已重命名名单";
        Saved?.Invoke();
    }

    /// <summary>把表单切回「新增」状态（打开添加抽屉 / 取消 / 保存成功后都走这里）。</summary>
    public void BeginAdd()
    {
        CancelEdit();
        FormTitle = "添加成员";
        SubmitButtonText = "添加";
    }

    /// <summary>把一行装回表单，进入「编辑」状态。</summary>
    public void BeginEdit(DutyMemberItem item)
    {
        var member = _service.FindMember(item.RecordId);
        EditingRecordId = item.RecordId;
        DraftId = member?.Id ?? item.Id;
        DraftName = member?.Name ?? item.Name;
        DraftGender = member?.Gender ?? item.Gender;
        DraftTags = member?.Tags ?? item.Tags;
        DraftIsActive = member?.IsActive ?? item.IsActive;
        DraftGroup = GroupOptions.FirstOrDefault(option => option.Id == (member?.GroupId ?? item.GroupId))
                     ?? _ungrouped;
        FormTitle = "编辑成员";
        SubmitButtonText = "保存";
        FormStatusText = string.Empty;
        _pendingAdd = null;
        _pendingConflict = null;
    }

    /// <summary>取消编辑，表单回到「新增」。</summary>
    [RelayCommand]
    private void CancelEdit()
    {
        EditingRecordId = null;
        DraftId = string.Empty;
        DraftName = string.Empty;
        DraftGender = string.Empty;
        DraftTags = string.Empty;
        DraftIsActive = true;
        DraftGroup = _ungrouped;
        SubmitButtonText = "添加";
        FormTitle = "添加成员";
        FormStatusText = string.Empty;
        _pendingAdd = null;
        _pendingConflict = null;
    }

    /// <summary>新增 / 保存成员（抽屉里的主按钮）。</summary>
    [RelayCommand]
    private void SubmitMember()
    {
        var id = DraftId.Trim();
        var name = DraftName.Trim();
        var gender = DraftGender.Trim();
        var tags = DraftTags.Trim();

        if (name.Length == 0 && id.Length == 0)
        {
            FormStatusText = "姓名和序号至少要填一个";
            return;
        }

        if (EditingRecordId is { } recordId)
        {
            if (_service.FindMember(recordId) is null)
            {
                FormStatusText = "这个成员已经不在了，刷新后再试";
                return;
            }

            ApplyLocal(() => _service.UpdateMemberProfile(
                recordId, name, id, gender, tags, DraftIsActive, DraftGroup?.Id));
            Reload();
            StatusText = "已保存";
            CancelEdit();
            Saved?.Invoke();
            return;
        }

        _pendingAdd = new PendingMemberAdd(id, name, gender, tags, DraftIsActive, DraftGroup?.Id);
        _pendingConflict = DutySequenceConflictAnalyzer.Analyze(
            _service.Document.Members.Select(member => member.Id).ToList(), id);

        if (_pendingConflict.Kind == DutySequenceConflictKind.None)
        {
            CommitAdd(_pendingAdd, id, null, null);
            return;
        }

        FormStatusText = "序号和现有成员重复，请选择处理方式";
        SequenceConflictRequested?.Invoke(
            _pendingConflict,
            _service.Document.Members.Select(MemberDisplayName).ToList());
    }

    /// <summary>页面弹完四档确认框后回这里执行选择。</summary>
    public void ResolvePendingAddConflict(DutySequenceConflictChoice choice)
    {
        if (_pendingAdd is not { } pending || _pendingConflict is not { } conflict)
            return;

        switch (choice)
        {
            case DutySequenceConflictChoice.ShiftAndInsert when conflict.Kind == DutySequenceConflictKind.Shiftable:
                CommitAdd(pending, conflict.NewId, conflict, conflict.InsertIndex);
                break;
            case DutySequenceConflictChoice.AppendAtEnd:
                var appendId = conflict.AppendNumber?.ToString(CultureInfo.InvariantCulture) ?? pending.Id;
                CommitAdd(pending, appendId, null, null);
                break;
            case DutySequenceConflictChoice.KeepDuplicate:
                CommitAdd(pending, pending.Id, null, null);
                break;
            default:
                FormStatusText = "改一下序号再试";
                break;
        }
    }

    private void CommitAdd(PendingMemberAdd pending, string id, DutySequenceConflict? shiftConflict,
        int? insertIndex)
    {
        ApplyLocal(() =>
        {
            if (shiftConflict is { Kind: DutySequenceConflictKind.Shiftable })
            {
                foreach (var entry in shiftConflict.ShiftEntries)
                {
                    if (entry.Index < 0 || entry.Index >= _service.Document.Members.Count)
                        continue;

                    _service.Document.Members[entry.Index].Id =
                        entry.NewNumber.ToString(CultureInfo.InvariantCulture);
                }
            }

            var member = _service.AddMember(pending.Name, id, pending.Gender, pending.Tags);

            // AddMember 落盘时用的还是默认值（参加值日、未分组、末尾），这几项只能补一次保存。
            var needsExtraSave = false;
            if (!pending.IsActive)
            {
                member.IsActive = false;
                needsExtraSave = true;
            }

            if (pending.GroupId is { } groupId && _service.FindGroup(groupId) is { } group)
            {
                group.MemberIds.Add(member.RecordId);
                needsExtraSave = true;
            }

            if (insertIndex is { } target && target >= 0 && target < _service.Document.Members.Count)
            {
                _service.Document.Members.Remove(member);
                _service.Document.Members.Insert(target, member);
                needsExtraSave = true;
            }

            if (needsExtraSave || shiftConflict is not null)
                _service.Save();
        });

        _pendingAdd = null;
        _pendingConflict = null;
        Reload();
        StatusText = "已添加";
        CancelEdit();
        Saved?.Invoke();
    }

    /// <summary>打开名单导入抽屉（页面 code-behind 负责开）。</summary>
    [RelayCommand]
    private void OpenImport() => ImportRequested?.Invoke();

    /// <summary>页面接住「打开导入抽屉」的请求。</summary>
    public event Action? ImportRequested;

    /// <summary>表单保存成功 / 名单重命名成功（页面据此关掉抽屉）。</summary>
    public event Action? Saved;

    /// <summary>新增时序号冲突（页面弹 <c>DutySequenceConflictPrompt</c>，再把选择交回 <see cref="ResolvePendingAddConflict" />）。</summary>
    public event Action<DutySequenceConflict, IReadOnlyList<string>>? SequenceConflictRequested;

    /// <summary>挂上服务的订阅（页面进入可视树时调）。</summary>
    public void Attach()
    {
        _service.Changed += OnDocumentChanged;
        Reload();
    }

    /// <summary>断开订阅（页面离开可视树时调）。</summary>
    public void Detach()
    {
        _service.Changed -= OnDocumentChanged;
        _searchDebounce.Cancel();
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        // 本地提交（保存/勾选/改组）自己会更新统计，整表重建会丢表格焦点，所以只刷新统计行。
        if (_suppressChanged > 0)
        {
            RefreshStats();
            return;
        }

        _searchDebounce.Cancel();
        Reload();
    }

    private void ApplyLocal(Action action)
    {
        _suppressChanged++;
        try
        {
            action();
        }
        finally
        {
            _suppressChanged--;
        }
    }

    private void RefreshStats()
    {
        var document = _service.Document;
        var activeCount = document.Members.Count(member => member.IsActive);
        StatsText = $"共 {document.Members.Count} 人 · 参加值日 {activeCount} 人 · {document.Groups.Count} 个小组";

        ShowEmptyState = Members.Count == 0;
        EmptyStateText = document.Members.Count == 0
            ? "还没有成员：点「添加成员」手工录入，或点「导入名单」从 SecRandom 名单导入"
            : "没有匹配的成员，换个关键词试试";
    }

    private void RefreshSourceInfo()
    {
        var source = _service.Document.Source;
        var manual = string.IsNullOrWhiteSpace(source.ImportMethod) && source.ImportedAt == default;

        HasImportSource = !manual;
        SourceProviderText = manual
            ? "手工添加"
            : string.IsNullOrWhiteSpace(source.Provider) ? "未知来源" : source.Provider.Trim();
        RosterSourceNameText = source.RosterName?.Trim() ?? string.Empty;
        ImportMethodText = manual ? string.Empty : MapImportMethod(source.ImportMethod);
        ImportedAtText = manual || source.ImportedAt == default
            ? string.Empty
            : source.ImportedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

        HasRosterSourceName = RosterSourceNameText.Length > 0;
        HasImportMethod = ImportMethodText.Length > 0;
        HasImportedAt = ImportedAtText.Length > 0;
    }

    private string GroupNameOf(Guid? groupId) =>
        groupId is { } id ? _service.FindGroup(id)?.Name ?? string.Empty : string.Empty;

    private bool MatchesSearch(DutyMember member, string keyword) =>
        keyword.Length == 0
        || member.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        || member.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        || member.Tags.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        || GroupNameOf(member.GroupId).Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private static string MemberDisplayName(DutyMember member) =>
        !string.IsNullOrWhiteSpace(member.Name)
            ? member.Name.Trim()
            : !string.IsNullOrWhiteSpace(member.Id) ? member.Id.Trim() : "（未命名）";

    private static string MapImportMethod(string method) => method.Trim() switch
    {
        "" => string.Empty,
        "File" or "文件导入" => "文件导入",
        "QuickQr" or "扫码快速导入" => "扫码快速导入",
        "Qr" or "OfflineQr" or "二维码导入" => "二维码导入",
        "SessionCode" or "会话码导入" => "会话码导入",
        "Paste" or "粘贴导入" => "粘贴导入",
        "Manual" or "手工添加" => "手工添加",
        var other => other
    };

    private sealed record PendingMemberAdd(
        string Id,
        string Name,
        string Gender,
        string Tags,
        bool IsActive,
        Guid? GroupId);
}