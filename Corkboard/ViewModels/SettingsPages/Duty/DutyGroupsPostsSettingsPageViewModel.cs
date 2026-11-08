using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.DutyRoster;
using Corkboard.Core.Services.Config;

namespace Corkboard.ViewModels.SettingsPages.Duty;

/// <summary>小组表格里的一行。</summary>
public sealed class DutyGroupRow
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int Order { get; init; }

    public int MemberCount { get; init; }

    public string MemberSummary { get; init; } = string.Empty;

    public string OrderText => (Order + 1).ToString();
}

/// <summary>职务表格里的一行。</summary>
public sealed class DutyPostRow
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int Order { get; init; }

    public int DefaultMemberCount { get; init; }

    public string OrderText => (Order + 1).ToString();
}

/// <summary>给「这个组里谁干这个职务」用的勾选项。</summary>
public sealed partial class DutyMemberToggle : ObservableObject
{
    public Guid RecordId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string GroupName { get; init; } = string.Empty;

    [ObservableProperty] private bool _isChecked;
}

/// <summary>固定人员表单里的星期勾选项。</summary>
public sealed partial class DutyWeekdayToggle : ObservableObject
{
    public DayOfWeek Day { get; init; }

    public string Label { get; init; } = string.Empty;

    [ObservableProperty] private bool _isChecked;
}

/// <summary>固定人员表格里的一行。</summary>
public sealed class DutyFixedAssignmentRow
{
    public Guid Id { get; init; }

    public string PostName { get; init; } = string.Empty;

    public string MemberName { get; init; } = string.Empty;

    public string WeekdaysText { get; init; } = string.Empty;

    public string RelationText { get; init; } = string.Empty;

    public string RangeText { get; init; } = string.Empty;
}

/// <summary>下拉项：固定人员与组内分配的关系。</summary>
public sealed class DutyRelationOption
{
    public DutyFixedRelation Relation { get; init; }

    public string Label { get; init; } = string.Empty;

    public override string ToString() => Label;
}

/// <summary>右侧抽屉正在编辑的对象。</summary>
public enum DutyGroupsPostsEditorMode
{
    None = 0,
    Group = 1,
    Post = 2,
    Fixed = 3
}

/// <summary>
///     「小组与职务」设置页：分组表、职务表、组内职务分配、固定人员（某职务在指定星期固定由某人做）。
///     <para>
///         这里全是排班规则，改动都经 <see cref="DutyRosterService" /> 显式 Save；
///         组内分配改完不用手动「重排」——值日表窗口每次打开都现算。
///     </para>
/// </summary>
public sealed partial class DutyGroupsPostsSettingsPageViewModel : ViewModelBase
{
    private readonly DutyRosterService _service;

    public DutyGroupsPostsSettingsPageViewModel(MainConfigHandler configHandler, DutyRosterService service)
        : base(configHandler)
    {
        _service = service;

        foreach (var (day, label) in WeekdayLabels())
        {
            WeekdayToggles.Add(new DutyWeekdayToggle
            {
                Day = day,
                Label = label,
                IsChecked = DutyWeekdays.Contains(DutyWeekdays.Workdays, day)
            });
        }

        Relations.Add(new DutyRelationOption { Relation = DutyFixedRelation.Replace, Label = "替换（默认）" });
        Relations.Add(new DutyRelationOption { Relation = DutyFixedRelation.Additional, Label = "叠加（多做一份）" });
        SelectedRelation = Relations[0];

        Reload();
    }

    public ObservableCollection<DutyGroupRow> Groups { get; } = [];

    public ObservableCollection<DutyPostRow> Posts { get; } = [];

    public ObservableCollection<DutyMemberToggle> GroupMemberToggles { get; } = [];

    /// <summary>「这个职务在本组由谁干」的勾选项（只列本组成员）。</summary>
    public ObservableCollection<DutyMemberToggle> PositionToggles { get; } = [];

    public ObservableCollection<DutyFixedAssignmentRow> FixedAssignments { get; } = [];

    public ObservableCollection<DutyWeekdayToggle> WeekdayToggles { get; } = [];

    public ObservableCollection<DutyRelationOption> Relations { get; } = [];

    [ObservableProperty] private DutyGroupRow? _selectedGroup;

    [ObservableProperty] private DutyPostRow? _selectedPost;

    /// <summary>「这个职务在本组由谁干」正在编辑的职务。</summary>
    [ObservableProperty] private DutyPostRow? _selectedPositionPost;

    [ObservableProperty] private DutyRelationOption? _selectedRelation;

    /// <summary>固定人员表单里选中的那个人。</summary>
    [ObservableProperty] private DutyMemberToggle? _fixedMember;

    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>状态行左侧那句汇总（共几个小组、几个职务）。</summary>
    [ObservableProperty] private string _summaryText = string.Empty;

    [ObservableProperty] private bool _hasGroups;

    [ObservableProperty] private bool _hasPosts;

    [ObservableProperty] private bool _hasFixedAssignments;

    // ---- 右侧抽屉（新增/编辑小组、职务、固定人员）----

    [ObservableProperty] private DutyGroupsPostsEditorMode _editorMode;

    [ObservableProperty] private string _editorTitle = string.Empty;

    [ObservableProperty] private string _editorSubtitle = string.Empty;

    [ObservableProperty] private string _editorPrimaryText = "保存";

    [ObservableProperty] private string _editorName = string.Empty;

    [ObservableProperty] private int _editorCount = 1;

    [ObservableProperty] private Guid? _editingGroupId;

    [ObservableProperty] private Guid? _editingPostId;

    public bool IsGroupEditor => EditorMode == DutyGroupsPostsEditorMode.Group;

    public bool IsPostEditor => EditorMode == DutyGroupsPostsEditorMode.Post;

    public bool IsFixedEditor => EditorMode == DutyGroupsPostsEditorMode.Fixed;

    partial void OnEditorModeChanged(DutyGroupsPostsEditorMode value)
    {
        OnPropertyChanged(nameof(IsGroupEditor));
        OnPropertyChanged(nameof(IsPostEditor));
        OnPropertyChanged(nameof(IsFixedEditor));
    }

    public IReadOnlyList<string> CallerHints { get; } =
    [
        "先建小组，再把成员放进组里；然后按组配好每个职务由谁负责",
        "固定人员可以跨组选人，用星期勾选控制他哪天做这个职务"
    ];

    partial void OnSelectedGroupChanged(DutyGroupRow? value)
    {
        ReloadGroupMembers();
        ReloadPositionToggles();
    }

    partial void OnSelectedPostChanged(DutyPostRow? value) => ReloadPositionToggles();

    partial void OnSelectedPositionPostChanged(DutyPostRow? value) => ReloadPositionToggles();

    public void Reload()
    {
        var selectedGroupId = SelectedGroup?.Id;
        var selectedPostId = SelectedPost?.Id;

        Groups.Clear();
        foreach (var group in _service.Document.OrderedGroups)
        {
            var members = _service.MembersOf(group.Id);
            Groups.Add(new DutyGroupRow
            {
                Id = group.Id,
                Name = group.Name,
                Order = group.Order,
                MemberCount = members.Count,
                MemberSummary = members.Count == 0 ? "还没有成员" : string.Join("、", members.Select(member => member.Name))
            });
        }

        Posts.Clear();
        foreach (var post in _service.Document.OrderedPosts)
        {
            Posts.Add(new DutyPostRow
            {
                Id = post.Id,
                Name = post.Name,
                Order = post.Order,
                DefaultMemberCount = post.DefaultMemberCount
            });
        }

        FixedAssignments.Clear();
        foreach (var assignment in _service.Document.FixedAssignments)
        {
            FixedAssignments.Add(new DutyFixedAssignmentRow
            {
                Id = assignment.Id,
                PostName = _service.FindPost(assignment.PostId)?.Name ?? "（职务已删除）",
                MemberName = _service.FindMember(assignment.MemberId)?.Name ?? "（成员已删除）",
                WeekdaysText = FormatWeekdays(assignment.Weekdays),
                RelationText = assignment.Relation == DutyFixedRelation.Additional ? "叠加" : "替换",
                RangeText = assignment.StartDate is null && assignment.EndDate is null
                    ? string.Empty
                    : $"{assignment.StartDate?.ToString("yyyy-MM-dd") ?? "…"} ~ {assignment.EndDate?.ToString("yyyy-MM-dd") ?? "…"}"
            });
        }

        HasGroups = Groups.Count > 0;
        HasPosts = Posts.Count > 0;
        HasFixedAssignments = FixedAssignments.Count > 0;

        Members.Clear();
        foreach (var member in _service.ActiveMembers.OrderBy(member => member.Name))
            Members.Add(new DutyMemberToggle { RecordId = member.RecordId, Name = member.Name });

        if (FixedMember is null || Members.All(item => item.RecordId != FixedMember.RecordId))
            FixedMember = Members.FirstOrDefault();

        SummaryText = $"共 {Groups.Count} 个小组、{Posts.Count} 个职务";

        SelectedGroup = Groups.FirstOrDefault(group => group.Id == selectedGroupId);
        SelectedPost = Posts.FirstOrDefault(post => post.Id == selectedPostId);
    }

    /// <summary>把勾选结果写回当前小组。</summary>
    [RelayCommand]
    private void SaveGroupMembers()
    {
        if (SelectedGroup is not { } group)
        {
            StatusText = "先在上面选一个小组";
            return;
        }

        var memberIds = GroupMemberToggles.Where(item => item.IsChecked).Select(item => item.RecordId).ToList();
        _service.SetGroupMembers(group.Id, memberIds);
        StatusText = $"「{group.Name}」现在有 {memberIds.Count} 个人";
        Reload();
    }

    /// <summary>把这个小组的职务分配清空（回到「按名单顺序自动补人」）。</summary>
    [RelayCommand]
    private void ClearPositionAssignments()
    {
        if (SelectedGroup is not { } group)
            return;

        foreach (var post in _service.Document.OrderedPosts)
            _service.SetPositionMembers(group.Id, post.Id, []);

        StatusText = "已清空职务分配，排班时按组内名单顺序自动补人";
        Reload();
    }

    // ---- 右侧抽屉：打开 / 取消 / 保存 ----

    /// <summary>打开「新增小组」抽屉。</summary>
    public void BeginAddGroup()
    {
        EditingGroupId = null;
        EditorName = string.Empty;
        EditorTitle = "新增小组";
        EditorSubtitle = "起一个一眼能认出来的名字，例如「一组」「第二组」";
        EditorPrimaryText = "新增";
        EditorMode = DutyGroupsPostsEditorMode.Group;
    }

    /// <summary>打开「重命名小组」抽屉。</summary>
    public void BeginEditGroup(DutyGroupRow row)
    {
        EditingGroupId = row.Id;
        EditorName = row.Name;
        EditorTitle = "重命名小组";
        EditorSubtitle = "改完不影响组内成员，只改显示名与排班表里的组名";
        EditorPrimaryText = "保存";
        EditorMode = DutyGroupsPostsEditorMode.Group;
    }

    /// <summary>打开「新增职务」抽屉。</summary>
    public void BeginAddPost()
    {
        EditingPostId = null;
        EditorName = string.Empty;
        EditorCount = 1;
        EditorTitle = "新增职务";
        EditorSubtitle = "例如「擦柜子」「扫地」；默认人数是自动补人时的目标人数";
        EditorPrimaryText = "新增";
        EditorMode = DutyGroupsPostsEditorMode.Post;
    }

    /// <summary>打开「编辑职务」抽屉（改名 + 默认人数）。</summary>
    public void BeginEditPost(DutyPostRow row)
    {
        EditingPostId = row.Id;
        EditorName = row.Name;
        EditorCount = Math.Clamp(row.DefaultMemberCount, 1, 20);
        EditorTitle = "编辑职务";
        EditorSubtitle = "改完不影响已配置的组内分工与固定人员";
        EditorPrimaryText = "保存";
        EditorMode = DutyGroupsPostsEditorMode.Post;
    }

    /// <summary>打开「添加固定人员」抽屉；缺职务或缺人时只写状态并返回假。</summary>
    public bool BeginAddFixedAssignment()
    {
        if (Posts.Count == 0)
        {
            StatusText = "先在「职务」里建一个职务";
            return false;
        }

        if (Members.Count == 0)
        {
            StatusText = "名单里还没有人，先去「名单与导入」加人";
            return false;
        }

        SelectedPost ??= Posts[0];
        SelectedRelation ??= Relations[0];
        FixedMember ??= Members[0];
        EditorTitle = "添加固定人员";
        EditorSubtitle = "选职务与人员，再勾上他固定做这个职务的星期；可以跨组选人";
        EditorPrimaryText = "添加";
        EditorMode = DutyGroupsPostsEditorMode.Fixed;
        return true;
    }

    /// <summary>关掉抽屉（不落任何改动）。</summary>
    [RelayCommand]
    private void CancelEditor()
    {
        EditorMode = DutyGroupsPostsEditorMode.None;
        EditingGroupId = null;
        EditingPostId = null;
    }

    /// <summary>抽屉里的主按钮：按当前编辑对象分派。</summary>
    [RelayCommand]
    private void SaveEditor()
    {
        switch (EditorMode)
        {
            case DutyGroupsPostsEditorMode.Group:
                SaveGroupEditor();
                break;
            case DutyGroupsPostsEditorMode.Post:
                SavePostEditor();
                break;
            case DutyGroupsPostsEditorMode.Fixed:
                SaveFixedEditor();
                break;
        }
    }

    private void SaveGroupEditor()
    {
        var name = EditorName.Trim();
        if (name.Length == 0)
        {
            StatusText = "小组名不能为空";
            return;
        }

        Guid groupId;
        if (EditingGroupId is { } existingId)
        {
            _service.RenameGroup(existingId, name);
            groupId = existingId;
            StatusText = "已重命名小组";
        }
        else
        {
            var group = _service.AddGroup(name);
            groupId = group.Id;
            StatusText = $"已新增小组「{group.Name}」";
        }

        EditorMode = DutyGroupsPostsEditorMode.None;
        Reload();
        SelectedGroup = Groups.FirstOrDefault(row => row.Id == groupId);
        Saved?.Invoke();
    }

    private void SavePostEditor()
    {
        var name = EditorName.Trim();
        if (name.Length == 0)
        {
            StatusText = "职务名不能为空";
            return;
        }

        var count = Math.Clamp(EditorCount, 1, 20);
        Guid postId;
        if (EditingPostId is { } existingId)
        {
            _service.RenamePost(existingId, name);
            _service.SetPostDefaultMemberCount(existingId, count);
            postId = existingId;
            StatusText = "已保存职务";
        }
        else
        {
            var post = _service.AddPost(name);
            _service.SetPostDefaultMemberCount(post.Id, count);
            postId = post.Id;
            StatusText = $"已新增职务「{post.Name}」";
        }

        EditorMode = DutyGroupsPostsEditorMode.None;
        Reload();
        SelectedPost = Posts.FirstOrDefault(row => row.Id == postId);
        Saved?.Invoke();
    }

    private void SaveFixedEditor()
    {
        if (SelectedPost is not { } post)
        {
            StatusText = "先选一个职务";
            return;
        }

        if (FixedMember is not { } member)
        {
            StatusText = "先选一个人";
            return;
        }

        var mask = DutyWeekdays.FromDays(WeekdayToggles.Where(toggle => toggle.IsChecked).Select(toggle => toggle.Day));
        mask = DutyWeekdays.Normalize(mask);

        _service.AddFixedAssignment(new DutyFixedAssignment
        {
            PostId = post.Id,
            MemberId = member.RecordId,
            Weekdays = mask,
            Relation = SelectedRelation?.Relation ?? DutyFixedRelation.Replace
        });

        StatusText = $"已添加固定人员：{member.Name} 做「{post.Name}」";
        EditorMode = DutyGroupsPostsEditorMode.None;
        Reload();
        Saved?.Invoke();
    }

    /// <summary>删除小组（确认框在页面 code-behind 里弹）。</summary>
    public void RemoveGroup(DutyGroupRow row)
    {
        _service.RemoveGroup(row.Id);
        StatusText = $"已删除小组「{row.Name}」";
        SelectedGroup = null;
        Reload();
    }

    /// <summary>上移 / 下移小组；<paramref name="delta" /> 为 -1 / +1。</summary>
    public void MoveGroup(DutyGroupRow row, int delta)
    {
        var ordered = _service.Document.OrderedGroups.Select(group => group.Id).ToList();
        var index = ordered.IndexOf(row.Id);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= ordered.Count)
            return;

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        _service.ReorderGroups(ordered);
        Reload();
    }

    /// <summary>删除职务（确认框在页面 code-behind 里弹）。</summary>
    public void RemovePost(DutyPostRow row)
    {
        _service.RemovePost(row.Id);
        StatusText = $"已删除职务「{row.Name}」";
        SelectedPost = null;
        Reload();
    }

    /// <summary>上移 / 下移职务。</summary>
    public void MovePost(DutyPostRow row, int delta)
    {
        var ordered = _service.Document.OrderedPosts.Select(post => post.Id).ToList();
        var index = ordered.IndexOf(row.Id);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= ordered.Count)
            return;

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        _service.ReorderPosts(ordered);
        Reload();
    }

    /// <summary>给某小组的某个职务分配组内成员（从页面上的「分配」弹层调用）。</summary>
    public void SavePositionMembers(DutyGroupRow group, DutyPostRow post, IEnumerable<Guid> memberIds)
    {
        _service.SetPositionMembers(group.Id, post.Id, memberIds);
        StatusText = $"已更新「{group.Name} · {post.Name}」的分工";
        Reload();
    }

    /// <summary>把当前勾选的组内成员写成「这个职务由谁干」。</summary>
    [RelayCommand]
    private void SavePositionAssignments()
    {
        if (SelectedGroup is not { } group)
        {
            StatusText = "先选一个小组";
            return;
        }

        if (SelectedPositionPost is not { } post)
        {
            StatusText = "先选一个职务";
            return;
        }

        SavePositionMembers(group, post, PositionToggles.Where(toggle => toggle.IsChecked).Select(toggle => toggle.RecordId));
    }

    /// <summary>删除固定人员。</summary>
    public void RemoveFixedAssignment(DutyFixedAssignmentRow row)
    {
        _service.RemoveFixedAssignment(row.Id);
        StatusText = "已删除固定人员";
        Reload();
    }

    /// <summary>供固定人员表单选人的名单。</summary>
    public ObservableCollection<DutyMemberToggle> Members { get; } = [];

    /// <summary>抽屉里保存成功（页面据此关掉右侧抽屉）。</summary>
    public event Action? Saved;

    /// <summary>挂上服务订阅（页面进入可视树时调）。</summary>
    public void Attach() => _service.Changed += OnDocumentChanged;

    public void Detach() => _service.Changed -= OnDocumentChanged;

    private void OnDocumentChanged(object? sender, EventArgs e) => Reload();

    private void ReloadGroupMembers()
    {
        GroupMemberToggles.Clear();
        foreach (var member in _service.Document.Members.OrderBy(member => member.Name))
        {
            GroupMemberToggles.Add(new DutyMemberToggle
            {
                RecordId = member.RecordId,
                Name = member.Name,
                GroupName = _service.FindGroup(member.GroupId ?? Guid.Empty)?.Name ?? string.Empty,
                IsChecked = SelectedGroup is { } group && _service.MembersOf(group.Id).Any(item => item.RecordId == member.RecordId)
            });
        }

        ReloadPositionToggles();
    }

    private void ReloadPositionToggles()
    {
        PositionToggles.Clear();
        if (SelectedGroup is not { } group || SelectedPositionPost is not { } post
            || _service.FindGroup(group.Id) is not { } entity)
        {
            return;
        }

        var assigned = entity.GetAssignedMembers(post.Id).ToHashSet();
        foreach (var member in _service.MembersOf(group.Id).OrderBy(member => member.Name))
        {
            PositionToggles.Add(new DutyMemberToggle
            {
                RecordId = member.RecordId,
                Name = member.Name,
                IsChecked = assigned.Contains(member.RecordId)
            });
        }
    }

    private static string FormatWeekdays(int mask)
    {
        var labels = DutyWeekdays.Enumerate(mask).Select(ShortWeekdayLabel).ToList();
        return labels.Count == 0 ? "未指定" : string.Join("、", labels);
    }

    private static IEnumerable<(DayOfWeek Day, string Label)> WeekdayLabels()
    {
        yield return (DayOfWeek.Monday, "周一");
        yield return (DayOfWeek.Tuesday, "周二");
        yield return (DayOfWeek.Wednesday, "周三");
        yield return (DayOfWeek.Thursday, "周四");
        yield return (DayOfWeek.Friday, "周五");
        yield return (DayOfWeek.Saturday, "周六");
        yield return (DayOfWeek.Sunday, "周日");
    }

    private static string ShortWeekdayLabel(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日"
    };
}
