using System.Collections.ObjectModel;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Exam;
using Corkboard.Shared.Abstraction;
using Xunit;

namespace Corkboard.Core.Tests;

/// <summary>一天几场的推荐、整体平移、以及「存下来的作息模板」。</summary>
public class ExamSlotPresetTests
{
    private static readonly DateOnly FirstDay = new(2026, 6, 24);

    // ---------- 推荐 ----------

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(6, 3)]
    [InlineData(7, 4)]
    [InlineData(8, 4)]
    [InlineData(9, 5)]
    [InlineData(12, 5)]
    public void Recommend_GrowsWithSubjectCount(int subjectCount, int expectedSlots)
    {
        Assert.Equal(expectedSlots, ExamPlanSlotPresets.Recommend(subjectCount).Count);
    }

    [Fact]
    public void Recommend_ZeroSubjects_StillGivesOneSlot()
    {
        // 还没勾科目时界面也要有一行能显示，不能返回空表。
        Assert.Single(ExamPlanSlotPresets.Recommend(0));
    }

    [Fact]
    public void ThreePerDay_HasTwoMorningAndOneAfternoon()
    {
        var slots = ExamPlanSlotPresets.ThreePerDay();

        Assert.Equal(3, slots.Count);
        Assert.Equal(new TimeOnly(8, 0), slots[0].FixedStart);
        Assert.Equal(ExamSlotStartMode.Fixed, slots[0].StartMode);
        Assert.Equal(ExamSlotStartMode.AfterPrevious, slots[1].StartMode);
        Assert.Equal(20, slots[1].BreakMinutes);
        Assert.Equal(new TimeOnly(14, 0), slots[2].FixedStart);
    }

    // ---------- 整体平移 ----------

    [Fact]
    public void Shift_MovesFixedSlotsAfterTheFirst()
    {
        var slots = ExamPlanSlotPresets.Materialize(ExamPlanSlotPresets.TwoPerDay());

        ExamPlanSlotShift.Apply(slots, -60); // 整体提前一小时

        // 第 1 场由调用方自己写（它就是「被改的那一场」），这里只管后面几场。
        Assert.Equal(new TimeOnly(8, 0), slots[0].FixedStart);
        Assert.Equal(new TimeOnly(13, 0), slots[1].FixedStart);
    }

    [Fact]
    public void Shift_LeavesAfterPreviousSlotsAlone()
    {
        var slots = ExamPlanSlotPresets.Materialize(ExamPlanSlotPresets.FourPerDay());

        ExamPlanSlotShift.Apply(slots, 30);

        Assert.Equal(new TimeOnly(8, 0), slots[0].FixedStart); // 第 1 场不动
        Assert.Equal(ExamSlotStartMode.AfterPrevious, slots[1].StartMode); // 它本来就跟着上一场走
        Assert.Equal(new TimeOnly(14, 30), slots[2].FixedStart);
        Assert.Equal(ExamSlotStartMode.AfterPrevious, slots[3].StartMode);
    }

    [Fact]
    public void Shift_ClampsAtMidnight()
    {
        var slots = ExamPlanSlotPresets.Materialize(ExamPlanSlotPresets.TwoPerDay());

        ExamPlanSlotShift.Apply(slots, -20 * 60); // 往前推 20 小时，落到昨天去了

        Assert.Equal(new TimeOnly(0, 0), slots[1].FixedStart);
    }

    [Fact]
    public void Shift_ZeroDelta_ChangesNothing()
    {
        var slots = ExamPlanSlotPresets.Materialize(ExamPlanSlotPresets.FivePerDay());
        var before = slots.Select(slot => slot.FixedStart).ToList();

        ExamPlanSlotShift.Apply(slots, 0);

        Assert.Equal(before, slots.Select(slot => slot.FixedStart));
    }

    [Fact]
    public void Delta_IsSignedMinuteDifference()
    {
        Assert.Equal(-60, ExamPlanSlotShift.Delta(new TimeOnly(8, 0), new TimeOnly(7, 0)));
        Assert.Equal(90, ExamPlanSlotShift.Delta(new TimeOnly(14, 0), new TimeOnly(15, 30)));
    }

    // ---------- 作息模板 ----------

    [Fact]
    public void BuiltIn_HasSixEntriesWithDistinctStableIds()
    {
        var builtIn = ExamSlotPreset.BuiltIn;

        Assert.Equal(5, builtIn.Count);
        Assert.All(builtIn, preset => Assert.True(preset.IsBuiltIn));
        Assert.Equal(builtIn.Count, builtIn.Select(preset => preset.Id).Distinct().Count());
        Assert.Equal(ExamSlotPreset.BuiltIn.Select(preset => preset.Id), builtIn.Select(preset => preset.Id));
    }

    [Fact]
    public void BuiltIn_CoversOneThroughFiveSlotsADay()
    {
        var counts = ExamSlotPreset.BuiltIn.Select(preset => preset.SlotCount).ToList();

        Assert.Equal([1, 2, 3, 4, 5], counts);
    }

    [Fact]
    public void FromSlots_ClampsFirstSlotToFixed()
    {
        var dirty = new List<ExamPlanSlot>
        {
            new(new TimeOnly(8, 0), ExamSlotStartMode.AfterPrevious, 20)
        };

        var preset = ExamSlotPreset.FromSlots("我的作息", dirty);

        Assert.Equal(ExamSlotStartMode.Fixed, preset.Slots[0].StartMode);
    }

    [Fact]
    public void Describe_CountsSlots()
    {
        var preset = ExamSlotPreset.FromSlots("两天六场", ExamPlanSlotPresets.ThreePerDay());

        Assert.Equal(3, preset.SlotCount);
        Assert.Contains("3", preset.Describe());
    }

    [Fact]
    public void ResolveName_FallsBackWhenEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(ExamSlotPreset.FromSlots("   ", ExamPlanSlotPresets.TwoPerDay())
            .ResolveName()));
    }

    [Fact]
    public void CreateSlots_IsIndependentCopy()
    {
        var preset = ExamSlotPreset.FromSlots("x", ExamPlanSlotPresets.TwoPerDay());

        var copy = preset.CreateSlots();
        copy[0].FixedStart = new TimeOnly(5, 0);

        Assert.Equal(new TimeOnly(8, 0), preset.Slots[0].FixedStart);
    }

    [Fact]
    public void Service_SaveAndReload_KeepsSlots()
    {
        var config = new InMemoryConfigService();
        var service = new ExamSlotPresetService(config);

        service.Save(ExamSlotPreset.FromSlots("两天六场", ExamPlanSlotPresets.ThreePerDay()));

        var reloaded = new ExamSlotPresetService(config);
        var preset = Assert.Single(reloaded.Presets);
        Assert.Equal("两天六场", preset.Name);
        Assert.Equal(3, preset.SlotCount);
        Assert.Equal(ExamSlotStartMode.AfterPrevious, preset.Slots[1].StartMode);
        Assert.Equal(20, preset.Slots[1].BreakMinutes);
    }

    [Fact]
    public void Service_SaveSameName_OverwritesInPlace()
    {
        var service = new ExamSlotPresetService(new InMemoryConfigService());

        var first = service.Save(ExamSlotPreset.FromSlots("常用", ExamPlanSlotPresets.TwoPerDay()));
        var second = service.Save(ExamSlotPreset.FromSlots("常用", ExamPlanSlotPresets.FivePerDay()));

        Assert.Single(service.Presets);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(5, service.Presets[0].SlotCount);
    }

    [Fact]
    public void Service_SaveSameNameIgnoringCase_Overwrites()
    {
        var service = new ExamSlotPresetService(new InMemoryConfigService());

        service.Save(ExamSlotPreset.FromSlots("Daily", ExamPlanSlotPresets.TwoPerDay()));
        service.Save(ExamSlotPreset.FromSlots("daily", ExamPlanSlotPresets.ThreePerDay()));

        Assert.Single(service.Presets);
        Assert.Equal(3, service.Presets[0].SlotCount);
    }

    [Fact]
    public void Service_All_ListsBuiltInFirstThenUserPresets()
    {
        var service = new ExamSlotPresetService(new InMemoryConfigService());
        service.Save(ExamSlotPreset.FromSlots("我的", ExamPlanSlotPresets.TwoPerDay()));

        var all = service.All;

        Assert.Equal(6, all.Count);
        Assert.All(all.Take(5), preset => Assert.True(preset.IsBuiltIn));
        Assert.False(all[5].IsBuiltIn);
    }

    [Fact]
    public void Service_Remove_RefusesBuiltIn()
    {
        var service = new ExamSlotPresetService(new InMemoryConfigService());
        var builtIn = ExamSlotPreset.BuiltIn[0];

        Assert.False(service.Remove(builtIn.Id));
        Assert.Equal(5, service.All.Count);
    }

    [Fact]
    public void Service_Remove_DropsUserPreset()
    {
        var service = new ExamSlotPresetService(new InMemoryConfigService());
        var saved = service.Save(ExamSlotPreset.FromSlots("我的", ExamPlanSlotPresets.TwoPerDay()));

        Assert.True(service.Remove(saved.Id));
        Assert.Empty(service.Presets);
    }

    [Fact]
    public void Service_Find_LooksThroughBuiltInAndUserPresets()
    {
        var service = new ExamSlotPresetService(new InMemoryConfigService());
        var saved = service.Save(ExamSlotPreset.FromSlots("我的", ExamPlanSlotPresets.TwoPerDay()));

        Assert.NotNull(service.Find(saved.Id));
        Assert.NotNull(service.Find(ExamSlotPreset.BuiltIn[0].Id));
        Assert.Null(service.Find(Guid.NewGuid()));
    }

    [Fact]
    public void Service_RaisesChangedOncePerSave()
    {
        var service = new ExamSlotPresetService(new InMemoryConfigService());
        var count = 0;
        service.Changed += (_, _) => count++;

        service.Save(ExamSlotPreset.FromSlots("a", ExamPlanSlotPresets.TwoPerDay()));
        service.Save(ExamSlotPreset.FromSlots("b", ExamPlanSlotPresets.ThreePerDay()));

        Assert.Equal(2, count);
    }

    [Fact]
    public void Normalize_TruncatesLongName()
    {
        var preset = ExamSlotPreset.FromSlots(new string('x', 100), ExamPlanSlotPresets.TwoPerDay());

        Assert.Equal(ExamSlotPreset.MaxNameLength, preset.Name.Length);
    }

    [Fact]
    public void Normalize_ClampsBreakMinutes()
    {
        var preset = new ExamSlotPreset
        {
            Name = "x",
            Slots =
            [
                new ExamPlanSlot(new TimeOnly(8, 0)),
                new ExamPlanSlot(new TimeOnly(10, 0), ExamSlotStartMode.AfterPrevious, 9999)
            ]
        };

        preset.Normalize();

        Assert.Equal(ExamPlanSlot.MaxBreakMinutes, preset.Slots[1].BreakMinutes);
    }

    [Fact]
    public void ConfigFile_LivesUnderExamDirectory()
    {
        Assert.Equal("slot-presets.json", ExamSlotPresetConfig.StorageFileName);
        Assert.Contains("exam", new ExamSlotPresetConfig().ConfigFilePath);
        Assert.Contains("slot-presets.json", new ExamSlotPresetConfig().ConfigFilePath);
    }

    /// <summary>不碰磁盘的配置服务，只把最后写进来的那份留在内存里。</summary>
    private sealed class InMemoryConfigService : ConfigServiceBase
    {
        private readonly Dictionary<string, ConfigBase> _saved = [];

        public override bool IsConfigExists<T>(T config) => _saved.ContainsKey(config.ConfigFilePath);

        public override T LoadConfig<T>(T config) =>
            _saved.TryGetValue(config.ConfigFilePath, out var saved) && saved is T typed ? typed : config;

        public override void SaveConfig<T>(T config) => _saved[config.ConfigFilePath] = config;

        public override void DeleteConfig<T>(T config) => _saved.Remove(config.ConfigFilePath);
    }
}
