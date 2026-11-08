using Corkboard.Core.Actions;
using Xunit;

namespace Corkboard.Core.Tests;

/// <summary>
///     行动域的三个存储：<c>rules.json</c>、<c>state.json</c>、<c>runs/yyyy-MM-dd.jsonl</c>。
///     <para>
///         全部写在临时目录里（每个用例一份），不碰用户数据；日期一律由参数给出，
///         只有「保留天数」这类跟今天有关的用例用 <see cref="DateTime.Today" /> 起算，
///         但前后各留了足够的余量，换台机器跑结论不变。
///     </para>
/// </summary>
public sealed class ActionStoreTests : IDisposable
{
    private readonly string _root;
    private readonly ActionStorePaths _paths;

    public ActionStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "corkboard-action-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _paths = new ActionStorePaths(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时目录清不掉不影响结论。
        }
    }

    private static ActionRule Rule(Guid? id = null, string? name = "晚自习行动", bool enabled = true) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name,
        Enabled = enabled,
        Trigger = new ActionTrigger
        {
            Kind = ActionTriggerKind.Weekly,
            Days = ActionDayFlags.Monday | ActionDayFlags.Wednesday,
            Time = new TimeOnly(19, 30)
        },
        Condition = new ActionCondition
        {
            Kind = ActionConditionKind.DateRange,
            Start = new DateOnly(2026, 11, 1),
            End = new DateOnly(2026, 12, 31)
        },
        Steps =
        [
            new ActionStep { TargetId = BoardTargetIds.Homework, Operation = BoardOperation.Show },
            new ActionStep { TargetId = BoardTargetIds.DutyBoard, Operation = BoardOperation.Hide }
        ],
        CreatedAt = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Unspecified),
        UpdatedAt = new DateTime(2026, 11, 2, 11, 30, 0, DateTimeKind.Unspecified)
    };

    // ── rules.json ──────────────────────────────────────────────────

    /// <summary>写盘再读回来，中文字段与所有契约字段都不丢；文件是 snake_case + 枚举字符串。</summary>
    [Fact]
    public void RuleStore_RoundTripsChineseAndAllFields()
    {
        var original = Rule();
        var store = new ActionRuleStore(_paths);
        store.Upsert(original);

        var reloaded = new ActionRuleStore(_paths);
        var rule = Assert.Single(reloaded.Rules);

        Assert.Equal(original.Id, rule.Id);
        Assert.Equal("晚自习行动", rule.Name);
        Assert.True(rule.Enabled);
        Assert.Equal(ActionTriggerKind.Weekly, rule.Trigger.Kind);
        Assert.Equal(new TimeOnly(19, 30), rule.Trigger.Time);
        Assert.Equal(ActionDayFlags.Monday | ActionDayFlags.Wednesday, rule.Trigger.Days);
        Assert.Null(rule.Trigger.Date);
        Assert.Equal(new DateOnly(2026, 11, 1), rule.Condition!.Start);
        Assert.Equal(new DateOnly(2026, 12, 31), rule.Condition.End);
        Assert.Equal(2, rule.Steps.Count);
        Assert.Equal(BoardTargetIds.DutyBoard, rule.Steps[1].TargetId);
        Assert.Equal(BoardOperation.Hide, rule.Steps[1].Operation);
        Assert.Equal(original.CreatedAt, rule.CreatedAt);
        Assert.Equal(original.UpdatedAt, rule.UpdatedAt);

        var json = File.ReadAllText(_paths.RulesFile);
        Assert.Contains("晚自习行动", json);
        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"created_at\"", json);
        Assert.Contains("\"kind\": \"Weekly\"", json);
        Assert.Contains("\"operation\": \"Show\"", json);
        Assert.DoesNotContain("\\u", json);
    }

    /// <summary>Id 为空的「全新规则」补一个新 Guid；同一 Id 再 Upsert 是替换而不是新增。</summary>
    [Fact]
    public void RuleStore_UpsertAssignsIdAndReplacesBySameId()
    {
        var store = new ActionRuleStore(_paths);

        store.Upsert(Rule(Guid.Empty));
        var created = Assert.Single(store.Rules);
        Assert.NotEqual(Guid.Empty, created.Id);

        store.Upsert(created with { Name = "改过的名字" });
        var replaced = Assert.Single(store.Rules);
        Assert.Equal(created.Id, replaced.Id);
        Assert.Equal("改过的名字", replaced.Name);
    }

    /// <summary>写盘是原子写：临时文件不会留在磁盘上。</summary>
    [Fact]
    public void RuleStore_WritesAtomicallyAndLeavesNoTempFile()
    {
        var store = new ActionRuleStore(_paths);
        store.Upsert(Rule());

        Assert.True(File.Exists(_paths.RulesFile));
        Assert.False(File.Exists(_paths.RulesFile + ".tmp"));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    /// <summary>坏文件：备份一份 <c>rules.bad-*.json</c>、从空列表起、只读打开，原文件一动不动。</summary>
    [Fact]
    public void RuleStore_CorruptFile_BacksUpAndOpensReadOnly()
    {
        const string broken = "{ 这不是 JSON";
        File.WriteAllText(_paths.RulesFile, broken);

        var store = new ActionRuleStore(_paths);

        Assert.Empty(store.Rules);
        Assert.True(store.IsReadOnly);
        Assert.NotNull(store.LoadError);
        Assert.Equal(broken, File.ReadAllText(_paths.RulesFile));

        var backups = Directory.GetFiles(_root, "rules.bad-*.json");
        var backup = Assert.Single(backups);
        Assert.Equal(broken, File.ReadAllText(backup));

        Assert.Throws<ActionStoreReadOnlyException>(() => store.Upsert(Rule()));
    }

    /// <summary>版本比当前新：只读打开，绝不降级覆盖。</summary>
    [Fact]
    public void RuleStore_FutureVersion_IsReadOnlyAndUntouched()
    {
        const string future = "{\n  \"version\": 2,\n  \"rules\": []\n}";
        File.WriteAllText(_paths.RulesFile, future);

        var store = new ActionRuleStore(_paths);
        Assert.True(store.IsReadOnly);
        Assert.NotNull(store.LoadError);

        Assert.Throws<ActionStoreReadOnlyException>(() => store.Upsert(Rule()));
        Assert.Throws<ActionStoreReadOnlyException>(() => store.Remove(Guid.NewGuid()));
        Assert.Throws<ActionStoreReadOnlyException>(() => store.SetEnabled(Guid.NewGuid(), false));
        Assert.Equal(future, File.ReadAllText(_paths.RulesFile));
    }

    /// <summary>启用 / 删除只在真有变化时写盘并通知。</summary>
    [Fact]
    public void RuleStore_SetEnabledAndRemove_NotifyOnlyOnChange()
    {
        var store = new ActionRuleStore(_paths);
        var rule = Rule();
        store.Upsert(rule);

        var notifications = 0;
        store.Changed += (_, _) => notifications++;

        store.SetEnabled(rule.Id, false);
        Assert.False(Assert.Single(store.Rules).Enabled);
        Assert.Equal(1, notifications);

        // 值没变：不写盘、不通知。
        store.SetEnabled(rule.Id, false);
        Assert.Equal(1, notifications);

        store.Remove(Guid.NewGuid());
        Assert.Equal(1, notifications);

        store.Remove(rule.Id);
        Assert.Empty(store.Rules);
        Assert.Equal(2, notifications);
    }

    /// <summary>文件不存在时按空列表、可写打开。</summary>
    [Fact]
    public void RuleStore_MissingFile_OpensEmptyAndWritable()
    {
        var store = new ActionRuleStore(_paths);

        Assert.Empty(store.Rules);
        Assert.False(store.IsReadOnly);
        Assert.Null(store.LoadError);
    }

    // ── runs/yyyy-MM-dd.jsonl ───────────────────────────────────────

    private static ActionRun Run(DateTime startedAt, string title) => new()
    {
        Id = Guid.NewGuid(),
        SourceKind = ActionRunSourceKind.CustomRule,
        SourceId = "rule-1",
        Title = title,
        TriggeredBy = ActionRunTrigger.Schedule,
        OccurrenceAt = startedAt,
        StartedAt = startedAt,
        FinishedAt = startedAt.AddSeconds(5),
        Status = ActionRunStatus.Success,
        Steps =
        [
            new ActionStepRun
            {
                TargetId = BoardTargetIds.Homework,
                Operation = BoardOperation.Show,
                Status = ActionRunStatus.Success
            }
        ]
    };

    /// <summary>按开始日归文件、一行一条、只追加；跨日读取按开始时刻升序。</summary>
    [Fact]
    public void RunStore_AppendsByDayAndReadsRange()
    {
        var store = new ActionRunStore(_paths);
        var first = new DateTime(2026, 11, 6, 9, 0, 0, DateTimeKind.Unspecified);
        var second = new DateTime(2026, 11, 6, 18, 0, 0, DateTimeKind.Unspecified);
        var third = new DateTime(2026, 11, 7, 9, 0, 0, DateTimeKind.Unspecified);

        store.Append(Run(first, "第一次"));
        store.Append(Run(second, "第二次"));
        store.Append(Run(third, "第三次"));

        var day1 = store.ReadDay(new DateOnly(2026, 11, 6));
        Assert.Equal(2, day1.Count);
        Assert.Equal("第一次", day1[0].Title);
        Assert.Equal("第二次", day1[1].Title);

        var lines = File.ReadAllLines(_paths.RunFile(new DateOnly(2026, 11, 6)));
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.False(string.IsNullOrWhiteSpace(line)));

        var range = store.ReadRange(new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 7));
        Assert.Equal(3, range.Count);
        Assert.Equal(new[] { first, second, third }, range.Select(run => run.StartedAt).ToArray());
        Assert.Equal(ActionRunStatus.Success, range[0].Status);
        Assert.Single(range[0].Steps);

        Assert.Empty(store.ReadDay(new DateOnly(2026, 11, 8)));
        Assert.Empty(store.ReadRange(new DateOnly(2026, 11, 8), new DateOnly(2026, 11, 7)));
    }

    /// <summary>坏行跳过，不影响同一天其它记录。</summary>
    [Fact]
    public void RunStore_SkipsBrokenLines()
    {
        var store = new ActionRunStore(_paths);
        var startedAt = new DateTime(2026, 11, 6, 9, 0, 0, DateTimeKind.Unspecified);
        store.Append(Run(startedAt, "好的记录"));

        File.AppendAllText(_paths.RunFile(new DateOnly(2026, 11, 6)), "{ 半行 JSON\n");

        var runs = store.ReadDay(new DateOnly(2026, 11, 6));
        Assert.Single(runs);
        Assert.Equal("好的记录", runs[0].Title);
    }

    /// <summary>清理只删「严格 yyyy-MM-dd.jsonl」且早于保留期的文件，别的文件一概不动。</summary>
    [Fact]
    public void RunStore_PruneDeletesOnlyExpiredDayFiles()
    {
        Directory.CreateDirectory(_paths.RunsFolder);

        var expired = Path.Combine(_paths.RunsFolder, "2000-01-01.jsonl");
        var today = _paths.RunFile(DateOnly.FromDateTime(DateTime.Today));
        var notADate = Path.Combine(_paths.RunsFolder, "2026-13-45.jsonl");
        var otherExtension = Path.Combine(_paths.RunsFolder, "2000-01-01.log");
        var userFile = Path.Combine(_paths.RunsFolder, "backup-2000-01-01.jsonl");

        foreach (var file in new[] { expired, today, notADate, otherExtension, userFile })
            File.WriteAllText(file, "{}\n");

        var store = new ActionRunStore(_paths);
        store.Prune(ActionRunStore.ActionRunRetentionDays);

        Assert.False(File.Exists(expired));
        Assert.True(File.Exists(today));
        Assert.True(File.Exists(notADate));
        Assert.True(File.Exists(otherExtension));
        Assert.True(File.Exists(userFile));
    }

    // ── state.json ─────────────────────────────────────────────────

    /// <summary>水位线与抑制表都落盘、能读回；抑制只对那一天有效（只改内存，Save 才写）。</summary>
    [Fact]
    public void StateStore_RoundTripsLastCheckedAndSuppression()
    {
        var lastChecked = new DateTime(2026, 11, 7, 9, 0, 0, DateTimeKind.Unspecified);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var store = new ActionStateStore(_paths);

        Assert.Null(store.LastChecked);
        Assert.False(store.IsSuppressed(ExamSourceId, today));

        store.LastChecked = lastChecked;
        store.Suppress(ExamSourceId, today);
        Assert.True(store.IsSuppressed(ExamSourceId, today));
        store.Save();

        var reloaded = new ActionStateStore(_paths);
        Assert.Equal(lastChecked, reloaded.LastChecked);
        Assert.True(reloaded.IsSuppressed(ExamSourceId, today));
        Assert.False(reloaded.IsSuppressed(ExamSourceId, today.AddDays(-1)));
        Assert.False(reloaded.IsSuppressed("另一个源", today));
        Assert.False(reloaded.IsReadOnly);

        reloaded.Unsuppress(ExamSourceId, today);
        Assert.False(reloaded.IsSuppressed(ExamSourceId, today));
        reloaded.Save();

        Assert.False(new ActionStateStore(_paths).IsSuppressed(ExamSourceId, today));
        Assert.False(File.Exists(_paths.StateFile + ".tmp"));
    }

    /// <summary>state.json 损坏：按安全默认起来（不抛），再 Save 能写回一份好文件。</summary>
    [Fact]
    public void StateStore_CorruptFile_FallsBackToSafeDefaults()
    {
        File.WriteAllText(_paths.StateFile, "]] 坏掉的状态");

        var store = new ActionStateStore(_paths);

        Assert.Null(store.LastChecked);
        Assert.False(store.IsSuppressed(ExamSourceId, DateOnly.FromDateTime(DateTime.Now)));
        Assert.NotNull(store.LoadError);
        Assert.False(store.IsReadOnly);

        store.LastChecked = new DateTime(2026, 11, 7, 9, 0, 0, DateTimeKind.Unspecified);
        store.Save();

        var reloaded = new ActionStateStore(_paths);
        Assert.Equal(new DateTime(2026, 11, 7, 9, 0, 0, DateTimeKind.Unspecified), reloaded.LastChecked);
        Assert.Null(reloaded.LoadError);
    }

    /// <summary>版本比当前新：只读打开，Save 不覆盖原文件。</summary>
    [Fact]
    public void StateStore_FutureVersion_DoesNotOverwrite()
    {
        const string future = "{\n  \"version\": 2\n}";
        File.WriteAllText(_paths.StateFile, future);

        var store = new ActionStateStore(_paths);
        Assert.True(store.IsReadOnly);

        store.LastChecked = new DateTime(2026, 11, 7, 9, 0, 0, DateTimeKind.Unspecified);
        store.Suppress(ExamSourceId, DateOnly.FromDateTime(DateTime.Now));
        store.Save();

        Assert.Equal(future, File.ReadAllText(_paths.StateFile));
    }

    private const string ExamSourceId = "exam-auto-show";
}
