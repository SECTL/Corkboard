namespace Corkboard.Core.DutyRoster;

/// <summary>
///     排班引擎：把规则、节假日、加罚、兑换、固定人员合成到一张「某天由哪个小组、每个职务谁值日」的视图上。
///     <para>
///         纯逻辑、无 IO、无 UI 依赖；同一份文档 + 同一个日期区间永远得到同一个结果，
///         时间（「今天」）由调用方传入，方便单测与回放。
///     </para>
///     <para>
///         叠加顺序（唯一权威）：基线组 → 加罚 → 小组代班/对调 → 固定人员 → 个人兑换/增设。
///     </para>
/// </summary>
public sealed class DutyScheduler
{
    private readonly DutyRosterDocument _document;
    private readonly DutyScheduleConfig _schedule;
    private readonly IDutyHolidayCalendar _holidays;
    private readonly Dictionary<DateOnly, DutyDayOverride> _overrides;

    public DutyScheduler(DutyRosterDocument document, IDutyHolidayCalendar? holidays = null)
    {
        _document = document;
        _schedule = document.Schedule;
        _holidays = holidays ?? NullDutyHolidayCalendar.Instance;
        _overrides = document.DayOverrides
            .Where(item => item.Date != default)
            .GroupBy(item => item.Date)
            .ToDictionary(group => group.Key, group => group.Last());
    }

    /// <summary>某天是否值日（含手动覆盖与节假日）。</summary>
    public bool IsDutyDay(DateOnly date) =>
        DutyCalendar.IsDutyDay(date, _schedule, _holidays, _overrides);

    /// <summary>某一天的值日安排（没有这一天时返回 null）。</summary>
    public DutyDayPlan? BuildDay(DateOnly date, DateOnly? today = null) =>
        BuildRange(date, date, today).Days.FirstOrDefault();

    /// <summary>构建 [start, end] 的排班视图；<paramref name="today" /> 只影响「今天」徽标。</summary>
    public DutyScheduleView BuildRange(DateOnly start, DateOnly end, DateOnly? today = null)
    {
        var groups = _document.OrderedGroups.ToList();
        var posts = _document.OrderedPosts.ToList();
        var dutyDays = DutyCalendar.EnumerateDutyDays(start, end, _schedule, _holidays, _overrides);

        Dictionary<DateOnly, Guid> baseline;
        var penaltyDays = new HashSet<DateOnly>();

        if (_schedule.Mode == DutyScheduleMode.Rotation)
        {
            var layout = BuildRotation(start, end, groups);
            baseline = layout.GroupByDate;
            penaltyDays = layout.PenaltyDays;
        }
        else
        {
            baseline = BuildFixedBaseline(dutyDays, groups);
            var penalties = ApplyFixedPenalties(baseline, dutyDays, groups);
            baseline = penalties.GroupByDate;
            penaltyDays = penalties.PenaltyDays;
        }

        var exchangeDays = new HashSet<DateOnly>();
        ApplyGroupAdjustments(baseline, dutyDays, start, end, groups, exchangeDays);

        var groupById = groups.ToDictionary(group => group.Id);
        var days = new List<DutyDayPlan>();

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var isDutyDay = IsDutyDay(date);
            Guid? groupId = baseline.TryGetValue(date, out var assigned) ? assigned : null;
            var group = groupId is { } id && groupById.TryGetValue(id, out var found) ? found : null;

            var markers = BuildDayMarkers(date, isDutyDay, group, penaltyDays, exchangeDays, today);
            var cells = group is null ? [] : BuildCells(date, group, posts);

            if (group is not null && cells.Any(cell => cell.Entries.Count == 0))
                markers |= DutyDayMarker.Unassigned;

            if (group is not null && HasDuplicateMember(cells))
                markers |= DutyDayMarker.Conflict;

            days.Add(new DutyDayPlan
            {
                Date = date,
                HasDuty = isDutyDay && group is not null,
                GroupId = group?.Id,
                GroupName = group?.Name ?? string.Empty,
                Markers = markers,
                Cells = cells
            });
        }

        return new DutyScheduleView
        {
            RangeStart = start,
            RangeEnd = end,
            Posts = posts,
            Days = days
        };
    }

    /// <summary>轮流模式接下来若干段，用于设置页的「接下来怎么排」预览。</summary>
    public IReadOnlyList<DutyRotationSegment> PreviewRotation(DateOnly from, int maxSegments = 12)
    {
        if (_schedule.Mode != DutyScheduleMode.Rotation)
            return [];

        var groups = _document.OrderedGroups.ToList();
        var layout = BuildRotation(from, from.AddDays(400), groups);
        return layout.Segments
            .Where(segment => segment.End >= from)
            .Take(Math.Max(1, maxSegments))
            .ToList();
    }

    // ── 固定周几 ────────────────────────────────────────────────────────────────

    private Dictionary<DateOnly, Guid> BuildFixedBaseline(
        IReadOnlyList<DateOnly> dutyDays,
        IReadOnlyList<DutyGroup> groups)
    {
        var known = groups.Select(group => group.Id).ToHashSet();
        var result = new Dictionary<DateOnly, Guid>();
        foreach (var day in dutyDays)
        {
            if (_schedule.FixedWeekdayMap.TryGetValue((int)day.DayOfWeek, out var groupId)
                && known.Contains(groupId))
            {
                result[day] = groupId;
            }
        }

        return result;
    }

    private (Dictionary<DateOnly, Guid> GroupByDate, HashSet<DateOnly> PenaltyDays) ApplyFixedPenalties(
        Dictionary<DateOnly, Guid> baseline,
        IReadOnlyList<DateOnly> dutyDays,
        IReadOnlyList<DutyGroup> groups)
    {
        var known = groups.Select(group => group.Id).ToHashSet();
        var result = new Dictionary<DateOnly, Guid>(baseline);
        var penaltyDays = new HashSet<DateOnly>();
        if (dutyDays.Count == 0)
            return (result, penaltyDays);

        var penalties = _document.Adjustments
            .Where(adjustment => adjustment.IsEnabled
                                 && adjustment.Kind == DutyAdjustmentKind.GroupPenalty
                                 && adjustment.GroupId is { } id
                                 && known.Contains(id))
            .OrderBy(adjustment => adjustment.CreatedAt)
            .ToList();

        foreach (var penalty in penalties)
        {
            var groupId = penalty.GroupId!.Value;
            var anchor = penalty.AnchorDate ?? dutyDays[0];

            // 找到该组在锚点之后的第一个值日段（固定模式下一段就是一天）。
            var targetIndex = -1;
            for (var index = 0; index < dutyDays.Count; index++)
            {
                if (dutyDays[index] < anchor)
                    continue;

                if (baseline.GetValueOrDefault(dutyDays[index]) == groupId)
                {
                    targetIndex = index;
                    break;
                }
            }

            if (targetIndex < 0)
                continue;

            var days = Math.Clamp(penalty.Days, 1, 14);
            for (var index = targetIndex + 1; index < dutyDays.Count && index <= targetIndex + days; index++)
            {
                result[dutyDays[index]] = groupId;
                penaltyDays.Add(dutyDays[index]);
            }
        }

        return (result, penaltyDays);
    }

    // ── 小组轮流 ────────────────────────────────────────────────────────────────

    private RotationLayout BuildRotation(DateOnly rangeStart, DateOnly rangeEnd, IReadOnlyList<DutyGroup> groups)
    {
        var groupByDate = new Dictionary<DateOnly, Guid>();
        var penaltyDays = new HashSet<DateOnly>();
        var segments = new List<DutyRotationSegment>();
        if (groups.Count == 0)
            return new RotationLayout(groupByDate, penaltyDays, segments);

        var rotation = _schedule.Rotation;
        var start = rotation.StartDate ?? rangeStart;
        if (start > rangeEnd)
            return new RotationLayout(groupByDate, penaltyDays, segments);

        var daysPerGroup = Math.Clamp(rotation.DaysPerGroup, 1, 60);
        var dutyDays = DutyCalendar.EnumerateDutyDays(start, rangeEnd, _schedule, _holidays, _overrides);
        if (dutyDays.Count == 0)
            return new RotationLayout(groupByDate, penaltyDays, segments);

        var count = groups.Count;
        var startIndex = 0;
        if (rotation.StartGroupId is { } startGroupId)
        {
            var found = groups.ToList().FindIndex(group => group.Id == startGroupId);
            if (found >= 0)
                startIndex = found;
        }

        var direction = rotation.Direction < 0 ? -1 : 1;

        Guid GroupAt(int segmentIndex)
        {
            var raw = Mod(startIndex + direction * segmentIndex, count);
            return groups[raw].Id;
        }

        var extra = new Dictionary<int, int>();
        var swallow = new Dictionary<int, int>();
        var penaltySegments = new HashSet<int>();

        var penalties = _document.Adjustments
            .Where(adjustment => adjustment.IsEnabled
                                 && adjustment.Kind == DutyAdjustmentKind.GroupPenalty
                                 && adjustment.GroupId is not null)
            .OrderBy(adjustment => adjustment.CreatedAt)
            .ToList();

        foreach (var penalty in penalties)
        {
            var groupId = penalty.GroupId!.Value;
            var groupIndex = groups.ToList().FindIndex(group => group.Id == groupId);
            if (groupIndex < 0)
                continue;

            var anchor = penalty.AnchorDate ?? rangeStart;
            var ordinal = FirstOrdinalOnOrAfter(dutyDays, anchor);
            if (ordinal < 0)
                continue;

            var baseSegment = ordinal / daysPerGroup;
            var delta = direction > 0
                ? Mod(groupIndex - startIndex, count)
                : Mod(startIndex - groupIndex, count);
            var target = baseSegment + Mod(delta - baseSegment, count);

            var days = Math.Clamp(penalty.Days, 1, 14);
            extra[target] = extra.GetValueOrDefault(target) + days;
            var mode = penalty.PenaltyMode ?? _schedule.PenaltyMode;
            if (mode == DutyPenaltyMode.Swallow)
                swallow[target] = swallow.GetValueOrDefault(target) + days;

            penaltySegments.Add(target);
        }

        var position = 0;
        var segmentIndex = 0;
        var debt = 0;
        var guard = 0;
        var guardLimit = dutyDays.Count * 4 + count * 4 + 1000;

        while (position < dutyDays.Count && guard++ < guardLimit)
        {
            var length = daysPerGroup + extra.GetValueOrDefault(segmentIndex);
            if (debt > 0)
            {
                var consumed = Math.Min(debt, length);
                length -= consumed;
                debt -= consumed;
            }

            if (length > 0)
            {
                var take = Math.Min(length, dutyDays.Count - position);
                var groupId = GroupAt(segmentIndex);
                for (var offset = 0; offset < take; offset++)
                {
                    var day = dutyDays[position + offset];
                    groupByDate[day] = groupId;
                    if (penaltySegments.Contains(segmentIndex))
                        penaltyDays.Add(day);
                }

                segments.Add(new DutyRotationSegment
                {
                    GroupId = groupId,
                    GroupName = groups.First(group => group.Id == groupId).Name,
                    Start = dutyDays[position],
                    End = dutyDays[position + take - 1],
                    Days = take
                });

                position += take;
            }

            debt += swallow.GetValueOrDefault(segmentIndex);
            segmentIndex++;
        }

        return new RotationLayout(groupByDate, penaltyDays, segments);
    }

    // ── 小组代班 / 对调 ──────────────────────────────────────────────────────────

    private void ApplyGroupAdjustments(
        Dictionary<DateOnly, Guid> groupByDate,
        IReadOnlyList<DateOnly> dutyDays,
        DateOnly rangeStart,
        DateOnly rangeEnd,
        IReadOnlyList<DutyGroup> groups,
        HashSet<DateOnly> exchangeDays)
    {
        var known = groups.Select(group => group.Id).ToHashSet();
        var adjustments = _document.Adjustments
            .Where(adjustment => adjustment.IsEnabled
                                 && adjustment.Kind is DutyAdjustmentKind.GroupAssign or DutyAdjustmentKind.GroupSwap)
            .OrderBy(adjustment => adjustment.CreatedAt)
            .ToList();

        if (adjustments.Count == 0)
            return;

        foreach (var day in dutyDays)
        {
            if (day < rangeStart || day > rangeEnd)
                continue;

            foreach (var adjustment in adjustments)
            {
                if (!adjustment.IsEffectiveOn(day))
                    continue;

                switch (adjustment.Kind)
                {
                    case DutyAdjustmentKind.GroupAssign when adjustment.GroupId is { } assigned:
                        if (known.Contains(assigned))
                        {
                            groupByDate[day] = assigned;
                            exchangeDays.Add(day);
                        }

                        break;

                    case DutyAdjustmentKind.GroupSwap
                        when adjustment.GroupId is { } first && adjustment.TargetGroupId is { } second:
                        if (!groupByDate.TryGetValue(day, out var current))
                            break;

                        if (current == first)
                            groupByDate[day] = second;
                        else if (current == second)
                            groupByDate[day] = first;
                        else
                            break;

                        exchangeDays.Add(day);
                        break;
                }
            }
        }
    }

    // ── 单元格 ──────────────────────────────────────────────────────────────────

    private List<DutyPostCell> BuildCells(DateOnly date, DutyGroup group, IReadOnlyList<DutyPost> posts)
    {
        var active = _document.Members
            .Where(member => member.IsActive && !string.IsNullOrWhiteSpace(member.Name))
            .ToDictionary(member => member.RecordId);
        var fixedAssignments = _document.FixedAssignments
            .Where(assignment => assignment.PostId != Guid.Empty && assignment.IsEffectiveOn(date))
            .ToList();
        var personAdjustments = _document.Adjustments
            .Where(adjustment => adjustment.IsEnabled
                                 && adjustment.Kind is DutyAdjustmentKind.PersonSwap or DutyAdjustmentKind.PersonAdd
                                 && adjustment.PostId is not null
                                 && adjustment.IsEffectiveOn(date))
            .OrderBy(adjustment => adjustment.CreatedAt)
            .ToList();

        var cells = new List<DutyPostCell>();
        var used = new HashSet<Guid>();
        foreach (var post in posts)
        {
            var entries = new List<(Guid MemberId, DutyEntryKind Kind)>();

            var replace = fixedAssignments
                .Where(assignment => assignment.PostId == post.Id
                                     && assignment.Relation == DutyFixedRelation.Replace
                                     && active.ContainsKey(assignment.MemberId))
                .ToList();

            if (replace.Count > 0)
            {
                foreach (var assignment in replace)
                {
                    used.Add(assignment.MemberId);
                    entries.Add((assignment.MemberId, DutyEntryKind.Fixed));
                }
            }
            else
            {
                var assigned = group.GetAssignedMembers(post.Id)
                    .Where(active.ContainsKey)
                    .ToList();

                if (assigned.Count == 0)
                {
                    // 没配「这个职务谁干」时按组内名单顺序补人（默认人数个），跳过当天已经排到的成员——
                    // 不然一个组五个人、三个职务，全都落在前两个人身上。
                    var count = Math.Clamp(post.DefaultMemberCount, 1, 20);
                    assigned = group.MemberIds
                        .Where(memberId => active.ContainsKey(memberId) && !used.Contains(memberId))
                        .Take(count)
                        .ToList();
                }

                foreach (var memberId in assigned)
                {
                    used.Add(memberId);
                    entries.Add((memberId, DutyEntryKind.Member));
                }
            }

            foreach (var assignment in fixedAssignments
                         .Where(assignment => assignment.PostId == post.Id
                                              && assignment.Relation == DutyFixedRelation.Additional
                                              && active.ContainsKey(assignment.MemberId)))
            {
                used.Add(assignment.MemberId);
                entries.Add((assignment.MemberId, DutyEntryKind.Fixed));
            }

            foreach (var adjustment in personAdjustments.Where(adjustment => adjustment.PostId == post.Id))
            {
                if (adjustment.Kind == DutyAdjustmentKind.PersonSwap && adjustment.FromMemberId is { } from)
                    entries.RemoveAll(entry => entry.MemberId == from);

                if (adjustment.ToMemberId is { } to && active.ContainsKey(to))
                {
                    entries.RemoveAll(entry => entry.MemberId == to);
                    used.Add(to);
                    entries.Add((to, adjustment.Kind == DutyAdjustmentKind.PersonSwap
                        ? DutyEntryKind.Substitute
                        : DutyEntryKind.Added));
                }
            }

            var distinct = entries
                .GroupBy(entry => entry.MemberId)
                .Select(grouping => grouping.First())
                .Select(entry => new DutyEntry
                {
                    MemberId = entry.MemberId,
                    Name = active[entry.MemberId].Name,
                    Kind = entry.Kind
                })
                .ToList();

            cells.Add(new DutyPostCell
            {
                PostId = post.Id,
                PostName = post.Name,
                Entries = distinct
            });
        }

        return cells;
    }

    private static bool HasDuplicateMember(List<DutyPostCell> cells)
    {
        var seen = new HashSet<Guid>();
        foreach (var cell in cells)
        {
            foreach (var entry in cell.Entries)
            {
                if (!seen.Add(entry.MemberId))
                    return true;
            }
        }

        return false;
    }

    private DutyDayMarker BuildDayMarkers(
        DateOnly date,
        bool isDutyDay,
        DutyGroup? group,
        HashSet<DateOnly> penaltyDays,
        HashSet<DateOnly> exchangeDays,
        DateOnly? today)
    {
        var markers = DutyDayMarker.None;

        if (today is { } value && value == date)
            markers |= DutyDayMarker.Today;

        if (_overrides.TryGetValue(date, out var overridden))
        {
            if (overridden.State == DutyDayOverrideState.Off)
                markers |= DutyDayMarker.DayOff;
            else if (overridden.State == DutyDayOverrideState.Duty)
                markers |= DutyDayMarker.DayOn;
        }

        if (!isDutyDay)
        {
            if (_holidays.IsVacation(date))
                markers |= DutyDayMarker.Vacation;
            else if (_schedule.SkipLegalHolidays && _holidays.IsLegalHoliday(date))
                markers |= DutyDayMarker.LegalHoliday;
        }
        else if (_holidays.IsMakeupWorkday(date))
        {
            markers |= DutyDayMarker.MakeupWorkday;
        }

        if (isDutyDay && group is null)
            markers |= DutyDayMarker.NoGroupConfigured;

        if (penaltyDays.Contains(date))
            markers |= DutyDayMarker.Penalty;

        if (exchangeDays.Contains(date))
            markers |= DutyDayMarker.Exchange;

        if (_document.Adjustments.Any(adjustment => adjustment.IsEnabled
                                                    && adjustment.Kind is DutyAdjustmentKind.PersonSwap or DutyAdjustmentKind.PersonAdd
                                                    && adjustment.IsEffectiveOn(date)))
            markers |= DutyDayMarker.PersonExchange;

        return markers;
    }

    private static int FirstOrdinalOnOrAfter(IReadOnlyList<DateOnly> dutyDays, DateOnly anchor)
    {
        var low = 0;
        var high = dutyDays.Count - 1;
        var result = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (dutyDays[middle] >= anchor)
            {
                result = middle;
                high = middle - 1;
            }
            else
            {
                low = middle + 1;
            }
        }

        return result;
    }

    private static int Mod(int value, int divisor)
    {
        var remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }

    private sealed record RotationLayout(
        Dictionary<DateOnly, Guid> GroupByDate,
        HashSet<DateOnly> PenaltyDays,
        List<DutyRotationSegment> Segments);
}
