using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Corkboard.Core.Abstraction;
using Corkboard.Shared;
using Corkboard.Shared.Abstraction;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Core.Exam;

/// <summary>
///     一份作息模板：用户自己调好的一张「一天几场、几点开考、中间歇多久」，存下来下次直接选。
///     <para>
///         与 <see cref="ExamPlanTemplate" /> 的区别：那个连科目一起存（整场考试的排法），
///         这个**只管作息**（几点考、歇多久），科目在第 2 步另外勾——所以换了科目组合也能套。
///     </para>
///     <para>
///         内置的那几份（<see cref="ExamPlanSlotPresets" />）在界面上和用户存的一起列出来，
///         但内置的不能删（<see cref="IsBuiltIn" />）。
///     </para>
/// </summary>
public partial class ExamSlotPreset : ObservableObject
{
    /// <summary>名字最多这么长。</summary>
    public const int MaxNameLength = 40;

    /// <summary>作息模板标识。删 / 覆盖都按它认，定下后不改。</summary>
    [ObservableProperty] private Guid _id = Guid.NewGuid();

    /// <summary>名字（「同名即覆盖」的判据）。</summary>
    [ObservableProperty] private string _name = string.Empty;

    /// <summary>一天里各场怎么排。</summary>
    [ObservableProperty] private List<ExamPlanSlot> _slots = [];

    /// <summary>是不是内置的那几份（内置的不许删、不许覆盖）。</summary>
    [JsonIgnore]
    public bool IsBuiltIn { get; init; }

    /// <summary>一天几场。</summary>
    public int SlotCount => Slots.Count;

    /// <summary>列表里显示的名字。</summary>
    public string ResolveName() =>
        string.IsNullOrWhiteSpace(Name) ? CR.Settings_Exam_Plan_SlotPresetDefaultName : Name.Trim();

    /// <summary>「一天 3 场」——下拉里那行说明。</summary>
    public string Describe() => string.Format(CR.Settings_Exam_Plan_SlotPresetSummary, Slots.Count);

    /// <summary>拷成可改的列表（套用时改时刻不该动到模板本身）。</summary>
    public List<ExamPlanSlot> CreateSlots() => ExamPlanSlotPresets.Materialize(Slots);

    /// <summary>拷一份。</summary>
    public ExamSlotPreset Clone() => new()
    {
        Id = Id,
        Name = Name,
        Slots = [.. Slots.Select(slot => slot.Clone())]
    };

    /// <summary>读盘后收拾一遍：名字去空白截断、休息时长夹进合法区间、丢掉空列表。</summary>
    public void Normalize()
    {
        var text = Name?.Trim() ?? string.Empty;
        Name = text.Length <= MaxNameLength ? text : text[..MaxNameLength];

        Slots = [.. (Slots ?? [])
            .Where(slot => slot is not null)
            .Select(slot =>
            {
                var clone = slot.Clone();
                clone.BreakMinutes = ExamPlanSlot.ClampBreak(clone.BreakMinutes);
                return clone;
            })];

        // 第 1 场永远固定时刻（没有「上一场」可以接），读盘时兜一下脏值。
        if (Slots.Count > 0)
            Slots[0].StartMode = ExamSlotStartMode.Fixed;
    }

    /// <summary>把当前这张作息表存成模板（内置那几份在界面上以只读方式出现）。</summary>
    public static ExamSlotPreset FromSlots(string? name, IEnumerable<ExamPlanSlot>? slots)
    {
        var preset = new ExamSlotPreset
        {
            Name = name?.Trim() ?? string.Empty,
            Slots = [.. (slots ?? []).Where(slot => slot is not null).Select(slot => slot.Clone())]
        };
        preset.Normalize();
        return preset;
    }

    /// <summary>
    ///     内置那几份，界面上与用户存的并排显示。
    ///     <para>Id 固定（不是 <see cref="Guid.NewGuid" />），这样下拉里的选中项在重开窗口后还是同一个。</para>
    ///     <para>
    ///         全部用 <see cref="ExamPlanSlotPresets.ForCount" /> 生成：界面上作息只有「一天几场 /
    ///         第一场几点 / 两场之间歇多久」三个数，内置模板必须能由这三个数还原出来，
    ///         否则用户挑了「一天两场」，下拉却匹配不上、看着像没生效。
    ///     </para>
    /// </summary>
    public static IReadOnlyList<ExamSlotPreset> BuiltIn =>
    [
        CreateBuiltIn("00000000-0000-0000-0000-0000000000b1", CR.Settings_Exam_Plan_PresetSingle, 1),
        CreateBuiltIn("00000000-0000-0000-0000-0000000000b2", CR.Settings_Exam_Plan_PresetTwo, 2),
        CreateBuiltIn("00000000-0000-0000-0000-0000000000b3", CR.Settings_Exam_Plan_PresetThree, 3),
        CreateBuiltIn("00000000-0000-0000-0000-0000000000b4", CR.Settings_Exam_Plan_PresetFour, 4),
        CreateBuiltIn("00000000-0000-0000-0000-0000000000b5", CR.Settings_Exam_Plan_PresetFive, 5)
    ];

    /// <summary>内置模板统一从「08:00 开始、每场之间歇 20 分钟」摊出来（与界面的默认值一致）。</summary>
    private static TimeOnly BuiltInFirstStart => new(8, 0);

    private static ExamSlotPreset CreateBuiltIn(string id, string name, int slotCount) =>
        CreateBuiltIn(id, name, ExamPlanSlotPresets.ForCount(slotCount, BuiltInFirstStart, ExamPlanSlot.DefaultBreakMinutes));

    private static ExamSlotPreset CreateBuiltIn(string id, string name, IReadOnlyList<ExamPlanSlot> slots) =>
        new()
        {
            Id = Guid.Parse(id),
            Name = name,
            Slots = ExamPlanSlotPresets.Materialize(slots),
            IsBuiltIn = true
        };
}

/// <summary>全部作息模板装一个文件：<c>data/exam/slot-presets.json</c>。</summary>
public partial class ExamSlotPresetConfig : ConfigBase
{
    public const string StorageFileName = "slot-presets.json";

    /// <summary>用户存下来的作息模板（内置那几份不落盘，见 <see cref="ExamSlotPreset.BuiltIn" />）。</summary>
    [ObservableProperty] private List<ExamSlotPreset> _presets = [];

    [JsonIgnore]
    public override string ConfigFilePath =>
        Utils.GetFilePath(ExamBoardDocument.StorageDirectoryName, StorageFileName);
}

/// <summary>作息模板的读写契约。</summary>
public interface IExamSlotPresetService
{
    /// <summary>用户存的作息模板（新的在前）。</summary>
    ReadOnlyObservableCollection<ExamSlotPreset> Presets { get; }

    /// <summary>内置 + 用户的，界面下拉直接用这个。</summary>
    IReadOnlyList<ExamSlotPreset> All { get; }

    /// <summary>增删改之后发一次。</summary>
    event EventHandler? Changed;

    /// <summary>存一份：同名的就地覆盖，否则插到最前面。</summary>
    ExamSlotPreset Save(ExamSlotPreset preset);

    /// <summary>删掉一份（内置的删不掉）。</summary>
    bool Remove(Guid id);

    /// <summary>按 Id 找一份。</summary>
    ExamSlotPreset? Find(Guid id);
}

/// <summary>
///     作息模板服务：内存一份清单，每次变更整份写回 <c>data/exam/slot-presets.json</c>。
///     <para>与考试模板同一个思路（份数少、单份小，一个文件装完），但两者分开存：作息模板能脱离科目单独复用。</para>
/// </summary>
public sealed class ExamSlotPresetService : IExamSlotPresetService
{
    private readonly ConfigServiceBase _configService;
    private readonly ILogger<ExamSlotPresetService>? _logger;

    private readonly ObservableCollection<ExamSlotPreset> _presets = [];
    private readonly ExamSlotPresetConfig _config;

    public ExamSlotPresetService(ConfigServiceBase configService, ILogger<ExamSlotPresetService>? logger = null)
    {
        _configService = configService;
        _logger = logger;

        Presets = new ReadOnlyObservableCollection<ExamSlotPreset>(_presets);

        _config = _configService.LoadConfig(new ExamSlotPresetConfig());
        LoadAll();
    }

    public ReadOnlyObservableCollection<ExamSlotPreset> Presets { get; }

    public IReadOnlyList<ExamSlotPreset> All => [.. ExamSlotPreset.BuiltIn, .. _presets];

    public event EventHandler? Changed;

    public ExamSlotPreset Save(ExamSlotPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        preset.Normalize();
        if (preset.Id == Guid.Empty)
            preset.Id = Guid.NewGuid();

        var existing = _presets.FirstOrDefault(
            item => string.Equals(item.ResolveName(), preset.ResolveName(), StringComparison.OrdinalIgnoreCase));

        if (existing is not null && existing.Id != preset.Id)
        {
            // 同名即覆盖：Id 留着，外面可能正拿着它的引用。
            existing.Slots = [.. preset.Slots.Select(slot => slot.Clone())];
            Persist();
            return existing;
        }

        if (existing is not null)
        {
            Persist();
            return existing;
        }

        _presets.Insert(0, preset);
        Persist();
        return preset;
    }

    public bool Remove(Guid id)
    {
        if (ExamSlotPreset.BuiltIn.Any(preset => preset.Id == id))
            return false;

        var target = _presets.FirstOrDefault(item => item.Id == id);
        if (target is null)
            return false;

        _presets.Remove(target);
        Persist();
        return true;
    }

    public ExamSlotPreset? Find(Guid id) =>
        All.FirstOrDefault(preset => preset.Id == id);

    private void LoadAll()
    {
        var loaded = _config.Presets ?? [];
        foreach (var preset in loaded)
        {
            if (preset is null)
                continue;

            preset.Normalize();
            if (preset.Id == Guid.Empty)
                preset.Id = Guid.NewGuid();

            _presets.Add(preset);
        }

        if (_presets.Count != loaded.Count)
            _logger?.LogDebug("Dropped {Count} invalid slot presets while loading",
                loaded.Count - _presets.Count);
    }

    private void Persist()
    {
        _config.Presets = [.. _presets];
        _configService.SaveConfig(_config);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
