using Corkboard.Core.Abstraction;
using Corkboard.Core.DutyRoster;
using Corkboard.Core.DutyRoster.History;
using Corkboard.Shared.Abstraction;

namespace Corkboard.Core.Tests;

/// <summary>值日表历史（临时层文件）：一天一个 jsonl、只追加、坏行跳过。</summary>
public class DutyHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "corkboard-duty-history-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void AppendAndRead_RoundTripsEventsInOrder()
    {
        var store = new DutyHistoryStore(root: _root);
        var day = DutyTestContext.Day(1, 5);

        store.Append(new DutyHistoryEvent
        {
            Kind = DutyHistoryEventKind.GroupPenalty,
            Date = day,
            GroupName = "A",
            Adjustment = new DutyAdjustment
            {
                Kind = DutyAdjustmentKind.GroupPenalty,
                GroupId = Guid.NewGuid(),
                Days = 2,
                Start = day
            }
        });
        store.Append(new DutyHistoryEvent
        {
            Kind = DutyHistoryEventKind.DayOverride,
            Date = day,
            DayOverride = new DutyDayOverride { Date = day, State = DutyDayOverrideState.Off }
        });

        var events = store.Read(day);

        Assert.Equal(2, events.Count);
        Assert.Equal(DutyHistoryEventKind.GroupPenalty, events[0].Kind);
        Assert.Equal(2, events[0].Adjustment!.Days);
        Assert.Equal(DutyDayOverrideState.Off, events[1].DayOverride!.State);
    }

    [Fact]
    public void ReadRange_SpansMultipleDays()
    {
        var store = new DutyHistoryStore(root: _root);
        store.Append(new DutyHistoryEvent { Kind = DutyHistoryEventKind.GroupAssign, Date = DutyTestContext.Day(1, 5) });
        store.Append(new DutyHistoryEvent { Kind = DutyHistoryEventKind.GroupAssign, Date = DutyTestContext.Day(1, 7) });
        store.Append(new DutyHistoryEvent { Kind = DutyHistoryEventKind.GroupAssign, Date = DutyTestContext.Day(1, 9) });

        var events = store.ReadRange(DutyTestContext.Day(1, 6), DutyTestContext.Day(1, 8));

        Assert.Single(events);
        Assert.Equal(DutyTestContext.Day(1, 7), events[0].Date);
        Assert.Equal(new[] { DutyTestContext.Day(1, 5), DutyTestContext.Day(1, 7), DutyTestContext.Day(1, 9) }, store.ListDays());
    }

    [Fact]
    public void CorruptLine_IsSkipped()
    {
        var store = new DutyHistoryStore(root: _root);
        var day = DutyTestContext.Day(1, 5);
        store.Append(new DutyHistoryEvent { Kind = DutyHistoryEventKind.PersonAdd, Date = day });
        File.AppendAllText(
            Path.Combine(_root, "2026-01-05.jsonl"),
            "{ this is not json" + Environment.NewLine);

        var events = store.Read(day);

        Assert.Single(events);
    }

    [Fact]
    public void Prune_RemovesOlderFiles()
    {
        var store = new DutyHistoryStore(root: _root);
        store.Append(new DutyHistoryEvent { Kind = DutyHistoryEventKind.GroupAssign, Date = DutyTestContext.Day(1, 5) });
        store.Append(new DutyHistoryEvent { Kind = DutyHistoryEventKind.GroupAssign, Date = DutyTestContext.Day(1, 7) });

        store.Prune(DutyTestContext.Day(1, 6));

        Assert.Equal(new[] { DutyTestContext.Day(1, 7) }, store.ListDays());
    }

    private sealed class InMemoryConfigService : ConfigServiceBase
    {
        public Dictionary<string, ConfigBase> Saved { get; } = [];

        public override bool IsConfigExists<T>(T fallback) => Saved.ContainsKey(fallback.ConfigFilePath);

        public override T LoadConfig<T>(T fallback) =>
            Saved.TryGetValue(fallback.ConfigFilePath, out var stored) ? (T)stored : fallback;

        public override void SaveConfig<T>(T config) => Saved[config.ConfigFilePath] = config;

        public override void DeleteConfig<T>(T config) => Saved.Remove(config.ConfigFilePath);
    }

    [Fact]
    public void Service_MutationsWriteHistory()
    {
        var config = new InMemoryConfigService();
        var store = new DutyHistoryStore(root: _root);
        var service = new DutyRosterService(config, store);
        var day = DutyTestContext.Day(1, 5);

        var group = service.AddGroup("A");
        var post = service.AddPost("Wipe cabinet");
        var member = service.AddMember("Inside");

        service.AddAdjustment(new DutyAdjustment
        {
            Kind = DutyAdjustmentKind.GroupPenalty,
            GroupId = group.Id,
            AnchorDate = day,
            Days = 2,
            Start = day
        });
        service.SetDayOverride(day, DutyDayOverrideState.Off, "test");
        service.AddFixedAssignment(new DutyFixedAssignment
        {
            PostId = post.Id,
            MemberId = member.RecordId,
            Weekdays = DutyWeekdays.All,
            StartDate = day
        });
        service.ApplyRoster(
            [member, new DutyMember { Id = "2", Name = "Second" }],
            new DutyRosterSource { RosterName = "Test roster", ImportMethod = "File" });

        var events = store.ReadRange(DutyTestContext.Day(1, 1), DutyTestContext.Day(12, 31));

        Assert.Contains(events, item => item.Kind == DutyHistoryEventKind.GroupPenalty);
        Assert.Contains(events, item => item.Kind == DutyHistoryEventKind.DayOverride);
        Assert.Contains(events, item => item.Kind == DutyHistoryEventKind.FixedAssignmentAdded);
        Assert.Contains(events, item => item.Kind == DutyHistoryEventKind.RosterImported && item.MemberCount == 2);
    }

    [Fact]
    public void DisablingAdjustment_AppendsRevokedEvent()
    {
        var config = new InMemoryConfigService();
        var store = new DutyHistoryStore(root: _root);
        var service = new DutyRosterService(config, store);
        var day = DutyTestContext.Day(1, 5);
        var group = service.AddGroup("A");

        var adjustment = service.AddAdjustment(new DutyAdjustment
        {
            Kind = DutyAdjustmentKind.GroupAssign,
            GroupId = group.Id,
            Start = day,
            End = day
        });
        service.SetAdjustmentEnabled(adjustment.Id, false);

        var events = store.Read(day);

        Assert.Contains(events, item => item.Kind == DutyHistoryEventKind.AdjustmentRevoked);
        Assert.False(adjustment.IsEnabled);
    }
}
