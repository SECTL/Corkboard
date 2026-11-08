using System.Text.Json;
using Corkboard.Core.DutyRoster;
using Corkboard.Core.Holidays;

namespace Corkboard.Core.Tests;

/// <summary>
///     节假日数据链路：两套上游格式的解析、合并优先级（覆盖 &gt; 在线 &gt; 内置 &gt; 周末）、
///     数据校验拒收、缺失年份降级、缓存与覆盖文件的读写。
/// </summary>
public class HolidayCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "corkboard-holiday-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录删不掉不影响用例结果。
        }
    }

    // ── 解析 ────────────────────────────────────────────────────────────────

    [Fact]
    public void Parser_ReadsHolidayCnShape()
    {
        const string json = """
        {
          "year": 2026,
          "papers": ["https://www.gov.cn/zhengce/2025-11/04/content_1.htm"],
          "days": [
            {"name":"元旦","date":"2026-01-01","isOffDay":true},
            {"name":"元旦","date":"2026-01-02","isOffDay":true},
            {"name":"元旦","date":"2026-01-03","isOffDay":true},
            {"name":"春节","date":"2026-02-16","isOffDay":true},
            {"name":"春节","date":"2026-02-17","isOffDay":true},
            {"name":"春节","date":"2026-02-18","isOffDay":true},
            {"name":"春节","date":"2026-02-19","isOffDay":true},
            {"name":"春节","date":"2026-02-20","isOffDay":true},
            {"name":"春节","date":"2026-02-21","isOffDay":true},
            {"name":"春节","date":"2026-02-22","isOffDay":true},
            {"name":"春节补班","date":"2026-02-14","isOffDay":false}
          ]
        }
        """;

        Assert.True(HolidayJsonParser.TryParse(json, 2020, out var data));
        Assert.NotNull(data);
        Assert.Equal(2026, data!.Year);
        Assert.Equal("holiday-cn", data.SourceName);
        Assert.Single(data.Papers);
        Assert.Equal(11, data.Days.Count);
        Assert.Equal(10, data.OffDayCount);
        Assert.Equal(1, data.MakeupDayCount);
        Assert.Equal("元旦", data.Find(new DateOnly(2026, 1, 1))!.Name);
        Assert.True(HolidayDataRules.IsPlausible(data, out _));
    }

    [Fact]
    public void Parser_ReadsChineseDaysShape()
    {
        const string json = """
        {
          "year": 2026,
          "holidays": {
            "2026-01-01": "New Year's Day,元旦,1",
            "2026-01-02": "New Year's Day,元旦,1",
            "2026-01-03": "New Year's Day,元旦,1",
            "2026-02-16": "Spring Festival,春节,1",
            "2026-02-17": "Spring Festival,春节,1",
            "2026-02-18": "Spring Festival,春节,1",
            "2026-02-19": "Spring Festival,春节,1",
            "2026-02-20": "Spring Festival,春节,1",
            "2026-02-21": "Spring Festival,春节,1",
            "2026-02-22": "Spring Festival,春节,1"
          },
          "workdays": {
            "2026-02-14": "Spring Festival,春节,0"
          }
        }
        """;

        Assert.True(HolidayJsonParser.TryParse(json, 2020, out var data));
        Assert.NotNull(data);
        Assert.Equal("chinese-days", data!.SourceName);
        Assert.Equal(10, data.OffDayCount);
        Assert.Equal(1, data.MakeupDayCount);
        Assert.False(data.Find(new DateOnly(2026, 2, 14))!.IsOffDay);
    }

    [Fact]
    public void DataRules_RejectImplausibleYear()
    {
        Assert.False(HolidayDataRules.IsPlausible(null, out _));

        var tooFew = MakeYear(2026, Enumerable.Range(1, 5).Select(day => (new DateOnly(2026, 1, day), true)));
        Assert.False(HolidayDataRules.IsPlausible(tooFew, out _));

        var tooMany = MakeYear(2026, Enumerable.Range(1, 81).Select(day => (new DateOnly(2026, 1, 1).AddDays(day), true)));
        Assert.False(HolidayDataRules.IsPlausible(tooMany, out _));

        var tooManyMakeup = MakeYear(
            2026,
            Enumerable.Range(1, 20).Select(day => (new DateOnly(2026, 1, 1).AddDays(day), false))
                .Append((new DateOnly(2026, 3, 1), true)));
        Assert.False(HolidayDataRules.IsPlausible(tooManyMakeup, out _));

        var noOffDay = MakeYear(2026, Enumerable.Range(1, 10).Select(day => (new DateOnly(2026, 1, 1).AddDays(day), false)));
        Assert.False(HolidayDataRules.IsPlausible(noOffDay, out _));

        Assert.True(HolidayDataRules.IsPlausible(MakeYear(2026, Enumerable.Range(1, 10).Select(day => (new DateOnly(2026, 1, day), true))), out _));
    }

    [Fact]
    public void DataRules_YearToleranceIsOneYear()
    {
        var data = MakeYear(
            2025,
            Enumerable.Range(1, 10).Select(day => (new DateOnly(2025, 12, day), true)));

        Assert.True(HolidayDataRules.IsYearConsistent(data, 2026));
        Assert.True(HolidayDataRules.IsYearConsistent(data, 2025));
        Assert.False(HolidayDataRules.IsYearConsistent(data, 2028));
    }

    // ── 合并优先级 ──────────────────────────────────────────────────────────

    [Fact]
    public void Catalog_OnlineBeatsBuiltin_AndManualBeatsOnline()
    {
        var builtin = new Dictionary<int, HolidayYearData>
        {
            [2026] = MakeYear(
                2026,
                Enumerable.Range(1, 10).Select(day => (new DateOnly(2026, 1, day), true))
                    .Append((new DateOnly(2026, 2, 1), true)))
        };

        var online = new Dictionary<int, HolidayYearData>
        {
            [2026] = MakeYear(
                2026,
                Enumerable.Range(1, 10).Select(day => (new DateOnly(2026, 1, day), true))
                    .Append((new DateOnly(2026, 1, 1), false)))
        };

        var overrides = new HolidayOverrides();
        overrides.Days.Add(new HolidayOverride
        {
            Date = new DateOnly(2026, 1, 2),
            State = HolidayOverrideState.Off,
            Note = "校庆"
        });

        var catalog = new HolidayCatalog(overrides, online, builtin);

        // 在线文件把 1 月 1 日改成补班：在线赢内置。
        var jan1 = catalog.Resolve(new DateOnly(2026, 1, 1));
        Assert.False(jan1.IsOffDay);
        Assert.Equal(HolidayOrigin.Online, jan1.Origin);
        Assert.True(catalog.IsMakeupWorkday(new DateOnly(2026, 1, 1)));

        // 手动覆盖赢在线。
        var jan2 = catalog.Resolve(new DateOnly(2026, 1, 2));
        Assert.True(jan2.IsOffDay);
        Assert.Equal(HolidayOrigin.Manual, jan2.Origin);
        Assert.Equal(HolidayOverrideState.Off, jan2.OverrideState);

        // 在线文件没提到的日期回落到内置。
        var feb1 = catalog.Resolve(new DateOnly(2026, 2, 1));
        Assert.True(feb1.IsOffDay);
        Assert.Equal(HolidayOrigin.Builtin, feb1.Origin);
    }

    [Fact]
    public void Catalog_OverrideWorkAndIgnore()
    {
        var online = new Dictionary<int, HolidayYearData>
        {
            [2026] = MakeYear(2026, Enumerable.Range(1, 10).Select(day => (new DateOnly(2026, 1, day), true)))
        };

        var overrides = new HolidayOverrides();
        overrides.Days.Add(new HolidayOverride
        {
            Date = new DateOnly(2026, 1, 3),
            State = HolidayOverrideState.Work
        });
        overrides.Days.Add(new HolidayOverride
        {
            // 2026-01-10 是周六：忽略数据源后退回周末规则 = 休。
            Date = new DateOnly(2026, 1, 10),
            State = HolidayOverrideState.Ignore
        });

        var catalog = new HolidayCatalog(overrides, online, builtin: null);

        Assert.False(catalog.IsLegalHoliday(new DateOnly(2026, 1, 3)));
        Assert.True(catalog.IsMakeupWorkday(new DateOnly(2026, 1, 3)));

        Assert.True(catalog.IsLegalHoliday(new DateOnly(2026, 1, 10)));
        Assert.False(catalog.IsMakeupWorkday(new DateOnly(2026, 1, 10)));
        Assert.False(catalog.HasData(2027));
    }

    [Fact]
    public void Catalog_NextYearFileCoversDecemberDates()
    {
        var online = new Dictionary<int, HolidayYearData>
        {
            [2027] = MakeYear(
                2027,
                Enumerable.Range(1, 9).Select(day => (new DateOnly(2027, 1, day), true))
                    .Append((new DateOnly(2026, 12, 31), true)))
        };

        var catalog = new HolidayCatalog(online: online);

        var dec31 = catalog.Resolve(new DateOnly(2026, 12, 31));
        Assert.True(dec31.IsOffDay);
        Assert.Equal(HolidayOrigin.Online, dec31.Origin);
    }

    [Fact]
    public void Catalog_MissingYearFallsBackToWeekendRule()
    {
        var catalog = new HolidayCatalog();

        var saturday = new DateOnly(2028, 1, 1);
        Assert.Equal(DayOfWeek.Saturday, saturday.DayOfWeek);
        Assert.True(catalog.IsLegalHoliday(saturday));
        Assert.Equal(HolidayOrigin.Weekend, catalog.Resolve(saturday).Origin);

        var friday = new DateOnly(2027, 12, 31);
        Assert.False(catalog.IsLegalHoliday(friday));
        Assert.False(catalog.IsMakeupWorkday(friday));
        Assert.Equal(HolidayOrigin.None, catalog.Resolve(friday).Origin);

        Assert.False(catalog.HasData(2028));
        Assert.Null(catalog.GetAttribution(2028));
    }

    [Fact]
    public void Catalog_VacationRange_IsAlwaysSkipped()
    {
        var overrides = new HolidayOverrides();
        overrides.Vacations.Add(new HolidayVacationRange
        {
            Start = new DateOnly(2026, 7, 1),
            End = new DateOnly(2026, 8, 31),
            Note = "暑假"
        });

        var catalog = new HolidayCatalog(overrides);
        Assert.True(catalog.IsVacation(new DateOnly(2026, 7, 15)));
        Assert.False(catalog.IsVacation(new DateOnly(2026, 9, 1)));

        var schedule = new DutyScheduleConfig { SkipLegalHolidays = false };
        Assert.False(DutyCalendar.IsDutyDay(new DateOnly(2026, 7, 15), schedule, catalog));
    }

    [Fact]
    public void Catalog_GetDays_IncludesManualEntries()
    {
        var online = new Dictionary<int, HolidayYearData>
        {
            [2026] = MakeYear(2026, Enumerable.Range(1, 10).Select(day => (new DateOnly(2026, 1, day), true)))
        };

        var overrides = new HolidayOverrides();
        overrides.Days.Add(new HolidayOverride
        {
            Date = new DateOnly(2026, 3, 2),
            State = HolidayOverrideState.Off,
            Note = "校运会"
        });

        var catalog = new HolidayCatalog(overrides, online);
        var days = catalog.GetDays(2026);

        Assert.Equal(11, days.Count);
        var manual = days.Single(day => day.Date == new DateOnly(2026, 3, 2));
        Assert.True(manual.IsOffDay);
        Assert.Equal(HolidayOrigin.Manual, manual.Origin);
        Assert.Equal("校运会", manual.Note);
    }

    [Fact]
    public void SourceCatalog_CandidatesPutMirrorFirstAndBackupLast()
    {
        var candidates = HolidaySourceCatalog.Candidates(2027);

        Assert.Equal(4, candidates.Count);
        Assert.Contains("ghproxy.sectl.cn", candidates[0].Url);
        Assert.True(candidates[0].IsMirror);
        Assert.Equal(HolidaySourceCatalog.ChineseDaysName, candidates[^1].SourceName);
        Assert.True(candidates[^1].IsBackup);
        Assert.Contains("2027.json", candidates[0].Url);
    }

    // ── 缓存读写 ────────────────────────────────────────────────────────────

    [Fact]
    public void Cache_YearRoundTrip_AndFaultTolerance()
    {
        var store = new HolidayCacheStore(_root);
        var fetchedAt = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        var state = new HolidayYearState
        {
            ETag = "\"abc123\"",
            LastCheckUtc = fetchedAt,
            FetchedAtUtc = fetchedAt,
            SourceName = HolidaySourceCatalog.HolidayCnName
        };

        Assert.True(store.SaveYear(2026, HolidayCnJson(2026), state));

        Assert.True(store.TryLoadYear(2026, out var loaded));
        Assert.NotNull(loaded);
        Assert.Equal(2026, loaded!.Year);
        Assert.Equal(10, loaded.Days.Count);
        Assert.Equal(fetchedAt, loaded.FetchedAt);

        var reloadedState = store.LoadState();
        Assert.Equal("\"abc123\"", reloadedState.Years[2026].ETag);

        // 文件坏了：按没有缓存处理，不抛异常。
        File.WriteAllText(store.YearFilePath(2027), "{ 这不是 JSON");
        Assert.False(store.TryLoadYear(2027, out _));

        // 状态文件坏了也同理。
        File.WriteAllText(store.StateFilePath, "not json");
        Assert.Empty(store.LoadState().Years);
    }

    [Fact]
    public void Cache_OverridesRoundTrip()
    {
        var store = new HolidayCacheStore(_root);
        var overrides = new HolidayOverrides();
        overrides.Days.Add(new HolidayOverride
        {
            Date = new DateOnly(2026, 1, 2),
            State = HolidayOverrideState.Work,
            Note = "调休"
        });
        overrides.Vacations.Add(new HolidayVacationRange
        {
            Start = new DateOnly(2026, 2, 1),
            End = new DateOnly(2026, 2, 20),
            Note = "寒假"
        });

        Assert.True(store.SaveOverrides(overrides));

        var loaded = store.LoadOverrides();
        Assert.Single(loaded.Days);
        Assert.Equal(HolidayOverrideState.Work, loaded.Days[0].State);
        Assert.Equal(new DateOnly(2026, 1, 2), loaded.Days[0].Date);
        Assert.Single(loaded.Vacations);
        Assert.True(loaded.IsVacation(new DateOnly(2026, 2, 10)));

        // 覆盖文件坏了：返回空覆盖，不抛异常。
        File.WriteAllText(store.OverridesFilePath, "[]");
        Assert.True(store.LoadOverrides().IsEmpty);
    }

    [Fact]
    public void Cache_LoadAllYearsIgnoresNonYearFiles()
    {
        var store = new HolidayCacheStore(_root);
        Assert.True(store.SaveYear(2026, HolidayCnJson(2026), new HolidayYearState
        {
            FetchedAtUtc = DateTimeOffset.UtcNow
        }));
        Directory.CreateDirectory(store.OnlineDirectory);
        File.WriteAllText(Path.Combine(store.OnlineDirectory, "notes.json"), "{}");

        var all = store.LoadAllYears();
        Assert.Single(all);
        Assert.True(all.ContainsKey(2026));
    }

    private static HolidayYearData MakeYear(int year, IEnumerable<(DateOnly Date, bool IsOffDay)> days) => new()
    {
        Year = year,
        SourceName = "holiday-cn",
        SourceUrl = HolidaySourceCatalog.HolidayCnRepoUrl,
        License = "MIT",
        FetchedAt = DateTimeOffset.UnixEpoch,
        Days = days.Select(item => new HolidayDay
        {
            Date = item.Date,
            Name = item.IsOffDay ? "假日" : "补班",
            IsOffDay = item.IsOffDay
        }).ToList()
    };

    private static string HolidayCnJson(int year)
    {
        var payload = new
        {
            year,
            papers = new[] { "https://www.gov.cn/zhengce/2025-11/04/content_1.htm" },
            days = Enumerable.Range(1, 10)
                .Select(day => new { name = "元旦", date = $"{year}-01-{day:00}", isOffDay = true })
                .ToArray()
        };

        return JsonSerializer.Serialize(payload);
    }
}
